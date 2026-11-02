/**
 * Host half of @sakikotgw/pad-session-jump.
 *
 * Injects the instance's pad-gateway ad (`<DSH_HOME>/pad-gateway.json`) into the
 * web page as a `global` index-injection row, so the browser half can long-poll
 * the gateway for jump events. Both delivery paths use the same row table:
 * - served index.html (browser) -> rendered into head/body by the host webserver;
 * - official desktop -> `ready.injections` -> `dsh-desktop:boot` -> renderer `PZ()`.
 *
 * No services are provided; no routes are registered; nothing runs per request.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";

/** Plugin display name (Loader diagnostics). */
const name = "pad-session-jump";
/** No host services are injected or provided. */
const inject = [];

/** Global name read by the browser half (`readGatewayConfig`). */
const GLOBAL_NAME = "__PAD_SESSION_JUMP__";
/** pad-gateway ad file written by the gateway plugin in the same DSH home. */
const AD_FILE = "pad-gateway.json";
/** Ad schema shared with agent-pack-dsh/gateway. */
const AD_SCHEMA = "pack-agent.pad-gateway/v1";

/**
 * Read and validate the gateway ad for this DSH home.
 * @param {string} [home] defaults to `process.env.DSH_HOME`.
 * @returns {{schema: string, url: string, token: string, home: string} | undefined}
 */
function readGatewayAd(home = process.env.DSH_HOME) {
	const dir = typeof home === "string" ? home.trim() : "";
	if (dir === "") return undefined;
	try {
		const ad = JSON.parse(readFileSync(join(dir, AD_FILE), "utf8"));
		if (ad?.schema !== AD_SCHEMA) return undefined;
		if (typeof ad.url !== "string" || typeof ad.token !== "string" || ad.token.length < 16) return undefined;
		return { schema: ad.schema, url: ad.url, token: ad.token, home: dir };
	} catch {
		return undefined;
	}
}

/**
 * Push the index-injection rows when the webserver collects them:
 * 1. `global` — the pad-gateway ad, so the browser half can long-poll events.
 * 2. `script` — capture `location.search` before the app boots (the app cleans
 *    the query, and client modules load after boot; a classic inline script
 *    runs before the deferred app module).
 * @param {unknown[]} table injection table (mutated in place)
 * @returns {boolean} whether rows were pushed
 */
function injectGatewayAd(table) {
	if (!Array.isArray(table)) return false;
	const ad = readGatewayAd();
	if (ad === undefined) return false;
	table.push({ kind: "global", name: GLOBAL_NAME, value: ad });
	table.push({
		kind: "script",
		placement: "head",
		text: "try{globalThis.__PAD_SESSION_JUMP_BOOT__={search:location.search,hash:location.hash}}catch(e){}",
	});
	return true;
}

/**
 * Plugin body: subscribe to the webserver injection event. The gateway may
 * start after this plugin, so the ad is read lazily at injection time.
 * @param {{on?: (event: string, handler: (table: unknown[]) => void) => unknown}} ctx
 * @returns {boolean} whether the subscription was installed
 */
function apply(ctx) {
	if (typeof ctx?.on !== "function") return false;
	ctx.on("webserver/index-inject", injectGatewayAd);
	return true;
}

export { apply, inject, name, readGatewayAd, injectGatewayAd, GLOBAL_NAME, AD_FILE, AD_SCHEMA };
