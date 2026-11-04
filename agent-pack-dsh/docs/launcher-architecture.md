# PAD 架构（精简版）

2026-11-04 · 状态：活文档 · 完整历史版在 `archive/launcher-architecture-full-2026-11.md`
关联：`PRODUCT.md`（产品红线）、`frontend-spec.md`（UI 法）、`dev-mode.md`（开发模式）

> 只记"现在是什么、边界在哪"。详细的历史推演、PCL 对照、逐条数据契约都在归档版里。

---

## 1. 形态

- **一个 Windows 桌面程序**（.NET 9 / WPF），`agent-pack-dsh/pad/Pad.csproj`，单程序集。
- 启动方式：窗口（默认）、`pad cli <group>`（无头命令行，退出码可判）、`pad cli mcp`（stdio MCP 服务）。
- 无网络后端：所有能力在进程内，状态全在磁盘（launcher root）。CLI、窗口、MCP 共用同一份 Core 代码。

## 2. 磁盘布局（launcher root）

```
<PACK_LAUNCHER_ROOT>/
  pad.json                  # 设置（含 download.shelves、launch、registry 等）
  runtime.json              # 运行账本：实例/profile/pid/port/startedAt
  instances/<id>/
    instance.json           # 实例声明（id/name/dsh.version/home/workspace/status）
    home/                   # 该实例的 DSH_HOME
      profiles/<p>/         # package.json(dsh.profile.bundles) + cordis.patch.yml + node_modules
    logs/                   # 运行日志
    checkpoints/<ts>/       # 声明层快照（回退用；不自动删）
    dev/                    # 开发会话：session.json / history.jsonl / verdicts/
  versions/<ver>/           # 内核发行号（version.json + node_modules/@deepseek-ai/dsh）
  library/                  # credentials / icons / plugins-meta 缓存
```

## 3. 领域模型

| 对象 | 含义 |
|---|---|
| 发行号（DshRelease） | 一份 DSH 内核版本，实例通过 `instance.dsh.version` 钉住 |
| 实例（Instance） | 一个隔离 `DSH_HOME`，对应一份 `.minecraft` |
| profile | `home/profiles/<p>`，可启动单元；`dsh.profile.bundles` 决定装哪些 bundle |
| bundle | profile 的一层；官方插件声明 `dsh.bundle`，第三方包可能只是 plain dependency |
| 整合包（pack） | 一份装配清单（`.dspack/.pack/.pinst`），只投影、不参与 bundle 解析 |
| 开发会话（DevSession） | 一次试验：`running → committed / discarded`，改动先落 overlay，固化才写 profile |
| 存档点（Checkpoint） | 变更前的最小声明层快照；`restore` 可回退 |

## 4. 运行与就绪

- 启动：拼 env（`DSH_HOME`、profile、`--patch` 覆盖层）→ 运行 `node <bin.js> --profile <p>` → 解析就绪 → 按这个 bundle 的类型开窗口（`dsh-web-app` → 浏览器，`dsh-tui` → 终端）。
- 运行账本 `runtime.json` 记录 pid/port/startedAt；停止要收敛进程树（含 node 子进程）。
- 端口从 3080 起避让；单实例锁、托盘、自更新是既有能力（D93/D95/D99）。

## 5. 命令面（`pad cli`）

```
release    list | install <ver> | remove <ver> | pin <bin.js>
instance   list | create | pin | clone | remove | workspace
profile    list | add
plugin     list | status | verify | undo | add|remove|update [--hot]
run        <instance> [profile] [--dev]
stop | ps | script | rewrite-scripts
trial      list | add | commit | drop
dev        start|status|apply|verify|commit|discard|list    # 开发会话（AI-first，--json）
checkpoint list | prune
mcp        serve
pack       list | project | allow | deny
market     plugin|tui|skill|host
session    ref | open
queue | task | inspect | crash | logs | gateway | adopt | sniff | ...
```

约定：`--json` 时 stdout 只有 JSON；错误用 `PAxxx`（`modpack/registries/pa-codes.json`）；退出码 0 成功 / 2 需重启或验证不通过 / 3 环境异常。

## 6. 验证与回滚

- **D110 热重载**：默认冷（写盘 + restart-required），`--hot` 走官方 `dsh-hmr`；热路径必须过 V1–V4（dump-config / bundle-row / tools / pid），失败自动 restore 存档点。
- **D111 开发会话**：trial overlay 只影响本次运行；`dev verify` 判据 `config-compiles / plugins-resolve / source-untouched`（boots/stays-up/tools 待 P1）；`commit` 先打点再写 profile；`discard` 清 overlay 与依赖。
- 存档点**不自动删除**：清理只走显式 `checkpoint prune`（默认 dry-run）。

## 7. 窗口面

- 概览 / 版本（实例与 profile）/ 插件页（热状态、开发会话条）/ 商店（npm 搜索 + 发行号）/ 设置 / 任务队列。
- UI 规则以 `frontend-spec.md` 为准：能删就删、技术细节折叠、状态用药丸/颜色、同类统一、禁止文字作图。

## 8. 已知耦合（待解耦，不是现状的美化）

| 现象 | 数量/位置 |
|---|---|
| 单程序集、无模块边界 | `Pad.csproj` 一个 |
| Views 直接调 Core | 213 处（InstanceView 63 / VersionsView 36 / DownloadView 27） |
| Core 反向引用 Views | 5 个文件（AppState / PortableBootstrap / RunFacts / ShellActivate / UiState） |
| Core 依赖 WPF | 96 个文件中 11 个 |
| Core 依赖全局单例 | 4 个文件用 `AppState.Current` |
| 同一动作多套流程 | 窗口裸调 `dsh plugin add`；CLI 走 `HotReload` / `DevOps` |

方向：Core 立"动作服务面"（DTO/JSON），窗口/CLI/MCP 都只调服务面；先斩断 Core→Views 的 5 个反向引用；UI 只渲染不判规则。

## 9. 活文档指针

- 产品定位与红线 → `PRODUCT.md`
- UI 规范 → `frontend-spec.md`（+ `frontend-spec/index.html` 手册）
- 开发模式 / 复现包 → `dev-mode.md`
- 决策历史 → 仓库根 `DECISIONS.md`（只追加，不当规范）
- 证据 → `EVIDENCE/`

## 10. DSH 的写法（PAD 要抄的部分）

DSH 由 305 个 `@deepseek-ai/*` 包组成，规律只有六条：

1. **一个能力 = 一个包**：`dsh-session`（服务）、`dsh-session-persistence-jsonl`（适配器）、`dsh-session-query-sqlite`、`dsh-tool-fs`、`dsh-client-ui-*`、`dsh-api-*-controller`。
2. **app 只做组合，不写实现**：`dsh-base` 的 `index.js` 只有 `export {}`，全部内容是 `cordis.patch.yml` 里的行（`id / name / disabled / config`）；CLI 只负责按 profile 加载这些插件。
3. **一切都是 plugin**：默认导出 `class extends Service`（或 `apply(ctx, config)`），通过 `ctx` 拿一切。
4. **依赖用接口声明**：`ctx.inject(['sessionPersistence'], cb)` 依赖可用才激活；`peerDependencies` 就是接口契约集合；`declare module '@deepseek-ai/cordis' { interface Context { sessions: SessionStore } }` 做类型层扩展。
5. **生命周期交给容器**：`ctx.effect(fn)` 注册 disposer，插件卸载/热替换自动清理；服务构造即注册，随 fiber 一起消失。
6. **协议层单独出包**：`dsh-api-session-controller` 导出 `.`（host）、`./client`、`./typert`（RPC schema），UI 与宿主只通过类型化协议通信。

### 10.1 对应到 PAD 的最小版本

| DSH | PAD |
|---|---|
| `Context`（服务容器） | `PadContext`：`Provide<T>` / `Require<T>` |
| `Service` + `ctx.provide` | 服务类实现接口，模块 `Apply(ctx)` 时注册 |
| `ctx.inject(deps, cb)` | 模块/构造函数里 `ctx.Require<T>()` |
| `ctx.effect` | `ctx.Effect(() => …)` 返回 `IDisposable` |
| `ctx.on / emit` | `IPadEvents`（同一容器） |
| `declare module Context` | C# 接口即契约（无声明合并，显式注册） |
| bundle patch | `pad.json` 的模块清单 |
| typert RPC schema | `pad cli --json` DTO + MCP tool schema |

三件套就够：**接口（interface）+ 容器（PadContext）+ 模块（IPadModule）**。组合根在 `AppHost.Boot()`；窗口、CLI、MCP 都只从容器取接口。

迁移顺序（每步可编译、可测）：
1. `PadContext` + 4 个接口：`ILaunchService`（启动/停止/账本）、`ICheckpointService`、`IDevSessionService`、`IStoreService`。
2. 启动/停止先搬家（`Runner`/`Launcher` 收进实现类）。
3. 存档点 → 开发会话 → 商店。
4. 两条禁令：Core 不 `using System.Windows`（渲染放 `Pad.Rendering` 模块）；不用 `AppState.Current`（用 `ISettingsService` 注入）。
### 10.2 PCL 是怎么写的（对照 `E:\PCL\src` 2.13.1.1）

| 维度 | PCL 的做法 |
|---|---|
| 工程 | 4 个项目：主 VB.NET WPF 单体 + `PCLCS`(C#) + `MeloongCore.Shared` / `.Wpf`(C# 共享核心) |
| 业务 | `Modules/` 25 个 `Public Module`（static）：Base / Minecraft / Resource / ThirdParty；最大 `ModMinecraft.vb` 2529 行、`ModLaunch.vb` 2229 行 |
| 界面 | `Pages/` 46 个 code-behind + 43 个 XAML；`Controls/` 34 + 17 |
| 依赖 | 没有接口/容器/DI；页面 code-behind 直接调模块函数 |
| 状态 | 全局状态放模块里（`ModMain` 提示队列、设置），`ModEvent` 自定义事件兜 XAML |
| 测试 | 无 |

**结论：抄它的组织，不抄它的连接方式。**

- 要抄：按域分模块目录、统一命名、页面只做呈现、共享核心单独成库。
- 不抄：static 全局状态、页面直调模块、没有接口——这正是 PAD 现在 213 处 UI 直调 Core、Core 反向引用 Views 的成因。
- 也不做 DSH 的 305 包插件系统：PAD 单端、单团队、没有第三方插件运行时需求，不划算。

目标形态 = **PCL 的目录组织 + 4 个接口 + 1 个容器**：

```
pad/
  Host/         PadContext.cs / AppHost.cs（组合根）
  Contracts/    ILaunchService / ICheckpointService / IDevSessionService / IStoreService / ISettingsService
  Modules/      Launch / Plugins / Dev / Checkpoints / Store / Settings
  Rendering/    唯一允许 using System.Windows 的非视图目录
  Views/ Pages / Controls
```