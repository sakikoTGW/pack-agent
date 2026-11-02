/**
 * pad-gateway jump channel (host side).
 *
 * A tiny authenticated queue used by PAD to hand a session target to the page
 * runtime: PAD POSTs an event, the page's client plugin long-polls it, then
 * calls `sessions.open()`. Reuses the gateway's loopback HTTP carrier + bearer
 * token — no extra port, no extra process.
 *
 * Transport contract (see agent-pack-dsh/docs/session-jump.md §7.1):
 *   POST /pad/jump        {sessionId}            -> {ok, seq}
 *   GET  /pad/jump?since=N                        -> event JSON | 204 (timeout)
 *   POST /pad/jump/ack    {seq, ok}               -> {ok:true}
 *   OPTIONS /pad/jump*                            -> CORS preflight
 *
 * The page origin may be a loopback http(s) origin (browser) or `dsh-app://app`
 * (official desktop renderer); anything else is rejected.
 */
import { timingSafeEqual } from "node:crypto";

export const JUMP_SCHEMA = "pack-agent.pad-jump/v1";
export const JUMP_DEFAULT_TTL_MS = 30_000;
export const JUMP_DEFAULT_TIMEOUT_MS = 25_000;
export const JUMP_MAX_QUEUE = 20;
export const JUMP_SESSION_ID = /^session-[A-Za-z0-9][A-Za-z0-9-]{7,63}$/;

const LOOPBACK_ORIGIN = /^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/;
const DESKTOP_ORIGIN = "dsh-app://app";

/**
 * @param {string} id raw session id
 * @returns {boolean} whether it is a canonical DSH session id
 */
export function isSessionId(id) {
  return typeof id === "string" && JUMP_SESSION_ID.test(id);
}

/**
 * @param {string} origin request Origin header
 * @returns {boolean} whether the origin may read jump responses
 */
export function isAllowedOrigin(origin) {
  if (typeof origin !== "string" || origin === "") return false;
  return origin === DESKTOP_ORIGIN || LOOPBACK_ORIGIN.test(origin);
}

/**
 * Create the in-memory jump queue.
 * @param {{ttlMs?: number, timeoutMs?: number, now?: () => number}} [options]
 */
export function createJumpStore(options = {}) {
  const ttlMs = options.ttlMs ?? JUMP_DEFAULT_TTL_MS;
  const timeoutMs = options.timeoutMs ?? JUMP_DEFAULT_TIMEOUT_MS;
  const now = options.now ?? Date.now;
  /** @type {{schema: string, seq: number, sessionId: string, at: number}[]} */
  const events = [];
  const waiters = new Set();
  let seq = 0;

  function prune(at) {
    while (events.length > 0 && at - events[0].at > ttlMs) events.shift();
  }

  /**
   * Enqueue one jump event and wake every waiting long-poll.
   * @param {string} sessionId canonical session id
   */
  function publish(sessionId) {
    if (!isSessionId(sessionId)) throw new Error("pad-jump: invalid session id");
    const at = now();
    prune(at);
    const event = { schema: JUMP_SCHEMA, seq: ++seq, sessionId, at };
    events.push(event);
    if (events.length > JUMP_MAX_QUEUE) events.splice(0, events.length - JUMP_MAX_QUEUE);
    for (const waiter of [...waiters]) waiter.settle(event);
    return event;
  }

  /**
   * Long-poll for the first event after `since`.
   * @param {number} since last consumed seq
   * @returns {Promise<object|null>} event, or null on timeout/dispose
   */
  function wait(since) {
    const at = now();
    prune(at);
    const found = events.find((event) => event.seq > since);
    if (found !== undefined) return Promise.resolve(found);
    return new Promise((resolve) => {
      const waiter = {
        settle(event) {
          clearTimeout(timer);
          waiters.delete(waiter);
          resolve(event);
        },
      };
      const timer = setTimeout(() => {
        waiters.delete(waiter);
        resolve(null);
      }, timeoutMs);
      timer.unref?.();
      waiters.add(waiter);
    });
  }

  function dispose() {
    for (const waiter of [...waiters]) waiter.settle(null);
    events.length = 0;
  }

  return {
    publish,
    wait,
    dispose,
    get lastSeq() {
      return seq;
    },
    get size() {
      return events.length;
    },
  };
}

/**
 * Build the HTTP handler for the jump endpoints.
 * @param {{store: ReturnType<typeof createJumpStore>, token: string}} options
 * @returns {(req: import("node:http").IncomingMessage, res: import("node:http").ServerResponse) => Promise<boolean>}
 */
export function createJumpHttpHandler({ store, token }) {
  return async function handleJump(req, res) {
    const url = new URL(req.url ?? "/", "http://127.0.0.1");
    if (!url.pathname.startsWith("/pad/jump")) return false;

    const origin = req.headers.origin;
    if (typeof origin === "string" && origin !== "") {
      if (!isAllowedOrigin(origin)) {
        sendJson(res, 403, { error: "origin not allowed" });
        return true;
      }
      res.setHeader("access-control-allow-origin", origin);
      res.setHeader("vary", "origin");
      res.setHeader("access-control-allow-methods", "GET, POST, OPTIONS");
      res.setHeader("access-control-allow-headers", "authorization, content-type");
      res.setHeader("access-control-max-age", "600");
    }
    if (req.method === "OPTIONS") {
      res.writeHead(204);
      res.end();
      return true;
    }
    if (!authorized(req.headers.authorization, token)) {
      sendJson(res, 401, { error: "unauthorized" });
      return true;
    }

    if (url.pathname === "/pad/jump" && req.method === "POST") {
      let body;
      try {
        body = await readJson(req);
      } catch {
        sendJson(res, 400, { error: "invalid json" });
        return true;
      }
      const sessionId = body?.sessionId;
      if (!isSessionId(sessionId)) {
        sendJson(res, 400, { error: "invalid session id" });
        return true;
      }
      const event = store.publish(sessionId);
      sendJson(res, 200, { ok: true, seq: event.seq });
      return true;
    }

    if (url.pathname === "/pad/jump" && req.method === "GET") {
      const since = Number(url.searchParams.get("since") ?? "0");
      const event = await store.wait(Number.isFinite(since) ? since : 0);
      if (event === null) {
        res.writeHead(204);
        res.end();
        return true;
      }
      sendJson(res, 200, event);
      return true;
    }

    if (url.pathname === "/pad/jump/ack" && req.method === "POST") {
      try {
        await readJson(req);
      } catch {
        sendJson(res, 400, { error: "invalid json" });
        return true;
      }
      sendJson(res, 200, { ok: true });
      return true;
    }

    sendJson(res, 404, { error: "not found" });
    return true;
  };
}

function authorized(header, token) {
  if (typeof header !== "string" || !header.startsWith("Bearer ")) return false;
  const got = header.slice(7).trim();
  const a = Buffer.from(got);
  const b = Buffer.from(token);
  return a.length === b.length && timingSafeEqual(a, b);
}

function sendJson(res, status, value) {
  res.writeHead(status, { "content-type": "application/json" });
  res.end(JSON.stringify(value));
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > 64 * 1024) {
        reject(new Error("body too large"));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8").trim();
      if (raw === "") return resolve({});
      try {
        resolve(JSON.parse(raw));
      } catch (error) {
        reject(error);
      }
    });
    req.on("error", reject);
  });
}
