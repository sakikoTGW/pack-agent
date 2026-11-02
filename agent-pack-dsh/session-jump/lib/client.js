window.__ModuleLoader__.load({ id: "@sakikotgw/pad-session-jump", factory: (require) => { var module = { exports: {} }; var exports = module.exports;
Object.defineProperty(exports, Symbol.toStringTag, { value: "Module" });
//#region src/client/index.ts
/**
 * PAD session jump — browser half.
 *
 * Two delivery channels, same client-side jump:
 * 1. URL: `?session=<id>` / `?padSession=<id>` (browser / Edge app window).
 * 2. Gateway: long-poll the instance's pad-gateway (`/pad/jump`) when the host
 *    half injected `globalThis.__PAD_SESSION_JUMP__` with `{url, token}`.
 *    This is what makes the jump work inside the official desktop renderer,
 *    where there is no external URL entry.
 *
 * Design rules (see agent-pack-dsh/docs/session-jump.md):
 * - feature detection only: a missing/renamed client service must never throw;
 * - no protocol registration, no host service, no third-party dependency;
 * - unknown/late sessions degrade to a warning, never block the page;
 * - address-bar sync can be disabled with `?padSync=0`.
 */

/** Services required before activation (provided by the client runtime). */
const inject = ["sessions"];

/** Query parameters carrying the target session id (PAD's one wins). */
const PAD_QUERY_KEY = "padSession";
const QUERY_KEY = "session";
/** Query parameter disabling address-bar sync (`?padSync=0`). */
const SYNC_QUERY_KEY = "padSync";

/** Event name PAD/the page can listen to for jump results. */
const RESULT_EVENT = "pad-session-jump:result";

/** Global injected by this plugin's host half (pad-gateway ad). */
const GATEWAY_GLOBAL = "__PAD_SESSION_JUMP__";
const GATEWAY_SCHEMA = "pack-agent.pad-gateway/v1";
const LOOPBACK_ORIGIN = /^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/;

/** `session-<uuid>` as used by the DSH web client and plugin ecosystem. */
const SESSION_ID = /^session-[A-Za-z0-9][A-Za-z0-9-]{7,63}$/;
/** Raw uuid as used by session cache file names (`<uuid>.json`). */
const RAW_UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/**
 * Normalize a session id: accepts `session-<uuid>`, a raw uuid, or a cache
 * file name (`<uuid>.json`). Returns undefined when it is not a session id.
 * @param value - raw id from the URL/event.
 * @returns canonical `session-<uuid>` or undefined.
 */
function normalizeId(value) {
	const raw = typeof value === "string" ? value.trim() : "";
	if (raw === "") return undefined;
	if (SESSION_ID.test(raw)) return raw;
	const uuid = raw.endsWith(".json") ? raw.slice(0, -5) : raw;
	if (RAW_UUID.test(uuid)) return `session-${uuid}`;
	return undefined;
}

/**
 * @param {string} search URL search string (with or without leading `?`)
 * @returns the target session id, if any
 */
function targetFromSearch(search) {
	try {
		const params = new URLSearchParams(typeof search === "string" ? search : "");
		return normalizeId(params.get(PAD_QUERY_KEY)) ?? normalizeId(params.get(QUERY_KEY));
	} catch {
		return undefined;
	}
}

/**
 * The 0.2.x web host consumes the token URL with a redirect, which drops the
 * query but keeps the fragment — so `#session=<id>` survives where `?session=`
 * does not. Accept both fragment forms: `#session=<id>` and `#?session=<id>`.
 * @param {string} hash URL hash (with or without leading `#`)
 * @returns the target session id, if any
 */
function targetFromHash(hash) {
	try {
		const raw = typeof hash === "string" ? hash.replace(/^#/, "") : "";
		const params = new URLSearchParams(raw.startsWith("?") ? raw.slice(1) : raw);
		return normalizeId(params.get(PAD_QUERY_KEY)) ?? normalizeId(params.get(QUERY_KEY));
	} catch {
		return undefined;
	}
}

/** @returns the target session id from the URL (query or fragment), if any. */
function readTarget() {
	try {
		return targetFromSearch(window.location.search) ?? targetFromHash(window.location.hash);
	} catch {
		return undefined;
	}
}

/** @returns true when address-bar sync is enabled for this page. */
function syncEnabled() {
	try {
		const value = new URLSearchParams(window.location.search).get(SYNC_QUERY_KEY);
		return value !== "0" && value !== "false";
	} catch {
		return false;
	}
}

/**
 * Report a jump result: a DOM event for the page/PAD bridge plus a console
 * line for diagnostics. Never throws.
 * @param kind - result kind (`opened` | `already` | `unknown` | `failed` | `unsupported` | `queued`).
 * @param detail - extra JSON-safe details.
 */
function report(kind, detail) {
	const payload = { kind, at: Date.now(), ...(detail ?? {}) };
	try {
		const events = (globalThis.__PAD_SESSION_JUMP_EVENTS__ ??= []);
		events.push(payload);
		if (events.length > 50) events.shift();
	} catch {}
	try {
		window.dispatchEvent(new CustomEvent(RESULT_EVENT, { detail: payload }));
	} catch {}
	try {
		console.info(`[pad-session-jump] ${kind}`, payload);
	} catch {}
}

/**
 * Mirror the current session into the URL so the page is copyable/shareable.
 * Preserves every other query parameter (notably the DSH access token).
 * @param sessionId - current session id, or undefined.
 */
function syncQuery(sessionId) {
	if (!syncEnabled()) return;
	try {
		const url = new URL(window.location.href);
		if (sessionId === undefined) url.searchParams.delete(QUERY_KEY);
		else if (url.searchParams.get(QUERY_KEY) !== sessionId) url.searchParams.set(QUERY_KEY, sessionId);
		const next = `${url.pathname}${url.search}${url.hash}`;
		const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
		if (next !== current) window.history.replaceState(window.history.state, "", next);
	} catch {}
}

/**
 * Pick the session key the runtime actually knows: the canonical id, or a
 * bare uuid if the runtime keys sessions that way.
 * @param byId - session map from `sessions.list.getSnapshot()`.
 * @param target - canonical session id.
 * @returns the key to pass to `sessions.open`, or undefined.
 */
function resolveKey(byId, target) {
	if (byId === undefined || byId === null) return undefined;
	if (Object.prototype.hasOwnProperty.call(byId, target)) return target;
	const bare = target.startsWith("session-") ? target.slice("session-".length) : target;
	if (Object.prototype.hasOwnProperty.call(byId, bare)) return bare;
	return undefined;
}

/**
 * Read the gateway ad injected by the host half.
 * @returns {{origin: string, token: string} | undefined}
 */
function readGatewayConfig() {
	try {
		const ad = globalThis[GATEWAY_GLOBAL];
		if (!ad || ad.schema !== GATEWAY_SCHEMA) return undefined;
		if (typeof ad.url !== "string" || typeof ad.token !== "string" || ad.token.length < 16) return undefined;
		const origin = new URL(ad.url).origin;
		if (!LOOPBACK_ORIGIN.test(origin)) return undefined;
		return { origin, token: ad.token };
	} catch {
		return undefined;
	}
}

/**
 * Long-poll the instance gateway for jump events. Delivery channel only; the
 * actual switch is `sessions.open()` in this page.
 * @param {{
 *   config: {origin: string, token: string},
 *   onEvent: (sessionId: string, seq: number) => void,
 *   report?: (kind: string, detail?: object) => void,
 *   fetchImpl?: typeof fetch,
 *   sleep?: (ms: number) => Promise<void>,
 * }} options
 * @returns {{stop: () => void, readonly seq: number}}
 */
function createGatewayChannel(options) {
	const { config, onEvent, report: note } = options;
	const fetchImpl = options.fetchImpl ?? globalThis.fetch;
	const sleep = options.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
	const auth = `Bearer ${config.token}`;
	let stopped = false;
	let seq = 0;
	let backoff = 1000;

	const loop = async () => {
		while (!stopped) {
			try {
				const res = await fetchImpl(`${config.origin}/pad/jump?since=${seq}`, {
					headers: { authorization: auth },
					cache: "no-store",
				});
				if (stopped) return;
				if (res.status === 204) {
					backoff = 1000;
					// The gateway answered the long-poll with a timeout. Yield a
					// macrotask before re-polling so a fast 204 loop can never
					// starve timers/rendering (and tests can interleave).
					await sleep(0);
					continue;
				}
				if (!res.ok) {
					await sleep(backoff);
					backoff = Math.min(backoff * 2, 10000);
					continue;
				}
				const event = await res.json();
				if (typeof event?.seq === "number" && event.seq > seq) seq = event.seq;
				if (typeof event?.sessionId === "string") {
					try {
						onEvent(event.sessionId, event.seq);
					} catch (error) {
						note?.("failed", { error: String(error) });
					}
					try {
						await fetchImpl(`${config.origin}/pad/jump/ack`, {
							method: "POST",
							headers: { authorization: auth, "content-type": "application/json" },
							body: JSON.stringify({ seq: event.seq, ok: true }),
							cache: "no-store",
						});
					} catch {}
				}
				backoff = 1000;
			} catch {
				if (stopped) return;
				await sleep(backoff);
				backoff = Math.min(backoff * 2, 10000);
			}
		}
	};
	void loop();
	return {
		stop() {
			stopped = true;
		},
		get seq() {
			return seq;
		},
	};
}

/**
 * Target captured before the app cleans the URL query. The host half injects a
 * classic `script` row that stores `location.search` into
 * `globalThis.__PAD_SESSION_JUMP_BOOT__` before the deferred app module runs;
 * module-level `readTarget()` alone is too late on 0.2.x.
 */
const bootCapture = globalThis.__PAD_SESSION_JUMP_BOOT__;
const bootTarget =
	targetFromHash(bootCapture?.hash) ??
	targetFromSearch(bootCapture?.search) ??
	readTarget();

/**
 * Client plugin body: opens URL/gateway targets, then keeps the address bar
 * synchronized with the current session selection.
 * @param ctx - the client cordis context (`sessions` service).
 */
function apply(ctx) {
	const sessions = ctx?.sessions;
	const list = sessions?.list;
	const getSnapshot = () => {
		try {
			return list?.getSnapshot?.();
		} catch {
			return undefined;
		}
	};
	let pending = bootTarget ?? readTarget();
	/** Set by the sessions effect; opener resolution and gateway events reuse it. */
	let reconcile = () => {};
	// 0.1.x exposes sessions.open(id); 0.2.x navigation lives on workspace
	// services (uiWorkspace.openSession / workspaceNavigation.openSession).
	let opener =
		typeof sessions?.open === "function"
			? (id) => sessions.open(id)
			: typeof sessions?.manager?.openSession === "function"
				? (id) => sessions.manager.openSession(id)
				: undefined;
	const setOpener = (fn) => {
		if (opener !== undefined) return;
		opener = fn;
		reconcile();
	};
	try {
		globalThis.__PAD_SESSION_JUMP_CTX__ = {
			hasSessions: Boolean(sessions),
			hasOpener: opener !== undefined,
			injectType: typeof ctx?.inject,
			managerKeys: sessions?.manager ? Object.keys(sessions.manager).slice(0, 20) : [],
			snapshotKeys: Object.keys(getSnapshot() ?? {}).slice(0, 20),
		};
	} catch {}
	if (opener === undefined) {
		if (typeof ctx?.inject !== "function") {
			report("unsupported", { reason: "no session opener and no inject on the client ctx" });
			return;
		}
		for (const serviceName of ["uiWorkspace", "workspaceNavigation"]) {
			try {
				ctx.inject([serviceName], (c) => {
					const service = c?.[serviceName];
					if (typeof service?.openSession === "function") setOpener((id) => service.openSession(id));
				});
			} catch {}
		}
	}
	ctx.effect(() => {
		let unsubscribe;
		reconcile = () => {
			const state = getSnapshot();
			const ready = state === undefined || state?.phase === undefined || state.phase === "ready";
			try {
				globalThis.__PAD_SESSION_JUMP_DEBUG__ = {
					phase: state?.phase,
					ready,
					hasOpener: opener !== undefined,
				};
			} catch {}
			if (!ready) return;
			if (pending !== undefined) {
				if (opener === undefined) return;
				const target = pending;
				pending = undefined;
				const key = resolveKey(state?.byId, target) ?? target;
				if (state?.current === key) {
					report("already", { target: key });
					return;
				}
				try {
					opener(key);
					report("opened", { target: key });
				} catch (error) {
					try {
						const bare = key.startsWith("session-") ? key.slice("session-".length) : `session-${key}`;
						opener(bare);
						report("opened", { target: bare });
					} catch (error2) {
						report("unknown", { target: key, error: String(error2) });
						return;
					}
				}
			}
			if (state?.current !== undefined) syncQuery(state.current);
		};
		reconcile();
		try {
			unsubscribe = list?.subscribe?.(reconcile);
		} catch {
			unsubscribe = undefined;
		}
		return () => {
			unsubscribe?.();
			unsubscribe = undefined;
		};
	}, "pad-session-jump: open target session from URL");
	const gateway = readGatewayConfig();
	if (gateway !== undefined) {
		ctx.effect(() => {
			const channel = createGatewayChannel({
				config: gateway,
				report,
				onEvent: (sessionId) => {
					const target = normalizeId(sessionId);
					if (target === undefined) {
						report("unknown", { target: sessionId });
						return;
					}
					pending = target;
					report("queued", { target });
					reconcile();
				},
			});
			return () => channel.stop();
		}, "pad-session-jump: gateway channel");
	}
}
//#endregion
exports.apply = apply;
exports.inject = inject;
exports.name = "pad-session-jump";
exports.normalizeId = normalizeId;
exports.readGatewayConfig = readGatewayConfig;
exports.createGatewayChannel = createGatewayChannel;
exports.QUERY_KEY = QUERY_KEY;
exports.RESULT_EVENT = RESULT_EVENT;

return module.exports; } });
