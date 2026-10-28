# 形态 Surface

date: 2026-10-22
status: draft

## 一句话

**Profile 决定"装什么"，Surface 决定"怎么看见"。两者正交。**

现在 PAD 把它们混在 `Recipe(kind)` 里：`"tui"` 既表示装 `dsh-tui`，也表示开终端。
`TerminalKind` 更进一步假设所有启动都进终端。这就是加 web / 桌面端时会别扭的原因。

## 三种形态

| 形态 | 名字 | 怎么显示 | 依赖 | 能同时开几个 |
|---|---|---|---|---|
| 终端 | `tui` | Windows Terminal / conhost / 自定义 | `dsh-tui` + gateway | 多开，一个实例一个窗口 |
| 浏览器 | `web` | 浏览器窗口（推荐 Edge `--app=`） | `dsh-web-app` | 多开，一个实例一个端口 |
| 桌面壳 | `app` | DSH Desktop 窗口 | 官方 Electron 壳 | **只有一个** |

最后一列是形态的本质差别，不是实现细节：

- TUI 和 web 都是**无状态展示**——窗口关了，进程还在；再开一个窗口接同一个端点
- 桌面壳是**有状态容器**——它自己持有 `lockfile`（独占）和固定端口 43129

所以 `Exclusive` 必须是形态的属性，不能靠调用方记着。

## 形态的四个契约

一个形态要实现四件事，缺一件就会在某个地方漏出分支：

1. **Recipe** — 这个形态需要装哪些包
2. **Launch** — 怎么把实例拉起来（终端 / 浏览器 / 壳）
3. **Endpoint** — 端点从哪来（tui 没有；web 读 announce；app 固定）
4. **Stop** — 怎么停（杀进程 / 关窗口 / 交给壳自己）

## 和整合包的关系

整合包是**内容投影**（skill / MCP / rule / command），不是形态。

- 换形态不影响装了什么
- 换整合包不影响窗口长什么样

所以兼容性天然成立：只要该 profile 里有 `pack-agent` 插件，投影就跟着走，与 `tui` / `web` / `app` 无关。

需要验证的一点：web profile 里装 `pack-agent` 时，投影是否和 TUI 一样生效（PRODUCT 已说明"投影不进插件名单"，说明机制在，但要实测）。

## 数据模型

```
Instance          一份 DSH_HOME + 一个版本 + 一个工作区
  ├── Profile     装什么（bundle set）      → 决定能力
  ├── Surface     怎么显示（tui/web/app）   → 决定窗口
  └── Modpack     注入什么内容（投影）      → 决定素材
```

三轴正交。任意两轴组合都合法，除了 `Surface.Exclusive` 限制 `app` 只能有一个实例在用。

## 未决

1. `web` 形态用 Edge `--app=` 独立窗口，还是 PAD 内嵌 WebView2（后者违背 PRODUCT「不把 DSH 装进自己窗口」）
2. `dsh web` 是自己 announce 端口还是要 `--port`——决定 `Endpoint` 契约怎么实现
3. 桌面壳未来若支持 `--user-data-dir`，`Exclusive` 才可能放开

## 官方契约（据 `apps/cli/reference`）

上面第 2 条已由官方文档回答，**先前的判断要改**：

| 事实 | 出处 |
|---|---|
| `dsh --profile web --port 8080` 合法，`--port` 是 web app 的参数 | App arguments 表 |
| `--host` / `--no-open` / `--tls-cert` / `--tls-key` / `--public-url` / `--trusted-host` 同理 | 同上 |
| web 默认端口 **3080**，不是 43129 | Web profile 一节 |
| `web` profile 首次使用会从官方模板自动初始化（base + web-app） | Profile boot |
| `--from-default-profile <template>` 可把 profile 重置成官方模板的全新副本 | 同上 |
| 官方模板只有五个：`web` / `headless` / `sdk` / `sdk-minimal` / `acp` | 同上 |
| `desktop` 是**保留的 profile 名**，dump 模式会拒绝 | Config schema dump |
| `--dump-default-config` 只打 bundle 层；`--dump-config` 再加 profile / home / `--patch` | Profile boot |

**结论修正：多开 web 实例没有端口冲突问题。** 之前那句“端口固定、多开大概率撞”说的是 Electron 桌面壳自己的 43129，不是 `dsh web`。`Endpoint` 契约因此变成最简形式：

```
PAD 分配端口 → dsh --profile <p> --port <n> --no-open → 窗口打开 http://127.0.0.1:<n>
```

不需要解析 announce，不需要探测。`--no-open` 保证浏览器不会被 DSH 抢着打开，窗口归 PAD 管。

**另一条修正：PAD 不该手搓 profile。** 官方模板已经覆盖 web / headless / sdk 三种形态，`--from-default-profile` 能拿到一份干净副本。PAD 要做的是「选模板 + 往里 `plugin add`」，不是自己拼 bundle 列表。

## 实测：web 形态的启动行（2026-10-22）

空 home 起 `--profile web --port 34871 --no-open`，25 秒后就绪：

```
dsh web: http://127.0.0.1:34871/?token=msClhwGfH8SrwWQ9P-qrgtPPMcbjgJqP7ft28-7vuVw
```

**关键：URL 带一次性 token。** 所以 `Endpoint` 契约不是「端口拼 URL」这么简单：

- DSH 每次启动生成一个 token，只出现在这一行 stdout 里
- PAD 必须**读子进程 stdout，逐行匹配 `dsh web: ` 前缀**，拿到完整 URL
- 只拼 `http://127.0.0.1:<port>` 会被拒

判据也随之确定：**等这一行出现**，而不是探测端口——端口先 listen、行后打印，探测会在最坏时刻返回假成功。

其余实测确认：`--no-open` 生效（没有试图开浏览器）；空的 `DSH_HOME` 能离线自动初始化出 `profiles/` + `storages/`；噪音只有一条 `DEP0180` 弃用警告。


