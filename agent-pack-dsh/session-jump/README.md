# @sakikotgw/pad-session-jump

PAD 的会话跳转消费端（v0.2，URL + gateway 双通道）：让 DSH Web 界面可以打开指定会话。两条通道都是**页面内切会话**（客户端状态），不需要服务端“打开会话”接口。

配套设计：[`../docs/session-jump.md`](../docs/session-jump.md)；真机证据：[`../../EVIDENCE/D100-session-jump-e2e/`](../../EVIDENCE/D100-session-jump-e2e/README.md)。

## 两个通道

| 通道 | 谁用 | 机制 |
|---|---|---|
| **URL** | 浏览器 / Edge app 窗 | 打开 `?session=session-<uuid>`，插件解析后打开会话（**0.2.x 实测不可用**，见下） |
| **gateway** | 官方桌面端 / 已打开的浏览器页 | host 半把 gateway 广告注入为全局 `__PAD_SESSION_JUMP__`，client 半长轮询 `pad-gateway` 的 `/pad/jump`，收到事件后打开会话并 `ack` |

打开动作是自适应的：0.1.x 用 `sessions.open(id)`；**0.2.x 用 `uiWorkspace.openSession(id)`**（通过 `ctx.inject(["uiWorkspace"])` 获取）。

## 能力

- 兼容三种 id：`session-<uuid>`、裸 uuid、缓存文件名 `<uuid>.json`。
- 会话列表未就绪（`phase !== "ready"`）时等待，不抢跑；未知会话只 warn。
- gateway 通道：Bearer 鉴权、25s 长轮询、每条事件消费后 `ack`；204 超时后让出宏任务再轮询，不空转。
- 未知会话 / 服务缺失 / API 异常：只 `console.info` + 发 DOM 事件，不抛错、不阻塞页面。
- 不注册协议、不起服务、不联网（只连实例自己的 loopback gateway）。

## 安装

```powershell
# 从 npm（PAD 建 web profile 时也会自动装：Cli.Recipe("web"))
dsh plugin --profile web add @sakikotgw/pad-session-jump
# 或本地开发
dsh plugin --profile web add file:E:\pack-agent\agent-pack-dsh\session-jump
```

装完重启 `dsh web`。gateway 通道还需要同 home 里有一个在跑的 jump carrier（0.2.x 用 `@sakikotgw/pad-gateway-jump@0.1.1`，0.1.x 用 `@sakikotgw/pad-gateway`）；插件 host 半会读 `<DSH_HOME>/pad-gateway.json` 并注入页面。

## 验证

```powershell
node --test test/client.test.mjs test/host.test.mjs   # 19 pass
cd ../gateway && node --test test/jump.test.mjs       # 7 pass
```

页面控制台可见 `[pad-session-jump] opened/already/unknown/unsupported/queued`；页面可监听：

```js
window.addEventListener("pad-session-jump:result", (e) => console.log(e.detail));
```

调试全局（有界，便于真机排查）：`__PAD_SESSION_JUMP_EVENTS__`、`__PAD_SESSION_JUMP_CTX__`、`__PAD_SESSION_JUMP_DEBUG__`。

## 0.2.x 实测结论（2026-11-02，DSH 0.2.1-rc.2）

- **必须** `dsh.manifestVersion: 1`；client bundle 的 `__ModuleLoader__.load({ id })` 必须等于包名 `@sakikotgw/pad-session-jump`。
- `dsh.client.inject` 需要客户端包列表（本包用 `@deepseek-ai/dsh-client-modules` + `@deepseek-ai/dsh-client-ui-conversation`）。
- 0.2.1 **没有 `sessions.open()`**：打开会话要 `ctx.inject(["uiWorkspace"], …)` 后调 `uiWorkspace.openSession(id)`；`sessions.list.getSnapshot()` 是 `{ids, byId, phase, projectionsBySession}`（先 `pending` 后 `ready`，无 `current`）。
- **URL 通道在 0.2.x 用 fragment**：token URL 会被 host redirect 消费，**query 丢失、fragment 保留**。host 半注入 `script` 行在 app 模块执行前把 `{search,hash}` 存进 `globalThis.__PAD_SESSION_JUMP_BOOT__`；client 半优先读 `#session=<id>`（也接受 `?session=`），实测 `#session=…` 可打开；运行中实例仍优先走 gateway 事件。
- 真机证据：[`../EVIDENCE/D101-jump-m1/`](../EVIDENCE/D101-jump-m1/README.md)（URL fragment + gateway 双通道复测）、[`../EVIDENCE/D100-session-jump-e2e/`](../EVIDENCE/D100-session-jump-e2e/README.md)。
- **0.1.0-rc.7 矩阵**（2026-11-02）：`?session=<uuid>` → `opened`/`already`；未知 id → `unknown` 不崩。注意 0.1 的合成 profile 可能停在 “Loading plugins…”（缺 `dsh-client-ui-primitives` client entry，与本插件无关）。
- **版本矩阵**：0.2.1（fragment）+ 0.1.0（query）均验证到 opener 语义；npm 发布版本 0.2.1。

## 契约

| 项 | 约束 |
|---|---|
| 客户端服务 | `inject = ["sessions"]`；打开动作自适应 `sessions.open` / `uiWorkspace.openSession`，全部特性探测 |
| 版本 | DSH `0.1.x`（`sessions.open`）/ `0.2.x`（`uiWorkspace.openSession`，已真机验证 gateway 通道） |
| 注入 | host 半订阅 `webserver/index-inject`，push `{kind:"global", name:"__PAD_SESSION_JUMP__", value: ad}` |
| 协议 | **不注册** `dsh://`（官方桌面端已占用） |
| gateway | loopback HTTP + Bearer；`/pad/jump`（POST/GET long-poll）、`/pad/jump/ack`；CORS 只放行 loopback origin 与 `dsh-app://app` |

## 已知限制

- 0.2.1 的会话列表快照没有 `current` 字段，地址栏同步只在 0.1.x 完整生效。
- 列表外（不同 workspace）的会话能否被 `uiWorkspace.openSession` 打开未实测；失败会被捕获并只报 `unknown`。
- gateway 通道要求同 home 有 jump carrier 在跑；仓库版 `pad-gateway` 在 0.2.x 上需要 jump-only 分支（见设计稿 §15.4）。
- 官方桌面端 E2E 未做（重启会中断当前 GUI）；其注入机制已从 asar 源码确认。

## 文件

```
package.json          # dsh.bundle.patch + dsh.client（manifestVersion 1, platform=web, inject 列表）
cordis.patch.yml      # 把插件行插进 profile 层
lib/index.mjs         # host 半：webserver/index-inject → 注入 gateway 广告
lib/client.js         # 浏览器半：URL / gateway → 自适应 opener
test/client.test.mjs  # 13 个用例
test/host.test.mjs    # 6 个用例
```

## License

MIT
