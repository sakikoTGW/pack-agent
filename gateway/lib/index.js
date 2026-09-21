import { createRequire } from "node:module";
var __require = /* @__PURE__ */ createRequire(import.meta.url);

// agent-pack-dsh/gateway/src/index.ts
import { createServer } from "node:http";
import { randomBytes, timingSafeEqual } from "node:crypto";
import { appendFileSync, chmodSync, existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
var name = "pack-agent-pad-gateway";
var inject = ["apiProxy"];
var GATEWAY_FILE = "pad-gateway.json";
var GATEWAY_LOG = "pad-gateway.log";
var GATEWAY_SCHEMA = "pack-agent.pad-gateway/v1";
function tokenOk(got, expected) {
  const a = Buffer.from(got);
  const b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}
function bearer(req) {
  const raw = req.headers.authorization;
  if (typeof raw !== "string")
    return "";
  return raw.startsWith("Bearer ") ? raw.slice(7).trim() : "";
}
function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (c) => {
      size += c.length;
      if (size > 8 * 1024 * 1024) {
        reject(new Error("request body too large"));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks)));
    req.on("error", reject);
  });
}
async function toRequest(req) {
  const url = `http://127.0.0.1${req.url ?? "/"}`;
  const headers = new Headers;
  for (const [k, v] of Object.entries(req.headers)) {
    if (typeof v === "string")
      headers.set(k, v);
    else if (Array.isArray(v))
      headers.set(k, v.join(", "));
  }
  const method = req.method ?? "GET";
  const init = { method, headers };
  if (method !== "GET" && method !== "HEAD") {
    const body = await readBody(req);
    if (body.length > 0)
      init.body = body;
  }
  const ac = new AbortController;
  req.on("close", () => ac.abort());
  init.signal = ac.signal;
  return new Request(url, init);
}
async function writeResponse(res, response) {
  const headers = {};
  response.headers.forEach((value, key) => {
    headers[key] = value;
  });
  res.writeHead(response.status, headers);
  if (!response.body) {
    res.end();
    return;
  }
  const reader = response.body.getReader();
  try {
    for (;; ) {
      const { done, value } = await reader.read();
      if (done)
        break;
      if (value)
        res.write(Buffer.from(value));
    }
  } catch {} finally {
    res.end();
  }
}
function apply(ctx, config = {}) {
  const home = (config.home ?? process.env.DSH_HOME ?? "").trim();
  const trace = (line) => {
    const stamped = `${new Date().toISOString()} ${line}
`;
    ctx.logger?.info?.(line);
    if (!home) {
      process.stderr.write(`[pad-gateway] ${stamped}`);
      return;
    }
    try {
      appendFileSync(join(home, GATEWAY_LOG), stamped);
    } catch {}
  };
  if (!home) {
    trace("no DSH_HOME, pad gateway not started");
    ctx.logger?.warn?.("[pack-agent] no DSH_HOME, pad gateway not started");
    return;
  }
  const token = randomBytes(32).toString("hex");
  const file = join(home, GATEWAY_FILE);
  let server;
  trace(`apply() in pid ${process.pid}`);
  const boot = async () => {
    trace("importing @deepseek-ai/dsh-host-apiproxy");
    const mod = await import("@deepseek-ai/dsh-host-apiproxy");
    if (typeof mod.toFetchHandler !== "function") {
      throw new Error("apiproxy has no toFetchHandler export");
    }
    if (!ctx.apiProxy)
      throw new Error("ctx.apiProxy is missing despite inject");
    const handler = mod.toFetchHandler(ctx.apiProxy);
    trace("toFetchHandler ready");
    server = createServer((req, res) => {
      (async () => {
        try {
          if (!tokenOk(bearer(req), token)) {
            res.writeHead(401, { "content-type": "application/json" });
            res.end(JSON.stringify({ error: "unauthorized" }));
            return;
          }
          const path = (req.url ?? "/").split("?")[0];
          if (path === "/pad/ping") {
            res.writeHead(200, { "content-type": "application/json" });
            res.end(JSON.stringify({
              ok: true,
              pid: process.pid,
              profile: process.env.DSH_PROFILE ?? null,
              schema: GATEWAY_SCHEMA
            }));
            return;
          }
          await writeResponse(res, await handler.fetch(await toRequest(req)));
        } catch (err) {
          if (!res.headersSent)
            res.writeHead(500, { "content-type": "text/plain; charset=utf-8" });
          res.end(String(err instanceof Error ? err.message : err));
        }
      })();
    });
    await new Promise((resolve, reject) => {
      server.once("error", reject);
      server.listen(0, "127.0.0.1", () => resolve());
    });
    const addr = server.address();
    if (!addr || typeof addr === "string")
      throw new Error("pad gateway failed to bind");
    const ad = {
      schema: GATEWAY_SCHEMA,
      url: `http://127.0.0.1:${addr.port}/api`,
      token,
      pid: process.pid,
      ...process.env.DSH_PROFILE ? { profile: process.env.DSH_PROFILE } : {}
    };
    writeFileSync(file, `${JSON.stringify(ad, null, 2)}
`, { encoding: "utf8", mode: 384 });
    try {
      chmodSync(file, 384);
    } catch {}
    trace(`listening on ${ad.url}`);
  };
  boot().catch((err) => {
    const detail = err instanceof Error ? `${err.message}
${err.stack ?? ""}` : String(err);
    trace(`boot failed: ${detail}`);
    ctx.logger?.warn?.(`[pack-agent] pad gateway failed: ${detail}`);
  });
  ctx.on?.("dispose", () => {
    server?.close();
    try {
      if (existsSync(file)) {
        const cur = JSON.parse(readFileSync(file, "utf8"));
        if (cur.pid === process.pid && cur.token === token)
          rmSync(file, { force: true });
      }
    } catch {
      rmSync(file, { force: true });
    }
  });
}
var src_default = apply;
export {
  name,
  inject,
  src_default as default,
  apply,
  GATEWAY_SCHEMA,
  GATEWAY_LOG,
  GATEWAY_FILE
};
