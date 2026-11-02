# D100 · 会话跳转：消费端插件 v0.2 + gateway 跳转通道

日期：2026-11-02｜状态：代码已落地（24 项单测全过）；浏览器真机 E2E 已通 gateway 通道（0.2.1-rc.2），URL 通道在 0.2.x 不可用；官方桌面端 E2E 未做（见 `EVIDENCE/D100-session-jump-e2e/`）

## 1) 交付

| 包 | 目录 | 内容 |
|---|---|---|
| `@sakikotgw/pad-session-jump` v0.2.0 | `agent-pack-dsh/session-jump/` | `package.json`（bundle patch + client）、`cordis.patch.yml`、`lib/index.mjs`（host 注入）、`lib/client.js`（URL + gateway 双通道）、`test/client.test.mjs`（10）、`test/host.test.mjs`（6）、`README.md` |
| `@sakikotgw/pad-gateway`（扩展） | `agent-pack-dsh/gateway/` | `lib/jump.mjs`（队列 + HTTP 处理器）、`lib/index.js`（挂 `/pad/jump*`）、`test/jump.test.mjs`（7）、`README.md`（新建） |
| 设计稿 | `agent-pack-dsh/docs/session-jump.md` | §15 实现进展；§7.1 端点发现机制 |

## 2) 测试

```powershell
# 插件（17）
node --test --test-timeout=5000 test/client.test.mjs test/host.test.mjs
# gateway（7）
node --test --test-timeout=5000 test/jump.test.mjs
```

结果（node v24.21.0，2026-11-02）：

```
session-jump: tests 17 / pass 17 / fail 0
gateway:      tests 7  / pass 7  / fail 0
```

覆盖：id 三形态规范化；URL/`padSession` 打开；ready 等待；未知会话不阻塞；地址栏同步保留 token；`padSync=0`；gateway 长轮询→onEvent→ack；事件→`sessions.open`；注入行（global）从 `<DSH_HOME>/pad-gateway.json` 读取；ad 缺失/异 schema/短 token 拒绝；gateway 的 id/origin 校验、队列唤醒、TTL 修剪、HTTP POST/GET/ack、CORS 预检、超时 204。

## 3) 官方桌面端深链核实（本机 0.2.1-rc.2 asar）

| 主张 | 证据 |
|---|---|
| 注册 `dsh://` | `main.js:13054` |
| 只处理 `dsh://open` | `main.js:13055-13058` → `focusPrimaryWindow()` |
| 命令行只认 `--updated` | `main.js:12071` |
| 会加载 profile client 插件 | `main.js:12192-12193` `ready.injections`；`12423-12431` `DESKTOP_IPC.boot`；desktop-host = `ctx.webServer.collectIndexInjections()` |
| 注入行机制 | `dsh-host-webserver/lib/index.js`：`collectIndexInjections()` emit `webserver/index-inject`，`renderIndex()` 渲染；前端 `PZ()` 解释 `global/script/html` 行 |

结论：官方桌面端无外部深链，但加载第三方 client 插件 → gateway 通道是 app 面唯一可行路径。

## 4) 仍缺

1. ~~浏览器真机 E2E~~ → **已做（gateway 通道）**：`EVIDENCE/D100-session-jump-e2e/`（`POST /pad/jump → {"ok":true,"seq":1}`；页面 `queued → opened`）；URL 通道在 0.2.x 不可用（app 清 query）。
2. 官方桌面端 E2E：profile 装两包 → 开壳 → POST → 渲染器内切会话（预期可行，未实测；重启会中断当前 GUI）。
3. gateway 0.2.x 正式支持：需要 jump-only carrier 分支（不能带 `dsh-host-apiproxy` 依赖与 patch 行），并重新验证安装体验。
4. PAD 侧 `Core/Jump/*` + `pad cli session open --explain`（M1）。
5. 基准 `EVIDENCE/D100-jump-bench/`（R0 vs 第三方 deeplink）。
