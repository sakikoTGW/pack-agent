# 会话跳转（Session Jump）设计稿

- 状态：**代码已落地（2026-11-02；插件 v0.2 + gateway `/pad/jump`；浏览器真机 E2E 已通 gateway 通道；URL 通道已加 boot 前捕获待复测；M1 的 PAD 规划器与 CLI 已编译验证；0.2.x 专用 carrier `pad-gateway-jump` 已落地）**；见 §15.4 / §15.5
- 背景：D67 当时测出「DSH 核心没有会话深链 → 打开对话只能复制 id」。2026-11 复测更正：**核心确实没有，但插件层已解决**（`dsh-session-deeplink` 的 `?session=`、`dsh-session-link` 的 `dsh://session/…`）。同时确认：**按 surface 枚举是错的方向**——DSH 的界面形态是开放集合（web / tui / app / headless + 24 个第三方壳 + 未来形态），PAD 不可能穷举。
- 本文定义：PAD 只做两件稳定的事——① 确定「哪个会话」（surface 无关）；② 按**能力注册表**把跳转请求送达界面（surface 是数据，不是代码）。
- 关联：D9（profile/surface 正交）、D67（需求来源，需补更正记录）、D77/D79（不重画、不内嵌 DSH 界面）、§20 注册表体系（引擎解释注册表，不内置种类）、D99（托盘/单实例已落地，跳转可聚焦托盘恢复的窗口）。
- 当前基线：PAD v0.4.5；`pad-gateway` 0.1.0（每实例 loopback HTTP carrier，`pad-gateway.json` 广播 url+token）；`sessions` 客户端服务在 DSH 0.1.x/0.2.x 均存在（已在装机的 0.2.x 插件中核实）。

---

## 1. 问题与目标

「打开对话」= 用户点一条会话 → 目标界面显示该会话。难点不在“跳”，在于：

1. **会话引用不统一**：PAD 手里是 `home + session id`；各界面有各自的入口（URL 参数 / CLI 参数 / 环境变量 / 协议 / 完全没有）。
2. **surface 无穷**：任何第三方都能再套一层壳，PAD 不能为每个壳写分支。
3. **失败不能被掩盖**：跳不过去时必须如实降级（聚焦 + 剪贴板），不能假装成功（D15/D18 的事实纪律）。

**目标（v1）**
- 一个 `SessionRef` 身份模型 + 一条 `pad cli session open` 命令 + 一个 UI 入口，能覆盖 PAD 自己启动的 web/tui/app 三个面。
- surface 通过**能力描述符**接入；新增 surface 不改 PAD 核心代码。
- 跳转可解释：`--explain` 输出候选、被选中的 transport、拒绝原因、降级路径。
- 性能可度量、可对比（见 §9），并给出预算与验收门槛。

**非目标**
- 不内嵌 DSH 界面（D77 之后已无嵌入入口；不因跳转需求复活它）。
- 不注册/抢占 `dsh://`（官方桌面端已注册，第三方插件也在抢）。
- 不新增常驻进程或默认端口（复用已有 `pad-gateway` 是上限）。
- 不做跨 home、跨机器、非 loopback 的跳转。
- 不替用户切换 DSH 内部界面结构（不重画）。

---

## 2. 不变量

1. 会话身份 = `(home, session-id)`；**跨 home 一律拒绝**（PA131）。
2. 跳转是「请求送达」，不是「保证显示」；planner 必须给出实际送达等级，UI 按等级如实展示。
3. 只允许 loopback；URL/协议/endpoint 必须是 `127.0.0.1|localhost`。
4. 不接管 DSH 鉴权：复用实例自己的 token/cookie，不另造登录。
5. 描述符是本地数据，不是可执行代码；descriptor 不得包含 shell 命令、token、绝对路径之外的注入。
6. 任何失败都落到两级兜底：**聚焦目标窗口 → 剪贴板放规范会话引用**，并给出 PA 码。

---

## 3. 分层架构

```
┌─ Fact 层：实例账本（nodes.json）+ 会话缓存（session_projcache / apiproxy session.list）
│    产出 SessionRef { home, id, workspace, profile, surface?, running? }
├─ Registry 层：SurfaceDescriptor[]（内置 + 用户级 + 实例级）
│    每项声明：怎么识别、是否在跑、支持哪些 transport、参数模板
├─ Planner 层：过滤 → 打分排序 → 生成 JumpPlan { chosen, candidates[], rejected[], degraded }
├─ Transport 层（固定 6 种原语，不随 surface 增加）
│    gateway-event / url-query / launch-env / launch-args / rendezvous-file / focus-clipboard
└─ Consumer 层：第一方 client 插件（web 面）+ 任何愿意消费 rendezvous 的壳
```

**核心原则**：planner 只认识「能力」，不认识「surface 名字」。surface 名字只出现在描述符数据里。

---

## 4. 会话身份与引用格式

```csharp
// Core/Jump/SessionRef.cs
public sealed record SessionRef(
    string Home,        // 规范化绝对路径
    string Id,          // 规范会话 ID，形如 session-<uuid>（见下）
    string? Workspace,  // cwd（可选，用于 UI 提示）
    string? Profile,    // 可选
    string? Surface,    // 可选：用户显式指定目标面
    string? RunningPid); // 可选：来自账本/进程现场
```

- **ID 规范化**：DSH 会话缓存文件名是 `<uuid>.json`，Web/插件层使用 `session-<uuid>`。`SessionRef.Normalize(id)` 接受 `uuid` / `session-<uuid>` / 缓存文件名三种输入，统一成 `session-<uuid>`；严格校验 `^session-[A-Za-z0-9-]{8,64}$`，否则 PA132。
- **对外引用（剪贴板/协议）**：
  - 首选官方 `dsh-session:` 规范引用（`@deepseek-ai/dsh-session-reference` 的 URI 形态，跨传输层可解析；TUI 里粘贴同样生效）。
  - 次选纯文本 `session-<uuid>`（人类可读，粘到 Web 搜索/粘贴到任意界面都能用）。
  - PAD **不发明**新的全局 scheme；若未来需要本地协议，只用 `pad-session:` 且仅由 PAD 自身描述符声明（不抢 `dsh:`）。

---

## 5. 能力注册表 schema v1（`pad.surface/v1`）

### 5.1 位置与优先级

| 级别 | 路径 | 说明 |
|---|---|---|
| L0 内置 | 编译进 PAD | PAD 自己启动的三个面（web/tui/app），bootstrap 用；可被覆盖 |
| L1 用户级 | `<dataRoot>/library/surfaces/*.json` | 用户手工放入；PAD 设置页可校验/导入 |
| L2 实例级 | `<home>/.pad/surfaces/*.json` | 跟随实例/pack 走；优先级最高 |

同一 `id` 的覆盖顺序：**L2 > L1 > L0**。`id` 必须匹配 `^[a-z0-9][a-z0-9-]{1,40}$`。

### 5.2 描述符字段

```json
{
  "schema": "pad.surface/v1",
  "id": "pad-web",
  "label": "Web（Edge app 窗 / 浏览器）",
  "kind": "web",                       // web | tui | app | headless | custom
  "detect": {
    "processName": ["msedge.exe"],     // 可选：进程名匹配
    "port": 3080,                      // 可选：实例端口
    "gatewayAd": "pad-gateway.json",   // 可选：读 gateway 广告文件
    "always": true                     // 规则：任一命中即视为候选
  },
  "launch": {                          // 仅 PAD 能启动的面填写；外来壳填 null
    "surface": "web",                  // web | tui | app | headless（走 PAD 现有 LaunchPolicy）
    "env": { "DSH_HOME": "{{home}}" }, // 可选：模板
    "args": ["--resume", "{{sessionId}}"] // 可选：仅 PAD 启动的面可用
  },
  "transports": [
    { "type": "gateway-event", "endpointFrom": "pad-gateway.json", "event": "session.jump" },
    { "type": "url-query", "base": "{{url}}", "sessionParam": "session" },
    { "type": "focus-clipboard", "match": { "home": "{{home}}" } }
  ],
  "constraints": { "loopbackOnly": true, "maxAgeMs": 30000 },
  "bias": 0
}
```

### 5.3 transport 对象（固定 6 种）

| type | 字段 | 语义 |
|---|---|---|
| `gateway-event` | `endpointFrom`、`event` | 通过实例内 `pad-gateway`（已有 loopback HTTP + bearer）推送跳转事件；页面插件长轮询/SSE 接收后 `sessions.open()`；**不重载页面** |
| `url-query` | `base`（通常 `{{url}}`，带实例 token）、`sessionParam` | 在目标窗口导航到 `?<param>=<id>`（保留原 query）；需 profile 内装消费端插件 |
| `launch-env` | `env`（模板） | PAD 启动该面时注入环境变量；例：tui 的 `DSH_TUI_RESUME_SESSION={{sessionId}}` |
| `launch-args` | `args`（模板数组） | PAD 启动该面时追加参数；仅限 PAD 启动的面 |
| `rendezvous-file` | `path`、`format` | 写 `<home>/.pad/jump/<seq>.json`；能读盘/文件监听的壳自行消费（Tauri/Electron 壳零代码接入） |
| `focus-clipboard` | `match` | 聚焦目标窗口 + 剪贴板写规范引用；**恒可用兜底** |

### 5.4 校验规则（拒绝即降级，不报错即用）

- 未知 `type` / 未知 `kind`：忽略该 transport，记录 reason。
- `url-query.base`、`gateway-event` endpoint：非 loopback → PA134。
- `launch-env`/`launch-args`：仅当 `launch != null`（PAD 能启动）才生效；外来壳声明无效（防任意命令执行）。
- `{{...}}` 模板只允许 `home / url / token / sessionId / profile / port` 六个变量，禁止函数调用。
- 描述符文件必须 0600；PAD 读取时做 schema 校验，错误逐条回报（`pad cli surface validate`）。

### 5.5 三个内置描述符（L0）

| id | kind | transports（按优先级） | 备注 |
|---|---|---|---|
| `pad-web` | web | `gateway-event` → `url-query` → `focus-clipboard` | 消费端插件是 PAD 第一方组件；无插件自动降级 |
| `pad-tui` | tui | `launch-env`（`DSH_TUI_RESUME_SESSION`）→ `launch-args`（`--resume`）→ `focus-clipboard` | 需 profile 含 dsh-tui bundle |
| `pad-app` | app | `gateway-event`（需 client 插件在官方桌面端内加载 + 端点发现）→ `focus-clipboard` | **无外部深链**：官方壳的 `dsh://` 只认 `dsh://open`（聚焦），argv 只认 `--updated`（本机 0.2.1-rc.2 asar 已核实）；但 host 的 `ready.injections` 来自 `ctx.webServer.collectIndexInjections()`，证明官方桌面端会加载 profile 的 client 插件 → 走 gateway 事件可在桌面端内跳转 |
| `pad-headless` | headless | `focus-clipboard`（提示无界面） | 不参与跳转 |

---

## 6. Planner：优先级与算法

### 6.1 硬过滤（顺序执行，失败即该候选淘汰）

1. `home` 一致（PA131）→ 2. `sessionId` 存在（缓存或 apiproxy；PA132）→ 3. 描述符 schema 合法（PA134）→
4. 目标面存活（进程/端口/账本；PA133）→ 5. transport 前置条件满足（如消费端插件已装、gateway 可达；PA135/136）。

### 6.2 交互等级（决定“打断程度”，第一优先级）

| 等级 | 语义 | 代表 transport |
|---|---|---|
| **R0 无打断** | 当前窗口已开，不重载、不新建 | `gateway-event`、`rendezvous-file`（watcher） |
| **R1 同窗重载** | 复用窗口，页面重新加载 | `url-query` |
| **R2 启动/切窗** | 新开或冷启动一个面 | `launch-env`、`launch-args`、`protocol` |
| **R3 仅提示** | 不导航，只聚焦+剪贴板 | `focus-clipboard` |

### 6.3 排序规则（确定性）

```
score = 1000 × R等级权重(R0=0,R1=1,R2=2,R3=3) 的负向
      + 200 × 存活度(前台=0, 在跑=1, 未跑=2)
      + 100 × 用户显式指定(--surface 命中=0, 未指定=1)
      + 1   × descriptor.bias
      + 0.1 × 稳定 id 字典序（唯一 tie-break）
```

- **R 等级优先于存活度**：已经开着的页面里无重载切换，永远优于冷启动（这也是 §9 性能优势的来源）。
- `--surface <id>` 是强制收敛：只在该描述符及其 transport 内选；全部不可用时报 PA135，不回落到别的面（用户意图优先）。
- 同级同分时按 `id` 字典序，保证同一台机器同一状态下 plan 可复现（可写进 EVIDENCE）。

### 6.4 JumpPlan 输出（`--explain --json`）

```json
{
  "schema": "pad.jump-plan/v1",
  "session": { "home": "E:\\DSH-Home", "id": "session-…", "workspace": "E:\\proj" },
  "chosen": { "surface": "pad-web", "transport": "gateway-event", "rank": "R0" },
  "candidates": [
    { "surface": "pad-web", "transport": "gateway-event", "score": 1201, "state": "eligible" },
    { "surface": "pad-web", "transport": "url-query", "score": 801, "state": "eligible" },
    { "surface": "pad-tui", "transport": "launch-env", "score": 402, "state": "eligible" },
    { "surface": "pad-app", "transport": "focus-clipboard", "score": 1, "state": "eligible" }
  ],
  "rejected": [ { "surface": "pad-web", "transport": "gateway-event", "code": "PA136", "why": "gateway 不可达" } ],
  "degraded": false,
  "pa": null
}
```

### 6.5 新诊断码（PA130–PA139，接入既有 PA 表）

| 码 | 含义 |
|---|---|
| PA130 | 没有可用的目标面（注册表为空/全被过滤） |
| PA131 | home 不匹配（跨 home 拒绝） |
| PA132 | 会话不存在或 id 非法 |
| PA133 | 目标面已不在运行 |
| PA134 | 描述符/transport 不合法（含非 loopback） |
| PA135 | 指定 surface 不可用（`--surface` 模式） |
| PA136 | endpoint 不可达（gateway/页面） |
| PA137 | 跳转超时（送达确认未回） |
| PA138 | 窗口匹配歧义（多个壳/多窗口，需用户确认） |
| PA139 | 已降级为聚焦 + 剪贴板（warning，不是错误） |

---

## 7. Transport 契约

### 7.1 gateway-event（R0，性能主路径）

- **端点发现（v0.2 定案方向）**：host 半订阅 `webserver/index-inject` 事件，push `{kind:"global", name:"__PAD_SESSION_JUMP__", value:{gatewayUrl,token,home}}`。据 `dsh-host-webserver/lib/index.js`：`collectIndexInjections()` 发 `webserver/index-inject` 让订阅者填表，`renderIndex()` 把行渲染进 index.html；桌面端把同一批行经 `ready.injections` → preload `dshDesktopBoot.ready()` → 前端 `PZ()` 解释（`case "global": globalThis[name]=value`）。**同一机制覆盖浏览器与官方桌面端，无 CORS/无探测。**
- **前置**：实例在跑且 `pad-gateway.json` 有效（`Core/Gateway.cs` 已实现探测）；profile 装了第一方消费端插件。
- **流程**：PAD 读 `pad-gateway.json` → `POST /pad/jump`（Bearer token，body：`{sessionId, seq, ttlMs}`）→ gateway 内存队列保存 pending → 消费端插件 `GET /pad/jump?since=<seq>` 长轮询（25s 超时）→ 收到后 `sessions.open(id)` → `POST /pad/jump/ack` 回报耗时 → PAD 收到 ack 记 `t_visible`。
- **CORS/Origin**：gateway 只对**本实例 web 页面的 origin**回 CORS；token 仍是 Bearer；不向 LAN 暴露。
- **幂等/TTL**：`seq` 单调递增，事件 30s 过期；同一 `(home, seq)` 只消费一次。
- **性能**：p50 < 150ms（同机 loopback 长轮询），页面不重载、对话状态保留。
- **兼容性风险**：`pad-gateway` 当前 `engines.dsh` 是 `>=0.1.0-rc.7 <0.2.0`，用于 0.2.x 前必须先升级 gateway（见 §13）。

### 7.2 url-query（R1）

- 需要实例账本里的 url（含 token）；追加 `&session=<id>` 并保留既有参数；只对 loopback。
- 由消费端插件解析；未知会话 → 插件 warn，PAD 收到 ack=false 报 PA132 并降级。
- 性能：p50 ≈ 本地 SPA 重载 + 首屏（目标 < 800ms），与现有 deeplink 插件同级。

### 7.3 launch-env / launch-args（R2）

- 仅对 PAD 能启动的面（`launch != null`）生效。
- TUI：`DSH_TUI_RESUME_SESSION=<id>`（本机 dsh-tui 插件 3 处读取点已核实）；`launch-args` 可追加 `--resume <id>`（其 bin 支持 `--resume`/`--resume=`/`-c`）。
- web/app：env 只放 `DSH_HOME` 等既有变量，不用于跳转（app 无入口）。

### 7.4 rendezvous-file（R0/R2 混合）

- 路径：`<home>/.pad/jump/<seq>.json`（0600，含 `{sessionId, home, at, ttlMs}`），保留最近 20 条。
- 消费方式：壳自己 watcher/启动时读；PAD 不等待消费结果（无 ack 时 plan 标 `degraded`）。
- 用途：第三方壳/未来形态**零代码接入**；官方桌面端若将来加载 profile 插件也可复用。

### 7.5 focus-clipboard（R3 兜底）

- 窗口匹配顺序：账本 pid → 端口 → 标题（`DSH_HOME`/实例名）→ 用户确认（PA138）。
- 剪贴板内容：`dsh-session:` 规范引用（可用时），否则 `session-<uuid>`。
- 永远成功（除剪贴板被占用，报 PA139 warning 但窗口已聚焦）。

---

## 8. 消费端契约 v1（第一方 client 插件）

包名建议 `@sakikotgw/pad-session-jump`（client-only + 空 host 壳，`dsh.bundle.patch` + `dsh.client`）。

必须行为：
1. `inject = ["sessions"]`；用 `ctx.sessions.list.getSnapshot()`（`{phase, byId, current}`）、`ctx.sessions.open(id)`、`ctx.sessions.list.subscribe()`；**先确认 `phase === "ready"`** 再打开。
2. 入口优先级：URL `?session=`（服务 server 对 query 路径正常回 index）→ gateway-event 长轮询 → 无。
3. 全部 API 调用做特性探测（`ctx.sessions?.open` 存在才用），失败只 console.warn，绝不阻塞页面。
4. 地址栏同步：`history.replaceState` 维护 `?session=`，可在设置关闭（默认开）。
5. **不注册协议**、不依赖 `@deepseek-ai/dsh-session-reference`（避免多一层依赖；仅在剪贴板引用格式上对齐）。
6. 版本矩阵：`0.1.0-rc.7 / 0.1.1-rc.2 / 0.2.1-rc.2 / 0.2.2-alpha`，逐版本跑“打开/未知 id/无 session 参数/服务缺失”四例；服务缺失时插件静默空转。
7. 体积预算：打包后 < 8KB（混淆前），页面加载增量 < 2ms（PAD 自测）。

---

## 9. 性能：预算、对比与测量（本次重点）

### 9.1 指标定义

| 指标 | 定义 |
|---|---|
| **T_plan** | 点击 → planner 产出 plan（纯 C#/内存，不含系统调用外的 IO） |
| **T_dispatch** | 点击 → transport 实际发出请求/启动进程 |
| **T_visible** | 点击 → 目标会话在界面可见（gateway-event 由 ack 上报；url-query 由消费端插件上报） |
| **Footprint** | PAD 额外进程数 / 额外监听端口 / 额外常驻内存 |
| **Reload** | 跳转是否导致页面重载（1/0，影响对话现场是否保留） |

### 9.2 预算（验收门槛）

| 项 | p50 | p95 | 备注 |
|---|---|---|---|
| T_plan | ≤ 15ms | ≤ 40ms | 注册表小（≤20 描述符），纯内存 |
| T_dispatch（gateway-event） | ≤ 25ms | ≤ 60ms | loopback POST |
| T_visible（gateway-event，R0） | ≤ 150ms | ≤ 400ms | 无重载 |
| T_visible（url-query，R1） | ≤ 800ms | ≤ 1500ms | 本地 SPA 重载 |
| T_visible（launch-env tui，R2） | ≤ 3.5s | ≤ 8s | 终端冷启动为主 |
| 消费端插件页面加载增量 | ≤ 2ms | ≤ 5ms | PerformanceObserver |
| Footprint（相对现状） | +0 进程、+0 端口、+0 常驻内存 | — | 复用 pad-gateway；R3 兜底无网络 |
| Reload（R0 路径） | 0 | — | 这是相对竞品的硬差异 |

### 9.3 能不能比别人好：分场景结论

**能明确更好的**
1. **R0 无重载跳转**：现有 deeplink 插件（`dsh-session-deeplink`/`dsh-session-link`）都是 URL 导航 → 页面重载；PAD 用已有 gateway 做 in-page 事件，**保留对话现场、T_visible 目标 ≤150ms**。
2. **零新增常驻**：不新起 daemon/端口/进程（gateway 已在跑时才用）；对比「内置 WebView 的壳」（≥11 家）整天挂一个 Chromium 视图，PAD 跳转路径的常驻开销为 0。
3. **plan 可解释、可复现**：竞品没有 planner，跳转选谁是隐式的；PAD `--explain` 给出候选/拒绝/降级，可进 EVIDENCE 当回归基线。
4. **覆盖面**：表面上看是功能，实际是性能——**新增 surface 不改变代码路径和 PAD 体积**；竞品每支持一个面都要发版。

**只能打平的**
- R1（url-query）路径的 T_visible 与 deeplink 插件同级：都受 SPA 重载限制，没有魔法。
- 冷启动场景由浏览器/终端启动时间主导，PAD 只省掉自己的 dispatch（几十毫秒）。

**做不到的**
- 官方桌面端（app）没有会话入口，任何方案都只能聚焦+剪贴板；不声称能跳。
- 跨机器/远程跳转不在 v1（loopback 约束）。

### 9.4 测量方法（可复现）

1. 固定环境：隔离 root + 一个 0.2.1-rc.2 实例 + Edge app 窗；关掉无关实例。
2. 采集：PAD 侧 `Stopwatch` 记 `t0/t_dispatch`；消费端插件 `performance.now()` 记 `t_render` 并通过 `/pad/jump/ack` 回传；30 次取 p50/p95；同时记录 `Reload`（`performance.navigation`）。
3. 对照组：同一实例装 `dsh-session-deeplink` 测 url-query；装 `dsh-session-link` 测 `dsh://`；PAD 只做观测不改被测对象。
4. 证据落 `EVIDENCE/D100-jump-bench/`：原始 CSV + 汇总表 + 环境快照（版本、进程、端口、home）。
5. 回归门槛：预算表中的 p95 作为 CI/契约测试上限；超了必须解释或改设计。

> 说明：以上“更好”限定在**可测量项**上（R0 延迟、Reload=0、Footprint=0、plan 可复现、覆盖扩展成本）。不写“全面超越”这种无法证伪的话。

---

## 10. 安全与隐私

1. loopback 硬约束：descriptor/URL/endpoint 非 `127.0.0.1|localhost` 直接 PA134。
2. token 只在内存/命令行参数模板里流转；**不写进描述符、不写进日志**（既有日志脱敏规则复用）。
3. 描述符是本地文件（0600）；`launch-env`/`launch-args` 只对 PAD 启动的面生效，外来壳描述符不能执行命令。
4. gateway-event：Bearer token + 严格 Origin 校验 + seq 单调 + TTL 30s + 单次消费。
5. 不注册 `dsh://`；如需本地协议，只用 `pad-session:` 并只由 PAD 自己的描述符声明。
6. 剪贴板可能被其他程序读取：默认放规范引用（session id 本身不是凭据；不含 token/凭据）。
7. 跳转不提升权限：不改变实例的 sandbox/profile/凭据；只触发 UI 选择。

---

## 11. 落地里程碑

| 里程碑 | 内容 | 验收 | 估算 |
|---|---|---|---|
| **M1** | `SessionRef` + `SurfaceRegistry`（L0/L1/L2 + schema 校验）+ `Planner` + `focus-clipboard` + `pad cli session ref/open/surface list\|validate --json --explain` | plan 可复现；PA130–139 全有单测；`session open` 在无插件时聚焦+剪贴板 | 1 天 |
| **M2** | `url-query` transport + 第一方消费端插件（URL 参数路径）+ 版本矩阵 4 例 | 0.1.0-rc.7 / 0.2.1-rc.2 上 `?session=` 可打开；未知 id 降级不崩 | 1.5 天 |
| **M3** | `gateway-event`（gateway 扩展 `/pad/jump` + CORS + ack）+ 桌面端内存队列 | R0 p95 ≤ 400ms、Reload=0；gateway 0.2.x 兼容 | 1.5–2 天 |
| **M4** | `launch-env`（tui）+ `launch-args` + app 描述符降级 + `rendezvous-file` | tui resume 实测；app 明确 PA139 文案 | 1 天 |
| **M5** | 基准脚本 + EVIDENCE/D100 + 文档（descriptor 作者指南、`docs/README.md` 索引） | 预算表全绿；对照数据落盘 | 0.5–1 天 |

总计 6–7 天（含测试），其中 M1+M2 已能交付“web 面真深链 + 三面降级”的可用形态。

---

## 12. 代码落点

| 职责 | 文件 |
|---|---|
| 身份与规范化 | `Core/Jump/SessionRef.cs` |
| 注册表加载/校验 | `Core/Jump/SurfaceRegistry.cs`、`Core/Jump/SurfaceDescriptor.cs` |
| 规划器 | `Core/Jump/Planner.cs` |
| transport 实现 | `Core/Jump/Transports/{GatewayEvent,UrlQuery,LaunchEnv,LaunchArgs,RendezvousFile,FocusClipboard}.cs` |
| 命令面 | `Core/Cli.cs`：`session list\|ref\|open`、`surface list\|validate` |
| UI 入口 | `Views/ManageView`（“打开对话”）；`Core/NowProbe` 提供会话行 |
| 消费端插件 | `agent-pack-dsh/session-jump/`（`package.json` + `cordis.patch.yml` + `lib/client.js` + 空 `lib/index.mjs`） |
| gateway 扩展 | `agent-pack-dsh/gateway/lib/index.js`（`/pad/jump`、`/pad/jump/ack`、CORS） |
| 基准 | `scripts/bench-jump.ts` + `EVIDENCE/D100-jump-bench/` |

---

## 13. 开放问题（影响 M3/M4）

1. **gateway 的 0.2.x 兼容**：`engines.dsh` 目前 `>=0.1.0-rc.7 <0.2.0`；0.2.x 实例上能否挂载、`host-apiproxy` 导出是否一致，需先验证；不通过则 M3 推迟，M1/M2/M4 不受影响。
2. **`sessions.open()` 的可见范围**：是否只能打开当前会话列表（同 workspace）内的会话；跨 workspace 会话能否被 open。不能则降级为 url-query + 复制 id，并在 UI 标注。
3. **token URL 的复用语义**：实例 ledger 里的 `?token=` 是否长期有效；失效时 planner 必须识别并降级（不能打开 401 页面）。
4. **官方桌面端**（已核实源码，0.2.1-rc.2）：`app.setAsDefaultProtocolClient("dsh")` + `open-url` 只处理 `dsh://open` / `dsh://open/` → `focusPrimaryWindow()`；`argv` 只认 `--updated` → **没有任何外部会话入口**。但 `ready.injections = ctx.webServer.collectIndexInjections()` 证明它会加载 profile 的 client 插件，所以 app 面走 `gateway-event` 可达（前提：gateway 支持 0.2.x + 端点发现，见 M3/M4）。
5. **client runtime `sessions` 服务的长版本稳定性**：0.1.x/0.2.x 已核实；0.2.2-alpha 起需进矩阵，发现缺失自动空转。
6. **`--surface` 强制模式**与 PA138 多窗口歧义的交互文案。

---

## 14. 待记录的决策（建议 D100）

1. 更正 D67：不是“DSH 没有深链”，而是“核心没有、插件层有”；PAD 自建消费端，不依赖第三方插件。
2. 采用「surface 能力注册表 + 固定 transport 原语」，不在核心枚举 surface。
3. 不注册 `dsh://`；不因跳转复活 WebView2 内嵌。
4. 诊断码 PA130–PA139 纳入 PA 表。

---

## 15. 实现进展（2026-11-02）

### 15.1 消费端插件 v0.2 + gateway 跳转通道（M2/M3 代码已落地）

目录：[`agent-pack-dsh/session-jump/`](../session-jump/)、[`agent-pack-dsh/gateway/`](../gateway/)

| 文件 | 内容 |
|---|---|
| `session-jump/package.json` | `dsh.bundle.patch` + `dsh.client`（platform=web, immediately）；v0.2.0 |
| `session-jump/lib/index.mjs` | host 半：订阅 `webserver/index-inject`，读 `<DSH_HOME>/pad-gateway.json`，push `{kind:"global", name:"__PAD_SESSION_JUMP__", value:{url,token,home}}` |
| `session-jump/lib/client.js` | URL 通道 + gateway 通道（长轮询 `/pad/jump` → `sessions.open()` → `/pad/jump/ack`）；地址栏同步保留 token；未知/缺失只发事件不抛错 |
| `session-jump/test/*.test.mjs` | **16 pass**：id 规范化、ready 等待、URL/事件打开、ack、注入行、ad 校验、loopback/desktop origin 门 |
| `gateway/lib/jump.mjs` | 跳转队列 + HTTP 处理器：POST/GET/ack、Bearer、seq、TTL 30s、容量 20、CORS（loopback + `dsh-app://app`） |
| `gateway/test/jump.test.mjs` | **7 pass**：id/origin 校验、队列唤醒、TTL  prune、HTTP 全链路、CORS 预检、长轮询超时 204 |
| `gateway/lib/index.js` | 在既有 carrier 上挂 `handleJump`（`/pad/jump*`），dispose 时清队列；原有 `/pad/ping` 与 `/api` 不变 |

验证（2026-11-02，node v24.21.0）：

```
session-jump: node --test --test-timeout=5000 test/client.test.mjs test/host.test.mjs
  ℹ tests 16  ℹ pass 16  ℹ fail 0
gateway:      node --test --test-timeout=5000 test/jump.test.mjs
  ℹ tests 7   ℹ pass 7   ℹ fail 0
```

尚未做实例内真机 E2E（需把两包装进隔离实例、起 `dsh web`/官方桌面端后跑一次），见 §15.3。

### 15.2 官方桌面端源码核实（0.2.1-rc.2，本机 asar）

| 事实 | 证据（asar 内 `lib/main.js`） |
|---|---|
| 注册 `dsh://` 协议 | `13054` `app.setAsDefaultProtocolClient("dsh")` |
| **只处理 `dsh://open`** | `13055-13058` `open-url` → `url === "dsh://open" \|\| url === "dsh://open/"` → `focusPrimaryWindow()`；其余 URL 忽略 |
| 命令行只认 `--updated` | `12071` `process.argv.includes("--updated")` |
| 会加载 profile 的 client 插件 | `12192-12193` `ready.injections` → `12423-12431` `DESKTOP_IPC.boot` 返回 `injections`；desktop-host 侧 `ready` 消息 = `ctx.webServer.collectIndexInjections()` |

**结论**：官方桌面端**没有外部会话深链**（`dsh://` 只能把窗口唤到前台）；但它会注入 profile 里的第三方 client 模块，所以我们的消费端插件在官方桌面端里**能加载**。要让 app 面可跳，需要 v0.2 的 `gateway-event`：PAD → `pad-gateway` `/pad/jump` → 页面内插件长轮询 → `sessions.open()`。这比官方给深链更可控，也不依赖上游改协议。

### 15.3 下一步（真机 E2E 与 PAD 侧）

1. **gateway 在 0.2.x 实例上的挂载验证**：`engines.dsh` 仍写 `<0.2.0`，需先确认 0.2.1-rc.2 上能启动（apiproxy 导出/依赖解析）。跳转端点本身只依赖 `node:http`，若 apiproxy 挂载失败，可考虑把 jump 通道拆成不依赖 apiproxy 的独立 carrier（设计预留）。
2. **隔离实例 E2E**：装 `pad-gateway` + `pad-session-jump` → 起 `dsh web` → 验证 ① URL `?session=` 打开 ② `POST /pad/jump` 后页面切会话并 ack。
3. **官方桌面端 E2E**：profile 装两包 → 打开官方壳 → `POST /pad/jump` → 渲染器内插件长轮询并切会话（预期会加载注入行；未实测）。
4. M1：PAD 侧 `Core/Jump/*` + `pad cli session open --explain`（把 `POST /pad/jump`、URL 拼接、TUI env、app 降级串起来）。
5. 基准：`EVIDENCE/D100-jump-bench/`（R0/R1 对照第三方插件）。

### 15.4 真机 E2E（2026-11-02，ev-project / DSH 0.2.1-rc.2 / web）

证据：[`EVIDENCE/D100-session-jump-e2e/`](../../EVIDENCE/D100-session-jump-e2e/README.md)

| 项 | 结果 |
|---|---|
| 插件在 0.2.1 加载 | ✅ 需 `dsh.manifestVersion: 1` + 包名一致的 `__ModuleLoader__.load({id})` + `dsh.client.inject` 客户端包列表 |
| host 注入 gateway 广告 | ✅ 页面 `window.__PAD_SESSION_JUMP__ = {url, token}` |
| gateway carrier（jump-only） | ✅ `listening … (jump channel ready)`；`apiproxy unavailable; jump-only mode`（0.2.x 无 npm apiproxy） |
| `POST /pad/jump` → 页面打开 | ✅ `{"ok":true,"seq":1}` → 页面事件 `queued → opened`（opener 调用无异常） |
| URL 通道 `?session=` | ⚠️→✅（复测）：token URL 会被 host redirect 消费，query 丢失但 **fragment 保留**；改用 `#session=<id>` + boot 前 script 捕获 → `opened` |
| 视觉确认会话切换 | ⚠️ 实例停在 onboarding（"请选择模型"），body 未变化；opener 调用成功但无法用 DOM 佐证 |

**0.2.x API 结论**：`sessions.open()` 在 0.2.1 不存在；打开入口是 **`uiWorkspace.openSession(id)`**（通过 `ctx.inject(["uiWorkspace"])` 获取）。`sessions.list.getSnapshot()` 形如 `{ids, byId, phase, projectionsBySession}`（`phase: pending → ready`），没有 `current` 字段。
**URL 通道结论**：0.2.x 的 query 会被 token redirect 丢掉；**fragment 会保留**。host 半注入 `script` 行在 boot 前捕获 `{search,hash}`，client 半优先读 `#session=`；C# 侧同时写 query 与 fragment。运行中实例仍优先走 gateway 事件（R0）。
**gateway 包装结论**：仓库版 `pad-gateway@0.1.0` 在 0.2.x 上会被 engines + `dsh-host-apiproxy` 依赖 + bundle patch 三处挡住；0.2.x 用正式的 jump-only carrier `@sakikotgw/pad-gateway-jump@0.1.0`（`agent-pack-dsh/gateway-jump/`，已 E2E 验证）。

### 15.5 M1 与 0.2.x carrier 落地（2026-11-02）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md)

**PAD 侧（C#）**：`Core/Jump/{JumpModels,JumpPlanner,SessionCli}.cs` + `Cli.cs` 的 `session` / `surface` 命令组。

- `SessionRef.Normalize`：`session-<uuid>` / 裸 uuid / 缓存文件名三形态。
- `SurfaceRegistry`：内置 `pad-web`（gateway-event > url-query > focus-clipboard）、`pad-tui`（launch-env/args）、`pad-app`（focus-clipboard）；另读 `<dataRoot>/library/surfaces/*.json` 与 `<home>/.pad/surfaces/*.json`（坏文件进 rejected，不致命）。
- `JumpPlanner`：确定性排序 R0 1500 > R1 800 > R2 400 > R3 1；输出 `pad.jump-plan/v1`（candidates / rejected / degraded / pa / clipboard）；执行 gateway POST、url 打开、focus+clipboard；PA130–PA139。
- CLI：`pad cli session ref|open`、`pad cli surface list|validate`（`--json` / `--surface` / `--no-exec`）。
- 验证：`dotnet build -o .tmp\pad-jump-out` → 0 警告 0 错误；CLI `session ref` / `surface list` / `session open --no-exec --json` 输出正确；用假 carrier 验证 `session open --json` 真发 `POST /pad/jump`（Bearer + `{sessionId}`，计划 `executed=true, seq 7`）。

**0.2.x carrier**：新增包 `agent-pack-dsh/gateway-jump/`（`@sakikotgw/pad-gateway-jump@0.1.0`）——jump-only loopback carrier，`engines.dsh >=0.1.0-rc.7`、无 apiproxy 依赖、patch 只插自己；`/pad/ping` 返回 `capabilities:{jump:true,api:false}`，`/api` 固定 503。`lib/jump.mjs` 与测试由 `scripts/sync-from-gateway.mjs` 从完整版同步。0.1.x 仍用完整 `pad-gateway`（带 `/api` 代理），按实例 DSH 版本选择（接线待做）。

**仍未做**：TUI launch-env 执行（plan-only）；focus-clipboard 执行实跑；版本→carrier 选择；真机 bin 替换（运行中的 PAD 锁着 exe）。

### 15.6 复测与窗口入口（2026-11-02 晚）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.1–2.3

- **URL 通道复测通过**：`/?token=…#session=session-a367…` → fragment 存活 → `__PAD_SESSION_JUMP_BOOT__={search:"",hash:"#session=…"}` → 插件事件 `opened`。C# `WithSessionParam` 已改为 query + fragment 双写。
- **gateway 通道复测通过**（正式 `pad-gateway-jump` 包，非临时变体）：`POST /pad/jump` → `{"ok":true,"seq":1}` → 页面 `queued → opened`。
- **窗口入口已接**：`ManageView` / `InstanceView` 会话行「打开」改为 `JumpPlanner.ExecuteAsync`；不可执行时退回 transcript reveal。构建 0 警告 0 错误。
- 测试计数：session-jump **19**（+fragment）、gateway 7、gateway-jump 7 = **33**。
- 复测后现场已恢复：测试插件移除、instance 重启（3080）、广告与日志删除。

### 15.7 carrier 选择与 TUI 执行（2026-11-02 深夜）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.4

- **版本→carrier 选择已接线**：`Cli.GatewaySpecFor(release)`（`0.2.*` → `@sakikotgw/pad-gateway-jump`，其余 → `@sakikotgw/pad-gateway`；`PAD_GATEWAY_SPEC` 仍可覆盖）。`Cli.Recipe(kind, release)` 的 `tui` / `gateway` 安装都用它。`pad-gateway-jump` 已于 2026-11-01T20:25Z 发布 npm（见 §15.8），真机安装不再需要 fixture。
- **TUI 执行已实现**：`Nodes.OpenTerminal` 新增 `extraEnv`；`JumpPlanner` 的 `launch-env` 分支真的启动 TUI（`DSH_TUI_RESUME_SESSION=<id>`），返回 `executed=true` + pid。`launch-args` 仍 plan-only。
- **构建**：`dotnet build` → 0 警告 0 错误。
- **真机 bin 已部署**：停 PAD（PID 50340）→ 重建 → 重启新 PAD（PID 62524）；真机 CLI 验证 `session ref` / `surface list` / `session open --no-exec --json` 正常。顺手修了 `launch-env` 与 `launch-args` 同分时选中 plan-only 的排序 bug（launch-env 420 > launch-args 400），现在默认选可执行的 `launch-env`。
- 未实跑：TUI 启动（会新开真实终端）、focus-clipboard 执行、窗口内点击。

### 15.8 发布、TUI 实跑与点击现场（2026-11-02 04:50）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.5

- **npm 发布**：`@sakikotgw/pad-gateway-jump@0.1.0` 已发布（2026-11-01T20:25:52Z，public access）。本地 `npm view` 曾有 CDN 滞后 404，`--prefer-online` 后可见；此后 `PAD_GATEWAY_SPEC` 不再是 0.2.x 装包的前置条件。
- **TUI launch-env 实跑通过**：给 ev-project 装 `tui` profile（dsh-tui + gateway-jump）→ `pad cli session open ev-project session-a367… --surface pad-tui --json`：
  - `chosen pad-tui/launch-env R2 420 eligible`，`executed=true`；
  - 实际拉起 `cmd.exe 63800 → node.exe 65876 bin.js --profile tui`，detail 为 `started tui with DSH_TUI_RESUME_SESSION=… (pid 63800)`；测试后已 kill。
  - 未肉眼确认 TUI 内已恢复该会话。
- **窗口点击现场已备好**：ev-project（web profile 装 `pad-session-jump`）以 PAD 管理形态跑在 49998（ledger 有记录）；URL fragment 实测页面事件 `opened`；CLI 计划 `chosen pad-web/url-query R1 eligible`（gateway 因无 ad 被 PA136）。点击位置：PAD → 实例 ev-project → 版本设置/会话列表 →「打开」；管理页“正在聊的”在 0.2.x 不可用（gateway-jump 不代理 `/api`）。

### 15.9 0.2.x「管理」页打通 + focus-clipboard 实跑（2026-11-02 05:10）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.6–2.7

- **管理页不再依赖 apiproxy**：0.2.x 的 web 监听器本身就有同一套 `client-request` 协议，只是路径改斜杠（`/api/session/list`）、envelope method 改斜杠、payload 包成 `{args}`（`session/list` 要 `_request`，`session/cancel` 要 `request`）。
- **接入方式**：`gateway-jump@0.1.1` 从 host `webServer.getOrigin()` 学到 web 地址写进广告 `webUrl` + `apiStyle:"dsh-0.2"`；PAD 检测到 `webUrl` 后走 `CallWebApi`（斜杠路径 + args 包装 + 方法适配）。
- **鉴权**：`/api` 用 HttpOnly cookie；PAD 不读浏览器 jar，而是用 `<home>/.credentials.yaml` 的 `client-connection/browser-session` 密钥自签 `dsh-auth-<sha256(authority)>` cookie（`v1.<body>.<hmac>`）。算法用真实浏览器 cookie 交叉验证一致；`curl` + 自签 cookie 调 `/api/session/list` 返回 **200**；部署后的 PAD `pad cli gateway ev-project session.list` 输出完整 items。
- **workspace 缺失**：0.2.x 没有 `workspace.list`，但注册表在 `<home>/storages/workspace.json`（unit v2 `tables.workspaces`）；PAD modern 模式直接读它，`Tree` 恢复 workspace 分组（实测 `ws-backend` / `ws-frontend`，session 归到 ws-frontend），文件缺失才退回单组「会话」。
- **取消会话**：新增 `pad cli gateway <instance> cancel <sessionId>`；`/api/session/cancel` args `{request:{sessionId}}`。不存在的 id → PA031 `session/not-found`；真实 idle session → `{"accepted":true}`；运行中 turn 的真实中断见 §2.9。
- **focus-clipboard 实跑**：`--surface pad-app` → `executed=true, degraded=true, pa=PA138`（10 个窗口同标题 “DeepSeek Harness” → 歧义），剪贴板正确写入 session id；符合“歧义只提示、不猜”的设计。
- **Tree/诊断入口**：`pad cli gateway <instance> tree|sessions|cancel <id>` 复用 ManageView 的 `Gateway.Tree` 路径，便于真机核对。
- **真 turn 取消（E2E）**：临时给实例补 raw key 后发一个 `Start-Sleep 300` 的 prompt → `running:true` → PAD cancel `{"accepted":true}` → 4s 后 `running:false`、页面“已停止”、shell 子进程消失；测试后凭据已还原。
- **launch-args**：`Nodes.OpenTerminal` 新增 `extraArgs`，`launch-args` 可执行（`--resume <id>`）。真机用临时 `launch-args` surface 验证进程命令行为 `bin.js --profile tui --resume session-a367…`。
- 当前现场：PAD PID **52940**；ev-project 跑在 49998（广告含 `webUrl`，pid 61824，ledger 已同步）；`@sakikotgw/pad-gateway-jump@0.1.1` 已发布且 npm 可见。

### 15.10 管理页 UI 点击 E2E + key 修复（2026-11-02 09:0x）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.10

- **管理页真点**：PAD 管理页选中 ev-project → `在跑`、`管理口已连上。`、树 `2 个 workspace · 1 条 session`（ws-backend / ws-frontend）；点会话「打开」→ 提示 **`posted to pad-gateway (seq 1)`**。
- **顺带修 4 个 PAD bug**：
  1. `ManageView.OpenSession_Click` 直接写 `TreeHint.Text`，running 态下提示块是 Collapsed → 点击成功也看不到反馈；改走 `Hint()`。
  2. `Gateway.LiveConfigured` 只认 0.1 的 `{credentials:{…}}` 包层 → 0.2.x 平铺结构被误判 PA116；已兼容两种形状。
  3. 0.2.x 读 `.credentials.yaml` 是字面量，PAD 的 `REF: <key>` 标记会进 key（非法 header 字符）；`Runner`/`CredentialsGate` 分发到 home 时对 0.2.x 写 raw key。
  4. `Launcher.Runtime()` 在 launcher 退出、harness 仍活时用 advert pid / `FindDshPid` 找回 pid，管理页不再误判“已停”。
- **key 现场**：raw key + `credentials/describe` = `{configured:true, source:"file"}`，管理页 PA116 消失；当前 PAD PID 60380，实例手动跑在 3080（pid 49792，runtime.json 已补记录）。
- **TUI 视觉确认 ✅**：真实控制台里 `node bin.js --profile tui --resume session-a367…`；首次被残留 TUI 进程占锁（截图 `tui-resume-locked.png`，报 “mounted by another TUI terminal”），kill 后成功（`tui-resume-success.png`：会话标题「执行等待三百秒命令」、ws-frontend、轨迹、「已打断」输入框）。PAD 自己生成的 cmd 窗口在该环境不可枚举，故用同参数直连 cmd 取证。
- **测试实例清理**：停 web、移除 session-jump/gateway-jump（bundles 回 base+web-app）、删 `profiles/tui`、网关 ad/log、`.pad/surfaces`、`.pad/nodes.json`、备份与临时文件；清空 runtime.json 后重启 PAD（PID 4400）。验证：3080 无监听、无 ad、只剩 `web` profile、管理页 ev-project「还没启动」。保留 raw key 的 `.credentials.yaml`。
- 顺序验证：`launch-args` 走 PAD 时 TUI 也是可用的；直连截图证明 `--resume` 的语义正确。

### 15.11 M1 验收补齐（2026-11-02 13:5x）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.12

- **`--explain` 落地**：人类可读（chosen/candidates 标记/rejected PA 码原因/degraded）与 `--explain --json`（稳定 plan schema）。
- **单测**：`agent-pack-dsh/pad.tests`（xUnit）**23 pass**，覆盖 SessionRef、注册表分层合并/kind 冲突/坏 schema、PA130–139、cookie 签名、`StripRefPrefixes`/`HarnessReadsRaw`。运行：`dotnet test agent-pack-dsh/pad.tests/Pad.Tests.csproj`（Pad 构建到 `pad.tests/pad-bin\`，PAD 运行时也能跑）。
- **注册表 L0/L1/L2 合并语义**：label/kind 高层优先；transports 按 `type` 合并（覆盖/追加）；kind 不一致标冲突；`surface validate` 输出 `merge:` notes。
- **GUI 启动链路验收**：PAD 启动卡启动 ev-project → 脚本复制 `.pad/credentials.raw.yaml`（raw，无 REF）→ home `.credentials.yaml` 值也是 raw；全文件 `REF:` 出现 0 次。顺手修了 `StripRefPrefixes` 的顶格行正则 bug。
- 测试工程注意：Pad 是 WPF WinExe，测试用“先 MSBuild Pad 再引用 DLL”的方式，避免 ProjectReference 触发 XAML 临时工程丢字段。
- 现场：PAD 重启后干净运行（runtime 清空、实例停止、3080 空闲）；`STATE.md` 已更新到 2026-11-02。

### 15.12 M2 收尾 + 官方桌面端 staging（2026-11-02 14:2x）

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.13–2.14、[`EVIDENCE/D101-desktop-e2e/`](../../EVIDENCE/D101-desktop-e2e/README.md)

- **发布**：`@sakikotgw/pad-session-jump@0.2.1`、`@sakikotgw/pad-gateway-jump@0.1.1` 均已在 npm（CDN GET 有滞后，`npm view --prefer-online` 可见）。
- **安装链路**：`Cli.Recipe("web", release)` 自动带 `@sakikotgw/pad-session-jump`；实测从 npm 安装成功，单测覆盖（28 pass）。
- **版本矩阵**：0.2.1 用 `#session=`（fragment）→ opened，未知 → unknown 不崩；0.1.0 用 `?session=`（query）→ opened/already，未知 → unknown 不崩。0.1 合成 profile 有壳层 “Loading plugins…” 环境问题（缺 `dsh-client-ui-primitives` client entry），插件/opener 语义已用事件证明。
- **基础设施修复**：`LaunchPolicy.PatchVirtualStoreDir` 兼容 pnpm 11 的 JSON `.modules.yaml`（此前会把 YAML 行追加到 JSON 后导致 plugin add 全挂），3 条单测覆盖。
- **官方桌面端**：实测官方壳严格单实例（带 `--user-data-dir` 启动 exit 9；现有进程的 user-data-dir 硬编码
  `%APPDATA%\@deepseek-ai\dsh-desktop`），不重启无法加载新插件；而本会话宿主就是该壳，故不能由 agent 自己重启。
  已回滚预置改动（桌面 profile 保持原样），保留可重复脚本 `EVIDENCE/D101-desktop-e2e/stage-desktop-e2e.ps1`
  （staging/`-Rollback`）和 `run-after-restart.ps1`（等 ad → ping → POST /pad/jump）。想跑时三步：
  staging → 重启官方壳 → 运行跳转脚本。

### 15.13 官方桌面端 E2E：复制实例法（2026-11-02 16:2x）✅

证据：[`EVIDENCE/D101-jump-m1/`](../../EVIDENCE/D101-jump-m1/README.md) §2.15、[`EVIDENCE/D101-desktop-e2e/`](../../EVIDENCE/D101-desktop-e2e/README.md)

- 绕开单实例：复制官方 app（1.09GB）→ 改 asar 内 `package.json#name` 为 `@deepseek-ai/dsh-desktop-padtest`
  （独立 Electron userData）→ 独立 `DSH_HOME` 启动副本，`--remote-debugging-port=9223`。
- 副本 shell 里文件级装入两个插件并重启：carrier ad 出现（`capabilities.jump=true`，webUrl 64885），
  渲染器注入生效（`__PAD_SESSION_JUMP_DEBUG__` ready/hasOpener）。
- `POST /pad/jump {sessionId}` → `200 {"ok":true,"seq":1}`；渲染器事件 `queued → opened`。
- 官方二进制只改了 app.name；当前桌面端与 `E:\DSH-Home` 未被改动。证据（日志/截图/CDP helper）
  在 `EVIDENCE/D101-desktop-e2e/`。
