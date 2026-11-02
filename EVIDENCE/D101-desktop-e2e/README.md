# 官方桌面端 E2E

## 状态：✅ 已通过（复制官方 shell 实例法，2026-11-02）

当前对话就跑在官方桌面端 host 里（`dsh-desktop-host`），重启真壳会杀掉本会话；
而官方壳严格单实例（原生 exe 带 `--user-data-dir` 启动 → exit code 9，参数被忽略）。
因此本轮用**复制官方 app + 改 asar 内 app.name** 得到一个独立 userData 的第二实例，在它里面完成了真实 E2E：

- 副本：`E:\pack-agent\.tmp\DSH-App-padtest`（官方 0.2.1-rc.2 shell 复制）
- `package.json#name` 改为 `@deepseek-ai/dsh-desktop-padtest`（独立 Electron userData）
- 独立 `DSH_HOME=E:\pack-agent\.tmp\desk2-home`，全新 `profiles/desktop`
- 文件级装入 `@sakikotgw/pad-session-jump` + `@sakikotgw/pad-gateway-jump`，重启副本壳
- 用 `--remote-debugging-port=9223` + CDP 直接检查渲染器

结果：

- renderer UA：`@deepseek-ai/dsh-desktop-padtest/0.2.1-rc.2 ... Electron/44.7.0`
- carrier ad：`{url: http://127.0.0.1:64884/api, pid: 75856, capabilities.jump: true, webUrl: ...64885, profile: "desktop"}`
- 渲染器注入：`__PAD_SESSION_JUMP_DEBUG__ = {phase:"ready", ready:true, hasOpener:true}`
- `POST /pad/jump {"sessionId":"session-f17907b0-…"}` → `200 {"ok":true,"seq":1}`（复测 seq 2）
- 渲染器事件：`queued → opened`（两次）
- 证据：`official-desktop-jump.log`、`official-desktop-jump.png`、`cdp-eval.mjs`、`cdp-shot.mjs`

副本是官方二进制，只改了 app.name；**当前桌面端与 `E:\DSH-Home` 未被改动**（此前 staging 已回滚）。

## 真实用户路径（可选，不依赖副本）

如果要在**当前真壳**里启用（需重启一次，会断本会话）：

```powershell
# 1) staging（文件级预置，不跑 pnpm；会先备份 package.json）
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\stage-desktop-e2e.ps1
# 2) 重启官方 DeepSeek Harness
# 3) 重启后
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\run-after-restart.ps1 -List
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\run-after-restart.ps1 -SessionId session-xxxxxxxx-...
```

预期：`pad-gateway.json` 出现、`/pad/ping` 200、`POST /pad/jump` 返回 `{"ok":true,"seq":N}`，窗口切到指定会话。

## 回滚

```powershell
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\stage-desktop-e2e.ps1 -Rollback
# 再重启官方壳
```

## 排查

- 无 `pad-gateway.json`：host 半没加载 → profile `package.json` 的 `dsh.profile.bundles` 是否含两个
  `@sakikotgw/*`，`node_modules\@sakikotgw` 文件是否在。
- ad 在但页面不切：消费端没加载 → bundles 是否含 `@sakikotgw/pad-session-jump`；
  渲染器里看 `window.__PAD_SESSION_JUMP_EVENTS__`（副本法可直接用 `cdp-eval.mjs`）。
- 在该 profile 跑 `dsh plugin add/remove` 可能清掉手工预置包；重装用 npm：
  `dsh plugin --profile desktop add @sakikotgw/pad-session-jump @sakikotgw/pad-gateway-jump`

## 来源依据

- 桌面端源码路径（asar）：`ready.injections`（= `ctx.webServer.collectIndexInjections()`）
  → `DESKTOP_IPC.boot` → preload `window.dshDesktopBoot` → 渲染器 `PZ()`，所以 host 半注入行会进渲染器。
- 官方壳没有外部深链（`dsh://` 只处理 `dsh://open` 聚焦窗口），这里走 gateway/jump 事件。
- 复制实例法关键：Electron 单实例锁按 userData 判重，`app.name` 在 asar 的 `package.json` 里；
  DSH home 由 `DSH_HOME` 独立决定，与 userData 无关。
