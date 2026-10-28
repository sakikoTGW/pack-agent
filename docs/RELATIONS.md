# 关系模型

> 这份文档只回答一个问题：**谁属于谁。** 不回答界面长什么样、也不回答怎么操作。
> 所有数字都由 `pad cli relations` 生成，**不要在本文档里手写数量**（见第 7 节）。

---

## 1. 一句话结构

```
Workspace（目录）  1 ──── n  Instance（一份 DSH_HOME）  1 ──── n  Agent（一条会话）
                                                                  Agent  1 ── n  Agent（子）
```

三层实体，三条关系，外加一条只在 Agent 内部的关系。

---

## 2. 实体

### 2.1 Workspace

| 项 | 值 |
|---|---|
| 定义 | 一个目录。PAD 面向项目，所以**目录就是项目** |
| 唯一键 | 规范化路径（小写、去掉尾部斜杠） |
| 字段 | `path`、`label`（末级目录名；若末级是 `home`/`workspace`/`harness`/`dsh-home` 则取上一级） |
| 来源 | `instances/<id>/instance.json` → `workspace.path` |

### 2.2 Instance

| 项 | 值 |
|---|---|
| 定义 | PAD 管理的一份 DSH 安装 + 一份 home + 一个工作区 |
| 唯一键 | `instance.id`（目录名） |
| 字段 | `id`、`name`、`home`、`workspace`、`dsh.version`、`display.logo` |
| 派生 | **指纹** = hash(home · workspace · version · profile 声明的 bundles) |
| 来源 | `instances/<id>/instance.json` |

### 2.3 Agent

| 项 | 值 |
|---|---|
| 定义 | DSH 里的一条会话。DSH 没有单独的"agent"实体，**会话即 agent** |
| 唯一键 | `session id`（缓存文件名去掉 `session-` 前缀） |
| 字段 | `title`、子 agent 目录、最后活动时间、深度（0 = 主 agent） |
| 来源 | `<home>/storages/session_projcache/sessions/session-<id>.json` |

**不截断。** 早先只读最近 5 条会话，导致"agent 数"是个近似值；现在按目录全量读取，
一次读盘换来准确的计数，值得。

---

## 3. 关系

| # | 关系 | 基数 | 判定依据 | 说明 |
|---|---|---|---|---|
| R1 | Workspace — Instance | 1 : n | `instance.workspace.path` | 一个目录可以被多个实例使用 |
| R2 | Instance — Agent | 1 : n | 会话缓存在 `instance.home` 下 | agent 不跨实例 |
| R3 | Agent — Agent | 1 : n | `subagentCatalog.head.values[].childId` | 子 agent 的会话属于**同一个** instance |
| R4 | Instance — Workspace | **n : 1** | 同上 R1 | 一个实例只有一个工作区，不共享 |

R1 和 R4 是同一件事的两个方向，写两遍是为了强调：**多对多不存在**。
这里不需要关系表——实例不会同时属于两个目录。

---

## 4. 耦合点（为什么要多种视图）

关系本身是树，耦合来自两条：

**C1 · 共用工作区**
多个 Instance 指向同一个 Workspace。它们**同目录、不同源**——指纹不同，各自的 home、版本、会话都是独立的。
它不是错误：一个项目上同时开 web 和 tui 是正常用法。

**C2 · 父子会话**
同一个 Instance 里，Agent 之间可能互为父子。所以 Agent 层不是平铺的，而是森林。

> R3 限定了子 agent 与父 agent **同属一个 instance**，所以父子关系不会跨层。
> 这是模型里最容易破坏的假设：若哪天允许跨实例派生子任务，这里要重写。

**C3 · 子会话可能没有缓存**
`subagentCatalog` 里登记了 `childId`，但对应的会话缓存可能还没落盘（或已被清理）。
这时仍然把它画出来，时间标为「未落盘」——**"它在"本身就是信息**。

---

## 5. 不变量

1. 一个 Instance 只属于一个 Workspace。
2. 一个 Agent 只属于一个 Instance。
3. 子 Agent 与父 Agent 属于同一个 Instance。
4. 两个 Instance 的指纹相同 ⟹ 它们是同一个 DSH（`home` 相同）。
5. 两个 Instance 的 Workspace 相同 ⇏ 它们是同一个 DSH。

第 5 条是本机真实踩到的坑：`pad-op-clone` 与 `pad-op-tmp` 共用 `pad-op-tmp/workspace`，
但指纹不同——**同目录，不同源**。

---

## 6. 视图分工

一张图不能同时回答"谁属于谁"和"谁和谁冲突"，所以分开：

| 视图 | 回答的问题 | 画法 |
|---|---|---|
| **关系图** | 谁属于谁 | 自上而下一层一带，连线即归属；颜色=活动热度，呼吸灯=在跑，虚线卡=登记未落盘，底边条=上下文 token；路径、指纹、规模这类长事实进 hover 提示，图面不加字 |
| **占比图** | 谁占的上下文大 | icicle：宽 = 会话上下文 token，颜色 = 热度，大段在左；没有数据就均分。点一段聚焦 |
| **列表** | 有哪些、什么状态 | 按 Workspace 分组，组内每行一个 Instance，附指纹 |
| **卡片页** | 我最先要看什么 | 自排方块卡，聚合数字 + 形状 |

关系图自上而下分层，一行一带；每带行首有层级名。
父子关系（R3）不换布局，用**卡片深浅 + 连线虚实**表达（子 agent 细边，未落盘的画虚线）。
会话规模三事实（surfaceTokens / contextWindow / archived 压缩数）读会话缓存时顺手取，
与 `pad cli inspect` 同源（见 §9）。搜索框在标签行右端：命中 + 祖先保持、其余淡化、计数贴框。

---

## 7. 实测（机器输出，勿手改）

命令：`pad cli relations`（加 `--md` 输出 markdown）

```
3 工作区 · 4 实例 · 11 agent · 1 子 agent

123/workspace   (1 实例)   e:\pack-agent\agent-pack-dsh\pad\bin\debug\net9.0-windows\.pack-launcher\instances\123\workspace
  123              a26e12d1  idle     0 agent / 0 子

dsh/workspace   (1 实例)   e:\pack-agent\agent-pack-dsh\pad\bin\debug\net9.0-windows\.pack-launcher\instances\dsh\workspace
  本机 DSH           29fc4b86  idle     11 agent / 1 子
    · DSH 使用中注意力分散问题   刚刚
      └ 修 PAD 三个待验缺陷   未落盘
    · 未命名会话   21 小时前
    · 安装 DSH 浏览器和侧边栏插件   2 天前
    · 哪个环境有Jupyter   2 天前
    · 给自己安装 dsh-context 插件   3 天前
    · 未命名会话   6 天前
    · DDLK 批处理学习任务方案   6 天前
    · 未命名会话   6 天前
    · 未命名会话   6 天前
    · Side: New thread   6 天前

pad-op-tmp/workspace   (2 实例)   e:\pack-agent\agent-pack-dsh\pad\bin\debug\net9.0-windows\.pack-launcher\instances\pad-op-tmp\workspace
  pad-op-clone     65613b1e  idle     0 agent / 0 子
  pad-op-tmp       dba49290  idle     0 agent / 0 子
```

---

## 8. 缺口与未决

| 缺口 | 现状 | 影响 |
|---|---|---|
| 子 agent 若不在缓存里 | 只画登记项，时间显示「未落盘」 | 没有子 agent 自己的会话内容 |
| **Agent 无运行态** | 会话缓存没有 pid | 只能显示"多久以前"，不能显示"这条会话在跑" |
| **Instance 与 Agent 口径不一致** | 实例的"在跑"来自 PAD 账本（只管 PAD 自己起的）；会话活跃度来自 mtime | 桌面端 harness 明明在跑，`本机 DSH` 却显示 idle |
| 主 agent 与子 agent 不区分类型 | 只看深度 | 无法表达"这是主代理 / 这是续跑子代理" |

---

## 9. 数据出处速查

| 事实 | 文件 | 字段 |
|---|---|---|
| 实例清单 | `instances/*/instance.json` | 整个对象 |
| 工作区 | 同上 | `workspace.path` |
| 版本 | 同上 | `dsh.version` |
| 用户设的 logo | 同上 | `display.logo` |
| 会话标题 | `home/storages/session_projcache/sessions/session-*.json` | `record.rows.title.val` |
| 子 agent | 同上 | `record.rows.subagentCatalog.val.head.values[]` |
| 上下文规模 | 同上 | `record.rows.contextPressure.val.surfaceTokens` / `.contextWindow` |
| 压缩轮数 | 同上 | `record.rows.contextTimeline.val.archived[]` 长度 |
| 包声明 | `home/profiles/*/package.json` | `dsh.profile.bundles` |
| 在跑 | `home/.pad/nodes.json` | `pid` + 进程存活 |

---

## 10. 代码对应

| 职责 | 文件 |
|---|---|
| 建模 + 计数 | `Core/Graph.cs` |
| 身份指纹 | `Core/Fingerprint.cs` |
| 关系图（web 渲染） | `Views/RelationsWebView.xaml(.cs)` + `Assets/web/page.html` |
| 侧栏容器（舞台层） | `MainWindow.xaml(.cs)` 持有 `Views/ProjectRail`；管理=项目视角、关系=工作区视角（D35），行语言统一、切换 FLIP |
| 舞台动画 | `Views/Fx.cs`（PageIn / Morph / WidthTo） |
| 列表与卡片 | `Views/WorkbenchView.xaml.cs` |
| 机器输出 | `Core/Cli.cs` → `pad cli relations [--md]` |

> 规矩：**文档里不写数量。** 要数字就跑命令，让它自己说。
