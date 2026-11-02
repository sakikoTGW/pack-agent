# D100-E2E · 会话跳转真机验证（0.2.1-rc.2 / web）

日期：2026-11-02｜对象：`ev-project` 实例（home 在 `.pack-launcher/instances/ev-project/home`，DSH 0.2.1-rc.2）
目的：验证 `pad-session-jump` + `pad-gateway` 的跳转链路（URL 通道 / gateway 通道）

## 结论

| 项 | 结果 | 证据 |
|---|---|---|
| 插件能在 0.2.1 web 端加载 | ✅ | 页面无 "Failed to load plugins"；`window.__PAD_SESSION_JUMP_CTX__` 出现 |
| host 半注入 gateway 广告 | ✅ | 页面 `window.__PAD_SESSION_JUMP__ = { url: http://127.0.0.1:<port>/api, token: <64 hex> }` |
| gateway 跳转 carrier 在 0.2.1 启动 | ✅（jump-only） | `pad-gateway.log`：`listening on http://127.0.0.1:…/api (jump channel ready)`；`apiproxy unavailable; jump-only mode: Cannot find package '@deepseek-ai/dsh-host-apiproxy'` |
| `POST /pad/jump` | ✅ | `{"ok":true,"seq":1}` |
| 页面收到事件并执行打开 | ✅ | `__PAD_SESSION_JUMP_EVENTS__ = [{kind:"queued"},{kind:"opened"}]`，无异常 |
| URL 通道 `?session=` | ⚠️→✅（2026-11-02 复测） | query 会被 token redirect 丢掉；改用 **fragment** `#session=<id>`：`__PAD_SESSION_JUMP_BOOT__={search:"",hash:"#session=…"}` → `__PAD_SESSION_JUMP_EVENTS__=[{kind:"opened"}]`（详见 `EVIDENCE/D101-jump-m1/` §2.1） |
| 视觉上会话切换 | ⚠️ 未能确认 | opener 调用成功（无抛错），但该实例停在 onboarding（"请选择模型"），页面 body 未变化；不是跳转代码报错 |

## 关键 API 发现（0.2.x）

- `ctx.sessions` 服务存在，但 **0.1 的 `sessions.open()` 在 0.2.1 上不存在**。其 key：`list / manager / scopes / rootCtx / retainObservers / scopeDrops / closed`。
- `ctx.sessions.list.getSnapshot()` → `{ ids, byId, phase, projectionsBySession }`，`phase` 会先 `pending` 后 `ready`。
- 打开会话的实际入口是 **`uiWorkspace.openSession(id)`**（`dsh-client-ui-workspace`；源码注释："Select a Session and show its Conversation as one UI navigation action"）。插件通过 `ctx.inject(["uiWorkspace"], …)` 拿到该服务并调用，验证成功。
- `dsh.client` 需要 **`manifestVersion: 1`** 并声明 `inject` 客户端包列表（本次用 `@deepseek-ai/dsh-client-modules` + `@deepseek-ai/dsh-client-ui-conversation`）；client bundle 的 `__ModuleLoader__.load({ id })` **必须等于包名** `@sakikotgw/pad-session-jump`，否则报 "could not load / 1 entry did not activate"。
- DSH web 启动时会清理 URL query（token 落地为 cookie 后 `history.replaceState('/')`），且 client 模块在 app boot 之后才执行 → **纯 URL 通道在 0.2.x 不可靠**；需要早期注入（script row 捕获 location）或改用 gateway 事件。

## gateway 在 0.2.x 的安装问题（重要）

- 仓库版 `@sakikotgw/pad-gateway@0.1.0` 安装到 0.2.1 被拒：`engines.dsh >=0.1.0-rc.7 <0.2.0`；授权豁免后其依赖 `@deepseek-ai/dsh-host-apiproxy@0.1.0-rc.8` 又被拒，且 bundle patch 会插入 `host-apiproxy` / `host-directory-picker` 两行——0.2.x 没有对应 npm 包。
- 本次 E2E 用 **临时变体** `@sakikotgw/pad-gateway-jump@0.0.0-e2e`（同 `lib/index.js` + `lib/jump.mjs`，`inject=[]`，patch 只插自己）验证跳转 carrier 本身可跑。
- 结论：正式 gateway 需要做 0.2.x 版本分支（jump-only carrier 与 apiproxy 代理包分离），否则 0.2.x 上只有跳转、没有 live session/代理。

## 恢复情况

- 测试实例 profile 已移除两个测试插件，`dsh.profile.bundles` 恢复为 `@deepseek-ai/dsh-base` + `@deepseek-ai/dsh-web-app`。
- 实例已按原方式重启（`node bin.js --profile web`，监听 3080），`pad-gateway.json` / `pad-gateway.log` 已删并从证据中脱敏保存。
- 未对官方桌面端做 E2E（重启会中断当前会话 GUI）；其注入机制已从 asar 源码确认。

## 证据文件

- `dsh-web.log`：启动行（token 已脱敏）
- `pad-gateway.log`：jump-only 模式启动日志
- `pad-gateway.redacted.json`：广告文件结构（token 脱敏）
- 页面实测数据（会话上下文）：`__PAD_SESSION_JUMP_EVENTS__ = queued → opened`、`POST /pad/jump → {"ok":true,"seq":1}`
