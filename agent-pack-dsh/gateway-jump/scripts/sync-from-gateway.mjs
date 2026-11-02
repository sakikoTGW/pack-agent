/**
 * Keep gateway-jump in sync with the shared jump channel implementation.
 * Run: node scripts/sync-from-gateway.mjs
 */
import { copyFileSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const src = resolve(here, "..", "..", "gateway");
const dst = resolve(here, "..");

function copyIfChanged(from, to) {
  const before = (() => {
    try {
      return readFileSync(to, "utf8");
    } catch {
      return undefined;
    }
  })();
  const next = readFileSync(from, "utf8");
  if (before === next) {
    console.log(`unchanged: ${to}`);
    return;
  }
  writeFileSync(to, next, "utf8");
  console.log(`updated:   ${to}`);
}

copyIfChanged(join(src, "lib", "jump.mjs"), join(dst, "lib", "jump.mjs"));
copyIfChanged(join(src, "test", "jump.test.mjs"), join(dst, "test", "jump.test.mjs"));
