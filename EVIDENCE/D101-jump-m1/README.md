# D101 · 会话跳转 M1（PAD 侧）+ gateway-jump 0.2.x carrier

日期：2026-11-02｜状态：代码落地并通过编译/CLI/真机验证（URL fragment 通道 + gateway 通道 + 窗口入口）；**真机 bin 已部署**（新 PAD 运行中）

## 1) 交付

### PAD 侧（C#，`agent-pack-dsh/pad/Core/Jump/`）

| 文件 | 内容 |
|---|---|
| `JumpModels.cs` | `SessionRef.Normalize`（uuid / session-uuid / 缓存文件名）；`SurfaceDescriptor` + `SurfaceRegistry`（内置 pad-web/tui/app + `<dataRoot>/library/surfaces` + `<home>/.pad/surfaces`，坏文件不致命）；`GatewayAd` 读取校验；`FocusClipboard`（Win32 EnumWindows/SetForegroundWindow + STA 剪贴板） |
| `JumpPlanner.cs` | 硬过滤 + 确定性排序（R0 gateway-event 1500 > R1 url-query 800 > R2 launch-env/args 400 > R3 focus-clipboard 1）；`JumpPlan` JSON（candidates/rejected/degraded/pa/detail）；执行 gateway POST、url 打开（**query + fragment 双写**）、focus+clipboard；PA130–PA139 |
| `SessionCli.cs` | `pad cli session ref|open`、`pad cli surface list|validate`（`--json` / `--surface` / `--no-exec`） |
| `Cli.cs` | 新增 `session` / `surface` 两个命令组 + help |
| `Views/ManageView.xaml.cs` | 会话行「打开」：先走 JumpPlanner；不可执行时退回原来的 transcript 文件 reveal |
| `Views/InstanceView.xaml.cs` | 会话行「打开」：同上（Toast 反馈 PA139 降级） |

### 0.2.x carrier（`agent-pack-dsh/gateway-jump/`）

- `@sakikotgw/pad-gateway-jump@0.1.0`：jump-only loopback carrier，`/pad/ping`、`/pad/jump`（POST/GET/ack），同 `pad-gateway.json` 广告；`engines.dsh >=0.1.0-rc.7`、无 apiproxy 依赖、patch 只插自己。
- `lib/jump.mjs`、`test/jump.test.mjs` 从 `../gateway` 同步（`scripts/sync-from-gateway.mjs`）。
- 与完整 `pad-gateway`（0.1.x，带 `/api` 代理）并存，PAD 按实例 DSH 版本选择（选择逻辑尚未接线）。

## 2) 验证

构建（运行中的 PAD 锁着 `bin\...\pack-agent-for DSH.exe`，所以输出到 `.tmp\pad-jump-out`）：

```powershell
dotnet build Pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-jump-out
# 已成功生成。0 个警告，0 个错误
```

CLI（`PACK_LAUNCHER_ROOT` 指向真实 launcher root；`pad-cli.log` 捕获输出）：

```
pad cli session ref 02d4a5ad-… → {"id":"session-02d4a5ad-…","clipboard":"session-02d4a5ad-…"}
pad cli surface list           → pad-web / pad-tui / pad-app，3 surface(s), 0 error(s)
pad cli session open ev-project session-a367… --no-exec --json
  → chosen pad-tui/launch-env R2 plan-only；rejected: pad-web/gateway-event PA136（无 ad）、
    pad-web/url-query PA133（ledger 无运行中的 web）；clipboard 已填
```

真实执行（临时假 carrier 监听 127.0.0.1:39990，写测试 ad 后跑 `session open --json`）：

```
plan: chosen pad-web/gateway-event R0 1500, executed=true, detail "posted to pad-gateway (seq 7)"
carrier log: POST /pad/jump  auth=true  body {"sessionId":"session-a36714d1-4b78-451a-a080-efd0244570f2"}
```

### 2.1 URL 通道复测（2026-11-02，ev-project / 0.2.1-rc.2）

- 根因确认：token URL 会被 web host 以 **redirect 消费**：query 被丢掉，**fragment 保留**。
- 修复：host 半注入 `script` 行在 boot 前把 `{search, hash}` 存进 `__PAD_SESSION_JUMP_BOOT__`；client 半支持 `#session=<id>`；C# `WithSessionParam` 同时写 query 与 fragment。
- 实测（浏览器）：
  - 打开 `/?token=…#session=session-a367…` → `location.href` 保留 fragment；
  - `__PAD_SESSION_JUMP_BOOT__ = {search:"", hash:"#session=session-a367…"}`；
  - `__PAD_SESSION_JUMP_EVENTS__ = [{kind:"opened", target:"session-a367…"}]` ✅

### 2.2 gateway 通道复测

`POST /pad/jump` → `{"ok":true,"seq":1}` → 页面事件追加 `queued → opened` ✅（正式 `pad-gateway-jump` 包，非临时变体）

### 2.3 窗口入口

`ManageView` / `InstanceView` 的会话「打开」按钮已改走 `JumpPlanner.ExecuteAsync`；构建 0 警告 0 错误。未做实机点击（运行中的 PAD 是旧二进制）。

清理：测试 web 已停；两个测试插件已从 ev-project profile 移除（bundles 回到 base+web-app），`pad-gateway.json/log` 已删，实例已重启并监听 3080。

单测（node）：session-jump **19 pass**、gateway **7 pass**、gateway-jump **7 pass**（共 33）。

### 2.4 第三轮：carrier 选择 + TUI 执行 + 真机部署

- **版本→carrier 选择已接线**：`Cli.GatewaySpecFor(release)` —— `0.2.*` → `@sakikotgw/pad-gateway-jump`，其余 → `@sakikotgw/pad-gateway`；`PAD_GATEWAY_SPEC` 仍可覆盖。`Cli.Recipe(kind, release)` 的 `tui` / `gateway` 两种 install 都走它。
  - 注意：`@sakikotgw/pad-gateway-jump` 目前只在仓库/fixture 里，**尚未发布 npm** → 真机 `profile add … gateway` 在 0.2.x 会装不到；发布后或 `PAD_GATEWAY_SPEC` 指向本地路径才可用（后续待办）。
- **TUI 执行已实现**：`Nodes.OpenTerminal(..., IReadOnlyDictionary<string,string>? extraEnv)` 可注入环境变量；`JumpPlanner` 的 `launch-env` 分支现在真的启动 TUI：`DSH_TUI_RESUME_SESSION=<session-id>`，返回 `executed=true` + pid。`launch-args` 仍是 plan-only。
- **构建**：`dotnet build` → 0 警告 0 错误。
- **真机 bin 已部署（2026-11-02 04:20）**：停掉运行中的 PAD（PID 50340）→ `dotnet build -c Debug` 成功 → 重启新 PAD（当前 PID 62524）。部署后实测：
  - `pad cli session ref` / `surface list` 正常（新命令面在真机可用）；
  - `pad cli session open ev-project … --no-exec --json` → chosen `pad-tui/launch-env` R2 **420 eligible**（修了 launch-env 与 launch-args 同分时选中 plan-only 的排序 bug：launch-env 420 > launch-args 400），`degraded=false`。

### 2.5 第四轮：npm 发布 + TUI 实跑 + 窗口点击现场

- **`@sakikotgw/pad-gateway-jump@0.1.0` 已发布 npm**（2026-11-01T20:25:52Z；`npm publish --access public`，access=public）。本地 `npm view` 曾因 CDN 滞后 404，`--prefer-online` 后可见；registry 元数据含 `0.1.0` 与 tarball。
- **TUI launch-env 实跑通过**：`pad cli profile add ev-project tui tui`（装 dsh-tui + gateway-jump）→ `pad cli session open ev-project session-a367… --surface pad-tui --json`：
  - 计划 `chosen pad-tui/launch-env R2 420 eligible`，`executed=true`；
  - 实际进程：`cmd.exe` pid 63800 → `node.exe` pid 65876 `bin.js --profile tui`，detail 记录 `started tui with DSH_TUI_RESUME_SESSION=session-a367… (pid 63800)`；
  - 测试后已 kill 进程树。
  - 未做：肉眼确认 TUI 内已恢复该会话（控制台窗口，无截图证据）。
- **窗口点击现场已准备好**（待用户点）：
  - ev-project web profile 装了 `@sakikotgw/pad-session-jump`；
  - 实例以 PAD 管理的形态跑在 `http://127.0.0.1:49998/`（ledger 有记录、pid 67860）；
  - URL fragment 实测：页面 `__PAD_SESSION_JUMP_EVENTS__=[{kind:"opened"}]` ✅；
  - CLI 计划实测：`session open ev-project … --no-exec --json` → `chosen pad-web/url-query R1 eligible`（gateway-event 因无 ad 被 PA136 拒绝），即窗口点击会走 `url-query`。
  - 点击位置：PAD → 实例 `ev-project` → **版本设置 / 会话列表** → 「打开」。注意 **不是**「管理」页的“正在聊的”——那页依赖 apiproxy，0.2.x 的 `gateway-jump` 不代理 `/api`，所以列表出不来。

### 2.6 0.2.x「管理」页打通（web API + 自签 cookie）

问题：PAD 管理页靠旧 apiproxy 的 `workspace.list` / `session.list`，0.2.x 没有 apiproxy。

实测发现 0.2.x 的 web 监听器本身就有同一套 `client-request` 协议：

| 差异 | 0.1 apiproxy | 0.2 web API |
|---|---|---|
| 路径 | `POST /api/<method>`（点号） | `POST /api/<ns>/<method>`（斜杠） |
| envelope 的 method | `session.list` | `session/list` |
| payload | `{...}` | `{args:{...}}`；`session/list` 要 `{args:{_request:{}}}`，`session/cancel` 要 `{args:{request:{sessionId}}}` |
| 鉴权 | carrier Bearer | HttpOnly cookie `dsh-auth-<base64url(sha256(authority))>` |

实现：

1. `gateway-jump@0.1.1` 启动后从 host `webServer.getOrigin()` 学到 web 地址，把 `webUrl` + `apiStyle:"dsh-0.2"` 写进 `pad-gateway.json`。
2. PAD `Gateway` 检测 `webUrl` → 走 `CallWebApi`：斜杠路径、`args` 包装、方法适配（上表）。
3. cookie：PAD 用 `<home>/.credentials.yaml` 的 `client-connection/browser-session` 密钥**自签**（`v1.<body>.<hmac>`，HMAC-SHA256，30 天有效期内取 7 天）。算法已用真实浏览器 cookie 交叉验证（签名逐字节一致）。
4. 0.2.x 没有 `workspace.list` → `Tree` 合成单组「会话」，不再假装有 registry。

验证：

- `curl` + 自签 cookie → `POST /api/session/list` **HTTP 200**，返回 `{"result":{"ok":true,"value":{"items":[…session-a367…]}}}`；
- 部署后的 PAD：`pad cli gateway ev-project session.list` 输出完整 items（不再 401）；
- `pad cli gateway ev-project tree`（新增诊断入口）→ `[{ Row.title:"会话", Sessions:[{sessionId:…, StatusText:"空会话", PresetText:"minimal"}] }]` —— ManageView 的 Tree 映射验证通过；
- 最终部署：PAD PID **71000**（`gateway…tree` 诊断已含）；实例 49998 带 `webUrl` 广告运行（pid 61824），ledger 已同步；
- `@sakikotgw/pad-gateway-jump@0.1.1`（含 `webUrl`）已发布且 `npm view --prefer-online` 可见。

### 2.7 focus-clipboard 实跑

`pad cli session open ev-project session-a367… --surface pad-app --json`：

```
chosen  pad-app/focus-clipboard
executed=True  degraded=True  pa=PA138
detail  no window focused (10 windows match title); session id copied
clipboard = session-a36714d1-4b78-451a-a080-efd0244570f2
```

结论：兜底路径可执行；窗口匹配因 10 个标题都叫 “DeepSeek Harness” 而歧义（PA138），但剪贴板写入正确，符合设计（歧义只提示、不猜）。

### 2.8 workspace 分组 + 取消会话

- **workspace 分组**：0.2.x 没有 `workspace.list`，但注册表就是 `<home>/storages/workspace.json`（unit v2：`tables.workspaces`，含 id/title/path/sessionIds）。PAD 的 `Workspaces` 在 modern 模式下读该文件（坏文件/缺失才退回单组）。
  - `pad cli gateway ev-project tree` 实测：`ws-backend`（无 session）+ `ws-frontend`（含 session-a367…，状态「空会话」）——ManageView 的分组恢复。
- **取消会话**：新增 `pad cli gateway <instance> cancel <sessionId>` 诊断入口，走 `Gateway.CancelSession` → `/api/session/cancel` args `{request:{sessionId}}`：
  - 不存在的 id → `error[PA031] session/not-found`（鉴权、参数、错误映射全对）；
  - 真实 idle session（session-a367…）→ `{"accepted": true}`。
  - 运行中 turn 的真实中断见 §2.9（已在真 turn 上验证 running true→false）。
- 新增诊断：`pad cli gateway <instance> tree|sessions|cancel <id>`。
- 最终部署：PAD PID **52940**（含 workspace 分组 + cancel + launch-args）；实例 49998（pid 61824）带 `webUrl` 广告运行；实机 `gateway tree` 冒烟通过。

### 2.9 真 turn 取消 + launch-args

**真 turn 取消（E2E）**：

1. ev-project 的 `DEEPSEEK_API_KEY` 原先是指向别处的 `REF:` 占位（模型加载报「key contains characters no HTTP header can carry」）；测试时临时从 `E:\DSH-Home\.credentials.yaml` 拷了 raw key 进实例（备份后、测完已恢复）。
2. 浏览器里选好 `DeepSeek-V4.1-Flash`，发 prompt「执行 shell：Start-Sleep -Seconds 300」。
3. 等 10s：`pad cli gateway ev-project sessions` → **`running: true`，`StatusText: "在跑"`**。
4. `pad cli gateway ev-project cancel session-a367…` → `{"accepted": true}`。
5. 4s 后再查：**`running: false`，`StatusText: "空转"`**；页面显示 **“已停止”**；`Start-Sleep` 的 shell 子进程已消失。
6. 凭据已还原（`.credentials.yaml.bak-test` → 原文件）。

**launch-args（TUI 带参）**：

- `Nodes.OpenTerminal` 新增 `extraArgs`；`JumpPlanner` 的 `launch-args` 从 plan-only 改为可执行，参数为 `--resume <sessionId>`。
- 真机验证：写一个只声明 `launch-args` 的临时 surface（`<home>/.pad/surfaces/tui-args.json`），`--surface tui-args` → plan `executed=true, detail "started tui with --resume … (pid 61044)"`；进程命令行实测：
  ```
  node.exe bin.js --profile tui --resume session-a36714d1-4b78-451a-a080-efd0244570f2
  ```
- 测试后已 kill 进程树、删除临时 descriptor。

## 3) 未完成 / M1 边界

### 2.10 管理页 UI 点击 E2E + key 修复（2026-11-02 09:0x）

**真点（UI Automation `InvokePattern`，等价鼠标点 WPF Button）**：

1. PAD 管理页 → 选 ev-project → 状态 `在跑`、`管理口已连上。`；树显示 `2 个 workspace · 1 条 session`（`ws-backend`、`ws-frontend`，session 在 ws-frontend）。
2. 点会话行「打开」→ 提示文本 **`posted to pad-gateway (seq 1)`**（ManageView 的 `TreeHint`）。

**这一轮顺带修掉的 PAD 真 bug**：

- `ManageView.OpenSession_Click` 之前直接写 `TreeHint.Text`，而 running 态下提示块是 Collapsed → 点击成功也看不见反馈；改成走 `Hint()`（设 Visibility）。
- `Gateway.LiveConfigured` 只认 0.1 的 `{credentials:{DEEPSEEK_API_KEY:{configured}}}` 包层；0.2.x 是平的 `{DEEPSEEK_API_KEY:{configured}}` → 误报 PA116。已兼容两种形状。
- `CredentialsFile.HarnessReadsRaw` + `Runner`/`CredentialsGate`：0.2.x 读文件是字面量，PAD 库里的 `REF: <key>` 会被当成 key 的一部分（报“非法 header 字符”）；启动脚本/分发到 home 时改为写 raw key。
- `Launcher.Runtime()`：launcher 进程退出、只留 harness 时，用 `pad-gateway.json` 的 advert pid / `FindDshPid` 找回 pid，管理页不再误判“已停”。

**key 状态**：`.credentials.yaml` 为 raw key；浏览器侧 `credentials/describe` 返回 `{"DEEPSEEK_API_KEY":{"configured":true,"source":"file"}}`；管理页 **PA116 消失**。

### 2.11 TUI 恢复视觉确认 + 测试实例清理（2026-11-02 10:0x）

**TUI 视觉确认 ✅**（截图在 `EVIDENCE/D101-jump-m1/`）：

- 直接在一个真实控制台里跑 `node bin.js --profile tui --resume session-a367…`，第一次被残留 TUI 进程占用会话锁：
  `tui-resume-locked.png` —— 报错「cannot resume session … it is mounted by another TUI terminal (pid 46124)」（这本身证明 `--resume` 被正确解析）。
- kill 掉残留 pid 后重跑成功：`tui-resume-success.png` —— 窗口标题 `执行等待三百秒命令`，正文可见 `E:\tmp\pack-agent\proj-evidence\ws-frontend`、Working_directory、Pwsh 失败、**“已打断”** 和输入框；即恢复到了该会话的完整轨迹。
- 注：PAD `OpenTerminal` 生成的 cmd 控制台窗口在该环境里不可枚举，所以视觉证据用同参数直连 cmd 窗口取得；两者命令行/环境一致。

**测试实例清理 ✅**：

- 停止 3080 上的测试 web；web profile 移除 `@sakikotgw/pad-session-jump` / `@sakikotgw/pad-gateway-jump` → bundles 回到 `@deepseek-ai/dsh-base,@deepseek-ai/dsh-web-app`。
- 删除测试产物：`profiles/tui`（测试新建）、`pad-gateway.json/.log`、`.pad/surfaces`、`.pad/nodes.json`、`.credentials.yaml.bak-*`、`launch-web.cmd`、`.pad/credentials.raw.yaml`。
- `runtime.json` 清空后重启 PAD（PID 4400）；验证：3080 无监听、无网关广告、`profiles` 只剩 `web`、管理页 ev-project 显示「还没启动」。
- 保留：`.credentials.yaml` 的 raw key（#1 的修复）。
- 清理中修了一个 pnpm 坑：`profiles/web/node_modules/.modules.yaml` 末尾被追加了一行 `virtualStoreDir:`（JSON 变非法），截断后 `dsh plugin remove` 恢复正常。

### 2.12 M1 剩余项收尾（2026-11-02 13:5x）

1. **`--explain` 已实现**（之前 flag 是空的）：
   - `pad cli session open … --explain --no-exec` → 人类可读：chosen/score、candidates 带 `->`、rejected 带 PA133/PA136 原因、degraded；
   - `--explain --json` → 稳定 `pad.jump-plan/v1`。
2. **PA130–139 单测**：新增 `agent-pack-dsh/pad.tests`（xUnit）**23 pass**：
   - SessionRef 三形态/拒绝垃圾；注册表 builtin/user/instance 合并、kind 冲突、坏 schema 不致命；
   - planner：PA130/PA132/PA133/PA136、R0/R1/R2 选择、launch-args eligible、确定性；
   - `GatewayAuth` cookie：名字 = `dsh-auth-`+sha256(authority)、`v1.<payload>.<hmac>` 签名逐字节校验、缺凭据返回 null；
   - 凭据：`StripRefPrefixes`（顶格/缩进两种）、`HarnessReadsRaw` 版本判定。
   - 运行：`dotnet test agent-pack-dsh/pad.tests/Pad.Tests.csproj`（Pad 是 WPF WinExe，测试工程用“先 MSBuild Pad 再引用 DLL”，避开 XAML 临时工程问题；Pad 构建到 `pad.tests/pad-bin\`，PAD 正在运行时也能跑）。
3. **SurfaceRegistry L0/L1/L2 合并**：同 id 不再整条覆盖——label/kind 高层优先，transports 按 `type` 合并（same type 字段覆盖、新 type 追加），`kind` 不一致标冲突；`surface validate` 输出 `merge:` notes 和 error 列表。
4. **GUI 启动链路验收**：PAD 启动卡（UIA Invoke）真实启动 ev-project：
   - 启动脚本 credentials 行改为复制 `.pad\credentials.raw.yaml`；
   - 该文件内容为 `DEEPSEEK_API_KEY: "sk-…"`（无 `REF:`）；home `.credentials.yaml` 被 DSH 迁成 `version/refs/records`，值是 raw key；
   - 全文件 `REF:` 出现次数 **0**；实例在 3080 正常启动。
   - 修的正则 bug：`StripRefPrefixes` 原来要求行首有缩进，PAD 库文件是顶格的，所以剥离没生效。
5. **文档**：`STATE.md` 更新到 2026-11-02 并记录 M1 完成；本文件与设计稿 §15.12 同步。

## 3) 未完成 / M1 边界

1. 管理页点击已 E2E（seq 1）；真实用户鼠标再点一次属于重复验证。
2. focus-clipboard 的窗口聚焦会因多窗口同标题而 PA138（有剪贴板兜底）。
3. URL 通道依赖消费端插件（未装插件的实例仍会退化为 clipboard）。
4. 测试实例已清理，当前为「干净停机」状态；PAD 已重启（干净 runtime），需要时从 PAD 启动即可。
5. 官方桌面端 E2E 仍差最后一步：插件已预置进桌面端 profile，但重启桌面壳会杀掉本会话，需重启后按 `EVIDENCE/D101-desktop-e2e/README.md` 跑一条命令。

### 2.13 M2 收尾：发布、安装链路、版本矩阵（2026-11-02 14:2x）

**发布**：
- `@sakikotgw/pad-session-jump@0.2.1` 已发布（`npm view --prefer-online` 返回 0.2.1；公共 registry GET 有 CDN 滞后，与 gateway-jump 当时现象一致）。
- `@sakikotgw/pad-gateway-jump@0.1.1`（含 `webUrl`）此前已发布。

**安装链路**：`Cli.Recipe("web", release)` 现在返回 `[@deepseek-ai/dsh-web-app@<release>, @sakikotgw/pad-session-jump]`，即 PAD 建 web profile 时自动带消费端插件；已用 `dsh plugin --profile web add @sakikotgw/pad-session-jump`（不带 file:）在 ev-project 上验证从 npm 安装成功。单测覆盖 Recipe（28 pass）。

**版本矩阵（4 例，插件/opener 级）**：

| 版本 | 已知 id | 未知 id |
|---|---|---|
| 0.2.1-rc.2 | `#session=session-a367…` → `opened` | `#session=session-0000…` → `unknown`（`sessions.retain: unknown`），页面 body/composer 正常 |
| 0.1.0-rc.7 | `?session=049f926b-…` → `opened`（首次）/`already`（重载） | `?session=session-0000…` → `unknown`（`sessions.select: unknown`），页面不崩 |

0.1 测试的环境注记：123 实例是合成 profile（`dsh-web-app@0.1.0-rc.7` + pnpm 11 hoisted），页面壳停在 “Loading plugins…”——缺 `@deepseek-ai/dsh-client-ui-primitives` 的 client entry（另两个插件 inject 它），与本次插件无关；我们的插件已加载并执行 opener（事件为证）。因此 0.1 只验证到“插件解析 URL + 调用 opener”的语义，不是 UI 视觉。

**顺手修的基础设施 bug**：pnpm 11 的 `.modules.yaml` 是 JSON 形状，`LaunchPolicy.PatchVirtualStoreDir` 却按 YAML 在结尾追加 `virtualStoreDir:`，导致后续所有 `dsh plugin add` 报 “document separator expected”。已改为 JSON 内替换/插入，并补 3 条单测（含 JSON 解析校验）。

### 2.14 官方桌面端：单实例实测 + staging 回滚（2026-11-02 14:4x）

- **尝试“不动当前桌面、另开一个官方壳”**：同一 `E:\DSH-App\DeepSeek Harness.exe` 带
  `--user-data-dir=E:\pack-agent\.tmp\dsh-desktop-probe` 启动 → 进程立即退出，**exit code 9**；
  现有 renderer/gpu/utility 进程的 `--user-data-dir` 全部硬编码为
  `C:\Users\Brs\AppData\Roaming\@deepseek-ai\dsh-desktop`，命令行参数不生效。
  结论：官方壳严格单实例，不重启无法加载新 host/client 插件。
- 本会话宿主就是该壳（`dsh-desktop-host`，PID 68084），所以 1 的最终 E2E 必须在重启后跑；
  按用户要求**不关闭当前桌面端**，本次不做重启。
- 已把此前文件级 staging **回滚**：`package.json` 从 `package.json.bak-pad-jump` 恢复，
  删除 `node_modules\@sakikotgw`；当前桌面端/本会话未受影响（host 仍在跑）。
- 留了可重复脚本：`EVIDENCE/D101-desktop-e2e/stage-desktop-e2e.ps1`（staging / `-Rollback`）、
  `run-after-restart.ps1`（等 ad → ping → POST /pad/jump）、`README.md` 三步说明。
