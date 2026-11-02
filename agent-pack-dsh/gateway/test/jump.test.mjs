import test from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import {
  createJumpHttpHandler,
  createJumpStore,
  isAllowedOrigin,
  isSessionId,
  JUMP_SCHEMA,
} from "../lib/jump.mjs";

const TOKEN = "t".repeat(32);
const ID = "session-02d4a5ad-9792-4569-a2de-d598e85d754a";

test("session id validation accepts canonical ids only", () => {
  assert.equal(isSessionId(ID), true);
  assert.equal(isSessionId("02d4a5ad-9792-4569-a2de-d598e85d754a"), false);
  assert.equal(isSessionId("session-"), false);
  assert.equal(isSessionId(undefined), false);
});

test("origin validation allows loopback and the desktop scheme only", () => {
  assert.equal(isAllowedOrigin("http://127.0.0.1:3080"), true);
  assert.equal(isAllowedOrigin("http://localhost:43129"), true);
  assert.equal(isAllowedOrigin("dsh-app://app"), true);
  assert.equal(isAllowedOrigin("https://evil.example"), false);
  assert.equal(isAllowedOrigin(undefined), false);
});

test("store publishes, wakes one waiter, and keeps seq monotonic", async () => {
  const store = createJumpStore({ timeoutMs: 200 });
  const pending = store.wait(0);
  const event = store.publish(ID);
  assert.equal(event.seq, 1);
  assert.equal(event.schema, JUMP_SCHEMA);
  const waited = await pending;
  assert.equal(waited?.seq, 1);
  assert.equal(waited?.sessionId, ID);
  assert.equal(store.publish(ID).seq, 2);
  assert.equal((await store.wait(1))?.seq, 2, "a late waiter gets the backlog");
  assert.equal(await store.wait(2), null, "an up-to-date waiter times out");
  store.dispose();
});

test("store rejects invalid ids and prunes expired events", () => {
  let clock = 1000;
  const store = createJumpStore({ ttlMs: 50, timeoutMs: 10, now: () => clock });
  assert.throws(() => store.publish("not-a-session"), /invalid session id/);
  store.publish(ID);
  assert.equal(store.size, 1);
  clock += 100;
  assert.equal(store.wait(0) instanceof Promise, true);
  return store.wait(0).then((event) => {
    assert.equal(event, null, "expired events must not be replayed");
    store.dispose();
  });
});

async function withServer(store, fn) {
  const handler = createJumpHttpHandler({ store, token: TOKEN });
  const server = createServer((req, res) => {
    handler(req, res).then((handled) => {
      if (!handled) {
        res.writeHead(404).end();
      }
    });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address();
  const base = `http://127.0.0.1:${port}`;
  try {
    return await fn(base);
  } finally {
    await new Promise((resolve) => server.close(resolve));
    store.dispose();
  }
}

test("http: POST publishes, GET long-polls, ack succeeds", async () => {
  const store = createJumpStore({ timeoutMs: 500 });
  await withServer(store, async (base) => {
    const denied = await fetch(`${base}/pad/jump`, { method: "POST", body: "{}" });
    assert.equal(denied.status, 401);

    const bad = await fetch(`${base}/pad/jump`, {
      method: "POST",
      headers: { authorization: `Bearer ${TOKEN}`, "content-type": "application/json" },
      body: JSON.stringify({ sessionId: "nope" }),
    });
    assert.equal(bad.status, 400);

    const post = await fetch(`${base}/pad/jump`, {
      method: "POST",
      headers: { authorization: `Bearer ${TOKEN}`, "content-type": "application/json" },
      body: JSON.stringify({ sessionId: ID }),
    });
    assert.equal(post.status, 200);
    const posted = await post.json();
    assert.equal(posted.ok, true);

    const got = await fetch(`${base}/pad/jump?since=0`, {
      headers: { authorization: `Bearer ${TOKEN}` },
    });
    assert.equal(got.status, 200);
    const event = await got.json();
    assert.equal(event.sessionId, ID);
    assert.equal(event.seq, posted.seq);

    const ack = await fetch(`${base}/pad/jump/ack`, {
      method: "POST",
      headers: { authorization: `Bearer ${TOKEN}`, "content-type": "application/json" },
      body: JSON.stringify({ seq: event.seq, ok: true }),
    });
    assert.equal(ack.status, 200);
  });
});

test("http: CORS preflight and origin gate", async () => {
  const store = createJumpStore({ timeoutMs: 100 });
  await withServer(store, async (base) => {
    const preflight = await fetch(`${base}/pad/jump`, {
      method: "OPTIONS",
      headers: {
        origin: "http://127.0.0.1:3080",
        "access-control-request-method": "GET",
        "access-control-request-headers": "authorization",
      },
    });
    assert.equal(preflight.status, 204);
    assert.equal(preflight.headers.get("access-control-allow-origin"), "http://127.0.0.1:3080");
    assert.match(preflight.headers.get("access-control-allow-headers") ?? "", /authorization/);

    const desktop = await fetch(`${base}/pad/jump?since=0`, {
      headers: { authorization: `Bearer ${TOKEN}`, origin: "dsh-app://app" },
    });
    assert.equal(desktop.status, 204);
    assert.equal(desktop.headers.get("access-control-allow-origin"), "dsh-app://app");

    const evil = await fetch(`${base}/pad/jump`, {
      method: "POST",
      headers: { authorization: `Bearer ${TOKEN}`, origin: "https://evil.example", "content-type": "application/json" },
      body: JSON.stringify({ sessionId: ID }),
    });
    assert.equal(evil.status, 403);
  });
});

test("http: long-poll timeout returns 204", async () => {
  const store = createJumpStore({ timeoutMs: 50 });
  await withServer(store, async (base) => {
    const res = await fetch(`${base}/pad/jump?since=99`, {
      headers: { authorization: `Bearer ${TOKEN}` },
    });
    assert.equal(res.status, 204);
  });
});
