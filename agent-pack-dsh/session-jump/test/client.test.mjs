import test from "node:test";
import assert from "node:assert/strict";
import { pathToFileURL } from "node:url";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const clientPath = resolve(here, "..", "lib", "client.js");

const TOKEN = "t".repeat(32);
const GATEWAY = "http://127.0.0.1:65432/api";
const UUID = "02d4a5ad-9792-4569-a2de-d598e85d754a";
const TARGET = `session-${UUID}`;

/**
 * Load the browser bundle against a fake page/runtime.
 * @param {{search?: string, phase?: string, byId?: object, current?: string, gateway?: object, fetchImpl?: Function}} opts
 */
async function loadClient(opts = {}) {
  const page = {
    calls: { open: [], replaced: [], events: [] },
    disposers: [],
    state: {
      phase: opts.phase ?? "ready",
      byId: opts.byId ?? {},
      current: opts.current,
    },
  };
  const search = opts.search ?? "";
  const href = `http://127.0.0.1:3080/${search}`;
  let factory = null;

  const previousFetch = globalThis.fetch;
  const previousGateway = globalThis.__PAD_SESSION_JUMP__;
  const previousBoot = globalThis.__PAD_SESSION_JUMP_BOOT__;
  if (opts.fetchImpl !== undefined) globalThis.fetch = opts.fetchImpl;
  if (opts.gateway !== undefined) globalThis.__PAD_SESSION_JUMP__ = opts.gateway;
  if (opts.bootSearch !== undefined || opts.bootHash !== undefined)
    globalThis.__PAD_SESSION_JUMP_BOOT__ = { search: opts.bootSearch ?? "", hash: opts.bootHash ?? "" };
  else delete globalThis.__PAD_SESSION_JUMP_BOOT__;

  globalThis.CustomEvent = class CustomEvent {
    constructor(type, init) {
      this.type = type;
      this.detail = init?.detail;
    }
  };
  globalThis.window = {
    location: { search, pathname: "/", hash: "", href },
    history: {
      state: null,
      replaceState(_state, _title, next) {
        page.calls.replaced.push(next);
      },
    },
    __ModuleLoader__: {
      load(def) {
        factory = def.factory;
      },
    },
    dispatchEvent(event) {
      page.calls.events.push(event.detail);
      return true;
    },
  };

  await import(`${pathToFileURL(clientPath).href}?t=${Math.random()}`);
  assert.equal(typeof factory, "function", "client bundle must register a factory");
  const exports = factory(() => {
    throw new Error("unexpected require");
  });

  const subscriptions = [];
  const ctx = {
    effect(callback) {
      const disposer = callback();
      if (typeof disposer === "function") page.disposers.push(disposer);
      return disposer;
    },
    sessions: {
      open(id) {
        if (page.state.byId === undefined || page.state.byId[id] === undefined) {
          throw new Error(`unknown session ${id}`);
        }
        page.calls.open.push(id);
        page.state.current = id;
      },
      list: {
        getSnapshot() {
          return page.state;
        },
        subscribe(callback) {
          subscriptions.push(callback);
          return () => {};
        },
      },
    },
  };
  const cleanup = () => {
    for (const dispose of page.disposers.splice(0)) dispose();
    if (previousFetch === undefined) delete globalThis.fetch;
    else globalThis.fetch = previousFetch;
    if (previousGateway === undefined) delete globalThis.__PAD_SESSION_JUMP__;
    else globalThis.__PAD_SESSION_JUMP__ = previousGateway;
    if (previousBoot === undefined) delete globalThis.__PAD_SESSION_JUMP_BOOT__;
    else globalThis.__PAD_SESSION_JUMP_BOOT__ = previousBoot;
  };
  return { exports, ctx, page, subscriptions, cleanup };
}

const tick = (ms = 25) => new Promise((resolve) => setTimeout(resolve, ms));

test("normalizeId accepts session ids, raw uuids and cache file names", async () => {
  const { exports, cleanup } = await loadClient();
  assert.equal(exports.normalizeId(TARGET), TARGET);
  assert.equal(exports.normalizeId(UUID), TARGET);
  assert.equal(exports.normalizeId(`${UUID}.json`), TARGET);
  assert.equal(exports.normalizeId("not-a-session"), undefined);
  assert.equal(exports.normalizeId(""), undefined);
  cleanup();
});

test("opens the session from ?session= once the list is ready", async () => {
  const { exports, ctx, page, cleanup } = await loadClient({
    search: `?token=t0ken&session=${TARGET}`,
    byId: { [TARGET]: { id: TARGET } },
  });
  exports.apply(ctx);
  assert.deepEqual(page.calls.open, [TARGET]);
  assert.equal(page.calls.events[0]?.kind, "opened");
  assert.ok(page.calls.replaced.every((next) => next.includes("token=t0ken")));
  cleanup();
});

test("accepts ?padSession= with a raw uuid and padSync=0", async () => {
  const { exports, ctx, page, cleanup } = await loadClient({
    search: `?padSession=${UUID}&padSync=0`,
    byId: { [TARGET]: {} },
  });
  exports.apply(ctx);
  assert.deepEqual(page.calls.open, [TARGET]);
  assert.deepEqual(page.calls.replaced, [], "padSync=0 disables address-bar sync");
  cleanup();
});

test("unknown session warns and never blocks the page", async () => {
  const { exports, ctx, page, cleanup } = await loadClient({
    search: "?session=session-00000000-0000-0000-0000-000000000000",
    byId: {},
  });
  assert.doesNotThrow(() => exports.apply(ctx));
  assert.deepEqual(page.calls.open, []);
  assert.equal(page.calls.events[0]?.kind, "unknown");
  cleanup();
});

test("waits for phase=ready before opening", async () => {
  const { exports, ctx, page, subscriptions, cleanup } = await loadClient({
    search: `?session=${TARGET}`,
    phase: "loading",
    byId: { [TARGET]: {} },
  });
  exports.apply(ctx);
  assert.deepEqual(page.calls.open, [], "must not open before the list is ready");
  page.state.phase = "ready";
  subscriptions[0]();
  assert.deepEqual(page.calls.open, [TARGET]);
  cleanup();
});

test("does not reopen when the target is already current, and syncs the URL", async () => {
  const { exports, ctx, page, cleanup } = await loadClient({
    search: `?token=t0ken&session=${TARGET}`,
    byId: { [TARGET]: {} },
    current: TARGET,
  });
  exports.apply(ctx);
  assert.deepEqual(page.calls.open, []);
  assert.equal(page.calls.events[0]?.kind, "already");
  assert.ok(page.calls.replaced.every((next) => next.includes("token=t0ken")));
  cleanup();
});

test("missing sessions service reports unsupported instead of throwing", async () => {
  const { exports, page, cleanup } = await loadClient();
  const ctx = { effect: (cb) => cb(), sessions: undefined };
  assert.doesNotThrow(() => exports.apply(ctx));
  assert.equal(page.calls.events[0]?.kind, "unsupported");
  cleanup();
});

test("readGatewayConfig validates schema and loopback", async () => {
  const { exports, cleanup } = await loadClient({ gateway: { schema: "pack-agent.pad-gateway/v1", url: GATEWAY, token: TOKEN } });
  const config = exports.readGatewayConfig();
  assert.equal(config?.origin, "http://127.0.0.1:65432");
  assert.equal(config?.token, TOKEN);
  cleanup();

  const foreign = await loadClient({ gateway: { schema: "pack-agent.pad-gateway/v1", url: "https://evil.example/api", token: TOKEN } });
  assert.equal(foreign.exports.readGatewayConfig(), undefined);
  foreign.cleanup();

  const wrongSchema = await loadClient({ gateway: { schema: "other", url: GATEWAY, token: TOKEN } });
  assert.equal(wrongSchema.exports.readGatewayConfig(), undefined);
  wrongSchema.cleanup();
});

test("gateway channel long-polls, reports the event and acks", async () => {
  const calls = [];
  let gets = 0;
  const fetchImpl = async (url, init = {}) => {
    calls.push({ url, init });
    if (url.includes("/pad/jump?since=")) {
      gets += 1;
      if (gets === 1) {
        return { status: 200, ok: true, json: async () => ({ schema: "pack-agent.pad-jump/v1", seq: 7, sessionId: TARGET, at: 1 }) };
      }
      return { status: 204, ok: true, json: async () => ({}) };
    }
    if (url.endsWith("/pad/jump/ack")) return { status: 200, ok: true, json: async () => ({ ok: true }) };
    throw new Error(`unexpected url ${url}`);
  };
  const seen = [];
  const { exports, cleanup } = await loadClient({ gateway: { schema: "pack-agent.pad-gateway/v1", url: GATEWAY, token: TOKEN }, fetchImpl });
  const channel = exports.createGatewayChannel({
    config: { origin: "http://127.0.0.1:65432", token: TOKEN },
    fetchImpl,
    sleep: async () => {},
    onEvent: (sessionId, seq) => {
      seen.push([sessionId, seq]);
      channel.stop();
    },
  });
  await tick();
  assert.deepEqual(seen, [[TARGET, 7]]);
  assert.ok(calls.some((call) => call.url.endsWith("/pad/jump/ack")), "must ack the consumed event");
  assert.equal(calls.find((call) => call.url.includes("/pad/jump?"))?.init.headers.authorization, `Bearer ${TOKEN}`);
  channel.stop();
  cleanup();
});

test("gateway event opens the session in the page", async () => {
  let gets = 0;
  const fetchImpl = async (url) => {
    if (url.includes("/pad/jump?since=")) {
      gets += 1;
      if (gets === 1) {
        return { status: 200, ok: true, json: async () => ({ schema: "pack-agent.pad-jump/v1", seq: 3, sessionId: TARGET, at: 1 }) };
      }
      return { status: 204, ok: true, json: async () => ({}) };
    }
    if (url.endsWith("/pad/jump/ack")) return { status: 200, ok: true, json: async () => ({ ok: true }) };
    throw new Error(`unexpected url ${url}`);
  };
  const loaded = await loadClient({
    gateway: { schema: "pack-agent.pad-gateway/v1", url: GATEWAY, token: TOKEN },
    fetchImpl,
    byId: { [TARGET]: { id: TARGET } },
  });
  try {
    loaded.exports.apply(loaded.ctx);
    await tick(50);
    assert.deepEqual(loaded.page.calls.open, [TARGET]);
    assert.ok(loaded.page.calls.events.some((event) => event.kind === "opened"));
  } finally {
    loaded.cleanup();
  }
});

test("falls back to uiWorkspace.openSession on 0.2.x (no sessions.open)", async () => {
  const loaded = await loadClient({ search: `?session=${TARGET}` });
  try {
    const opened = [];
    const state = { phase: "ready", byId: { [TARGET]: {} } };
    const ctx = {
      effect: (callback) => callback(),
      sessions: { list: { getSnapshot: () => state, subscribe: () => () => {} } },
      inject: (deps, callback) => {
        if (deps.includes("uiWorkspace")) callback({ uiWorkspace: { openSession: (id) => opened.push(id) } });
      },
    };
    loaded.exports.apply(ctx);
    assert.deepEqual(opened, [TARGET]);
    assert.ok(loaded.page.calls.events.some((event) => event.kind === "opened"));
  } finally {
    loaded.cleanup();
  }
});

test("uses the boot-captured URL when the app already cleaned the query", async () => {
  const loaded = await loadClient({
    search: "",
    bootSearch: `?token=t0ken&session=${TARGET}`,
    byId: { [TARGET]: {} },
  });
  try {
    loaded.exports.apply(loaded.ctx);
    assert.deepEqual(loaded.page.calls.open, [TARGET]);
    assert.ok(loaded.page.calls.events.some((event) => event.kind === "opened"));
  } finally {
    loaded.cleanup();
  }
});

test("uses the boot-captured fragment (#session survives the token redirect)", async () => {
  const loaded = await loadClient({
    search: "",
    bootHash: `#session=${TARGET}`,
    byId: { [TARGET]: {} },
  });
  try {
    loaded.exports.apply(loaded.ctx);
    assert.deepEqual(loaded.page.calls.open, [TARGET]);
    assert.ok(loaded.page.calls.events.some((event) => event.kind === "opened"));
  } finally {
    loaded.cleanup();
  }
});
