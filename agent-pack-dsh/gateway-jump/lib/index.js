import { createServer } from "node:http";
import { randomBytes, timingSafeEqual } from "node:crypto";
import { appendFileSync, chmodSync, existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { createJumpHttpHandler, createJumpStore } from "./jump.mjs";

/**
 * Jump-only loopback carrier for DeepSeek Harness 0.2.x.
 *
 * The full `@sakikotgw/pad-gateway` mounts the official apiproxy too; on 0.2.x
 * apiproxy has no matching npm package, so installing it is refused and its
 * bundle patch cannot load. This carrier serves only the jump channel:
 *
 *   GET  /pad/ping
 *   POST /pad/jump
 *   GET  /pad/jump?since=N
 *   POST /pad/jump/ack
 *
 * It writes the same `<DSH_HOME>/pad-gateway.json` ad shape (url points to
 * `/api` for compatibility with PAD's ad reader; endpoints live on the origin).
 */
const name = "pad-gateway-jump";
const inject = [];
const GATEWAY_FILE = "pad-gateway.json";
const GATEWAY_LOG = "pad-gateway.log";
const GATEWAY_SCHEMA = "pack-agent.pad-gateway/v1";

function tokenOk(got, expected) {
  const a = Buffer.from(got);
  const b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

function bearer(req) {
  const raw = req.headers.authorization;
  if (typeof raw !== "string") return "";
  return raw.startsWith("Bearer ") ? raw.slice(7).trim() : "";
}

function apply(ctx, config = {}) {
  const home = (config.home ?? process.env.DSH_HOME ?? "").trim();
  const trace = (line) => {
    const stamped = `${new Date().toISOString()} ${line}\n`;
    ctx.logger?.info?.(line);
    if (!home) {
      process.stderr.write(`[pad-gateway-jump] ${stamped}`);
      return;
    }
    try {
      appendFileSync(join(home, GATEWAY_LOG), stamped);
    } catch {}
  };
  if (!home) {
    trace("no DSH_HOME, pad gateway-jump not started");
    return;
  }
  const token = randomBytes(32).toString("hex");
  const file = join(home, GATEWAY_FILE);
  const jumpStore = createJumpStore({
    ttlMs: Number(process.env.PAD_GATEWAY_JUMP_TTL_MS ?? 0) || undefined,
    timeoutMs: Number(process.env.PAD_GATEWAY_JUMP_TIMEOUT_MS ?? 0) || undefined,
  });
  const handleJump = createJumpHttpHandler({ store: jumpStore, token });
  let server;
  let listenPort;
  let webUrl;
  const adUrl = () => `http://127.0.0.1:${listenPort ?? 0}/api`;
  /**
   * Rewrite the ad. Once the host web listener is bound, `webUrl` points at it:
   * 0.2.x serves the same `client-request` gateway protocol under `/api/<method>`,
   * so PAD can read sessions directly instead of having them proxied here.
   */
  const writeAd = () => {
    if (listenPort === undefined) return;
    const ad = {
      schema: GATEWAY_SCHEMA,
      url: adUrl(),
      token,
      pid: process.pid,
      capabilities: { jump: true, api: false },
      ...(webUrl ? { webUrl, apiStyle: "dsh-0.2" } : {}),
      ...(process.env.DSH_PROFILE ? { profile: process.env.DSH_PROFILE } : {}),
    };
    writeFileSync(file, `${JSON.stringify(ad, null, 2)}\n`, { encoding: "utf8", mode: 384 });
    try {
      chmodSync(file, 384);
    } catch {}
  };
  /** Learn the web listener origin from the host service and publish it in the ad. */
  const attachWebUrl = () => {
    try {
      ctx.inject?.(["webServer"], (services) => {
        const webServer = services?.webServer;
        if (typeof webServer?.getOrigin !== "function") return;
        const publish = () => {
          try {
            const origin = webServer.getOrigin();
            if (typeof origin !== "string" || origin.length === 0) return false;
            webUrl = origin;
            writeAd();
            trace(`web api origin attached: ${origin}`);
            return true;
          } catch {
            return false;
          }
        };
        if (publish()) return;
        const timer = setInterval(() => {
          if (publish()) clearInterval(timer);
        }, 300);
        timer.unref?.();
        ctx.on?.("dispose", () => clearInterval(timer));
      });
    } catch (err) {
      trace(`webServer injection failed: ${err instanceof Error ? err.message : String(err)}`);
    }
  };
  trace(`apply() in pid ${process.pid}`);
  const boot = async () => {
    server = createServer((req, res) => {
      (async () => {
        try {
          if (await handleJump(req, res)) return;
          if (!tokenOk(bearer(req), token)) {
            res.writeHead(401, { "content-type": "application/json" });
            res.end(JSON.stringify({ error: "unauthorized" }));
            return;
          }
          const path = (req.url ?? "/").split("?")[0];
          if (path === "/pad/ping") {
            res.writeHead(200, { "content-type": "application/json" });
            res.end(
              JSON.stringify({
                ok: true,
                pid: process.pid,
                profile: process.env.DSH_PROFILE ?? null,
                schema: GATEWAY_SCHEMA,
                capabilities: { jump: true, api: false },
              }),
            );
            return;
          }
          res.writeHead(503, { "content-type": "application/json" });
          res.end(JSON.stringify({ error: "apiproxy not part of pad-gateway-jump" }));
        } catch (err) {
          if (!res.headersSent) res.writeHead(500, { "content-type": "text/plain; charset=utf-8" });
          res.end(String(err instanceof Error ? err.message : err));
        }
      })();
    });
    await new Promise((resolve, reject) => {
      server.once("error", reject);
      server.listen(0, "127.0.0.1", () => resolve());
    });
    const addr = server.address();
    if (!addr || typeof addr === "string") throw new Error("pad gateway-jump failed to bind");
    listenPort = addr.port;
    writeAd();
    trace(`listening on ${adUrl()} (jump channel ready, /api served from the web listener when available)`);
    attachWebUrl();
  };
  boot().catch((err) => {
    const detail = err instanceof Error ? `${err.message}\n${err.stack ?? ""}` : String(err);
    trace(`boot failed: ${detail}`);
    ctx.logger?.warn?.(`[pack-agent] pad gateway-jump failed: ${detail}`);
  });
  ctx.on?.("dispose", () => {
    jumpStore.dispose();
    server?.close();
    try {
      if (existsSync(file)) {
        const cur = JSON.parse(readFileSync(file, "utf8"));
        if (cur.pid === process.pid && cur.token === token) rmSync(file, { force: true });
      }
    } catch {
      rmSync(file, { force: true });
    }
  });
}

export { apply, inject, name, GATEWAY_SCHEMA, GATEWAY_FILE, GATEWAY_LOG };
export default apply;
