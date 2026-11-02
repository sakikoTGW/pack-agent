import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  AD_FILE,
  AD_SCHEMA,
  GLOBAL_NAME,
  apply,
  injectGatewayAd,
  readGatewayAd,
} from "../lib/index.mjs";

const TOKEN = "t".repeat(32);

function withHome(ad, fn) {
  const dir = mkdtempSync(join(tmpdir(), "pad-jump-home-"));
  if (ad !== undefined) writeFileSync(join(dir, AD_FILE), JSON.stringify(ad), "utf8");
  const previous = process.env.DSH_HOME;
  process.env.DSH_HOME = dir;
  try {
    return fn(dir);
  } finally {
    if (previous === undefined) delete process.env.DSH_HOME;
    else process.env.DSH_HOME = previous;
    rmSync(dir, { recursive: true, force: true });
  }
}

const AD = {
  schema: AD_SCHEMA,
  url: "http://127.0.0.1:65432/api",
  token: TOKEN,
  pid: 1234,
};

test("reads and validates the gateway ad from DSH_HOME", () => {
  withHome(AD, (dir) => {
    const ad = readGatewayAd();
    assert.equal(ad?.url, AD.url);
    assert.equal(ad?.token, TOKEN);
    assert.equal(ad?.home, dir);
  });
});

test("rejects a missing or foreign-schema ad file", () => {
  withHome(undefined, () => {
    assert.equal(readGatewayAd(), undefined);
  });
  withHome({ ...AD, schema: "something/else" }, () => {
    assert.equal(readGatewayAd(), undefined);
  });
  withHome({ ...AD, token: "short" }, () => {
    assert.equal(readGatewayAd(), undefined);
  });
});

test("pushes a global index-injection row with the ad", () => {
  withHome(AD, () => {
    const table = [];
    assert.equal(injectGatewayAd(table), true);
    assert.equal(table.length, 2, "gateway ad row + boot-capture script row");
    assert.equal(table[0].kind, "global");
    assert.equal(table[0].name, GLOBAL_NAME);
    assert.equal(table[0].value.url, AD.url);
    assert.equal(table[0].value.token, TOKEN);
    assert.equal(table[1].kind, "script");
    assert.equal(table[1].placement, "head");
    assert.match(table[1].text, /__PAD_SESSION_JUMP_BOOT__/);
  });
});

test("no row when the gateway ad is absent", () => {
  withHome(undefined, () => {
    const table = [];
    assert.equal(injectGatewayAd(table), false);
    assert.deepEqual(table, []);
  });
});

test("apply subscribes to webserver/index-inject", () => {
  const listeners = new Map();
  const ctx = {
    on(event, handler) {
      listeners.set(event, handler);
      return () => {};
    },
  };
  withHome(AD, () => {
    assert.equal(apply(ctx), true);
    assert.equal(typeof listeners.get("webserver/index-inject"), "function");
    const table = [];
    listeners.get("webserver/index-inject")(table);
    assert.equal(table[0]?.name, GLOBAL_NAME);
  });
});

test("apply is a no-op on a context without events", () => {
  assert.equal(apply({}), false);
  assert.equal(apply(undefined), false);
});
