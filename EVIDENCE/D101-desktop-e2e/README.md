# 官方桌面端 E2E（交接运行）

## 为什么 agent 不能在自己会话里跑完

当前这个对话就跑在**官方桌面端 host** 里（`DeepSeek Harness.exe` → `dsh-desktop-host`，
profile = `E:\DSH-Home\profiles\desktop`）。加载新的 host/client 插件必须重启官方壳，
而重启会杀掉 `dsh-desktop-host` —— 也就是本会话的宿主进程。

2026-11-02 实测确认「另开一个壳」也不可行：

- 同一 exe 带 `--user-data-dir=…` 再启动：进程立即退出，**exit code 9**；
- 现有 renderer/gpu/utility 进程的 `--user-data-dir` 都被硬编码成
  `C:\Users\Brs\AppData\Roaming\@deepseek-ai/dsh-desktop`，参数不生效；
- 结果：官方壳严格单实例，无法在不关当前桌面的前提下加载新插件并做 E2E。

所以最终验证只有一条路：**staging → 重启官方壳（本会话会断）→ 跑跳转脚本**。

## staging 已回滚（当前桌面端未被改动）

安全考虑，agent 在做完上面的单实例实测后已把桌面 profile 还原：

- `package.json` 从 `package.json.bak-pad-jump` 恢复；
- `node_modules\@sakikotgw` 已删除；
- 当前桌面端/本会话继续正常运行。

## 想跑时：三步

```powershell
# 1) staging（文件级预置，不跑 pnpm；会先备份 package.json）
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\stage-desktop-e2e.ps1

# 2) 重启官方 DeepSeek Harness（关窗再打开；本会话 agent 会断，属预期）

# 3) 重启后跑验证（等 carrier ad → ping → POST /pad/jump）
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\run-after-restart.ps1 -List
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\run-after-restart.ps1 -SessionId session-xxxxxxxx-...
```

预期：carrier `pad-gateway.json` 出现、`/pad/ping` 200、`POST /pad/jump` 返回 `{"ok":true,"seq":N}`，
官方窗口切到指定会话。

## 回滚

```powershell
powershell -File E:\pack-agent\EVIDENCE\D101-desktop-e2e\stage-desktop-e2e.ps1 -Rollback
# 再重启官方壳
```

## 排查

- 无 `E:\DSH-Home\pad-gateway.json`：host 半没加载 → 确认 profile `package.json` 的
  `dsh.profile.bundles` 含两个 `@sakikotgw/*`，且 `node_modules\@sakikotgw` 文件在。
- ad 在但页面不切：消费端没加载 → 确认 bundles 含 `@sakikotgw/pad-session-jump`；
  渲染器 devtools 里看 `window.__PAD_SESSION_JUMP_EVENTS__`。
- 后续在该 profile 跑任何 `dsh plugin add/remove` 可能清掉手工预置包；重装请用 npm：
  `dsh plugin --profile desktop add @sakikotgw/pad-session-jump @sakikotgw/pad-gateway-jump`

## 来源依据

- 桌面端源码路径（asar）：`ready.injections`（= `ctx.webServer.collectIndexInjections()`）
  → `DESKTOP_IPC.boot` → preload `window.dshDesktopBoot` → 渲染器 `PZ()`，所以 host 半注入行会进渲染器。
- 官方壳没有外部深链（`dsh://` 只处理 `dsh://open` 聚焦窗口），这里走 gateway/jump 事件。
