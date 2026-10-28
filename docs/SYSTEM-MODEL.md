# 系统模型：系统 → 层 → 模块

> **2026-10-23 · 实验性功能（D20）**
> 这套结构不进主界面、不参与默认启动路径。默认形态是**一个项目 = 一份 `DSH_HOME` = 一个工作区**。
> 保留理由：同一项目里不同职能需要不同工具面。转正或删除的条件见 DECISIONS.md D20。
> 另注（D18）：本文多处把"断言 / 验收"写成 PAD 的职能，**越界了**。
> PAD 是启动器：它建结构、起实例、记账本，不执行断言、不判读结果。
> 各模块目录下的 `ACCEPTANCE.md` 只是留给人的空文件，PAD 永远不读它。

date: 2026-10-22
status: draft
例子贯穿全文：复刻 `qiuzhifangzhou.com`（11 个产品）

## 为什么不是"开一堆工作区"

"11 个部分 → 开 11 个工作区" 听着对，但会原样复现老问题：

- 没有**层**，就没有**工具预算**。11 个模块都用同一份 profile，回到默认全量，71 个工具照旧
- 11 个模块各自一条长会话，上下文照旧膨胀，只是分散在 11 个地方

所以需要三级，而不是一级。

## 三级

| 级 | 是什么 | 边界 | 例 |
|---|---|---|---|
| **系统 System** | 一个可交付的东西 | 一个仓库 + 顶层验收 + 非目标 | `qiuzhifangzhou` |
| **层 Layer** | 按职能切 | **一个隔离实例 = 一套工具预算** | `collect` / `frontend` / `backend` / `data` |
| **模块 Module** | 按产物切 | **一个工作区 = 一条会话 = 一组断言** | 11 个产品 |

**层是实例，模块是工作区。** 这个分工不是拍脑袋：

- 工具预算按**职能**分，不按产物分。信息搜集要 `web_search / web_fetch / browser_*`；前端要 `read / write / edit / pwsh / browser_*`；后端要 `pwsh / grep / glob`，不要浏览器
- 11 个模块各开实例 = 11 份 `node_modules`，磁盘和时间都吃不消
- 但模块必须有独立工作区，否则 11 条会话互相污染

## 盘上布局

```
<system>/
  SYSTEM.md                人写：目标、顶层验收、非目标
  plan.yaml                机器读：layers[] + modules[] + 依赖
  modules/<id>.md          该模块的可执行断言
  EVIDENCE/<module>/<n>/   证据
  .pad/                    实例绑定、运行态（gitignore）
```

`plan.yaml` 示例：

```yaml
name: qiuzhifangzhou
goal: 复刻 11 个产品，前端 + 数据 + 采集三条线
layers:
  collect:  { profile: collect,  tools: [web_search, web_fetch, browser_*] }
  frontend: { profile: web-ui,   tools: [read, write, edit, pwsh, browser_*] }
  backend:  { profile: service,  tools: [read, write, edit, pwsh, grep, glob] }
modules:
  - id: job-board-data
    layer: collect
    deps: []
    accept: modules/job-board-data.md
  - id: ai-apply
    layer: frontend
    deps: [job-board-data]
    accept: modules/ai-apply.md
  # …其余 10 个
```

## 依赖图就是"架构"

11 个模块不是并行的：

- **信息搜集**是全部模块的上游（没有数据就没有页面）
- **数据表**（校招汇总 / 实习汇总 / 笔试真题）是展示型模块的上游
- **前端外壳**是 11 个页面的上游

PAD 展示这张 DAG 和每个节点的状态——**这就是"有架构"的样子**，而不是 11 个并列的文件夹。

## PAD 的边界（承 D6，2026-10-23 按 D18 更正）

| 做 | 不做 |
|---|---|
| 建目录、写 `plan.yaml` 骨架 | 替你决定 11 个模块怎么拆 |
| 起实例、绑工作区、发工具预算 | 写任何 prompt |
| **拉起 / 收回 / 记账本** | **执行断言、判读结果、验收（D18）** |
| 展示 DAG 与**在跑 / 已停** | 决定先做哪个 |

一句话：**PAD 管"起没起来"，不管"做得对不对"。**

## 前端怎么表达（承 D7）

不渲染 `plan.yaml`，不解释架构。

- 一列**层**，每层一个环，环的宽度就是工具预算
- 环内按**依赖顺序**排小节点，节点大小 = 优先级
- 节点状态只用两态：**在跑 / 已停**（再加"有窗口"这种物理事实）。不出现"待验收 / 通过"——那是人的事
- 点节点 = 进入该工作区（启动对应实例）
- 全屏唯一的文字是模块短名（≤ 6 字）

自检：如果一屏里出现了"因为……所以……"，说明又把工作笔记搬上来了。

## 未决

1. **层是不是实例边界**——若要模块级隔离，代价是 11 份实例
2. **模块内部要不要再切 stage**——一个模块可能有多轮验收
