# 整合包的两层载荷：内容 与 状态

date: 2026-10-22
status: draft
extends: `modpack.md`（本文只补"地图"这一层，不改装包链）

## 类比成立，但要拆清楚

MC 的整合包有两种：

| MC | 包里有 | 装完你得到 |
|---|---|---|
| 纯整合包 | mods + config | 一个空世界 |
| 地图整合包 | mods + config + **world save** | 一个建好的世界 |

DSH 完全同构：

| DSH | 包里有 | 装完你得到 |
|---|---|---|
| 纯内容包 | skill / MCP / rule / command / plugins | 干净实例 |
| 地图包 | 上面这些 **+ workspace + sessions** | 一个进行中的项目 |

第二种就是你真正需要的东西：**把一个正在做的项目，连同它的对话历史，整块搬到别的实例/别的机器上。**

## 地图在 DSH 里到底是什么

实测（`<DSH_HOME>/storages/workspace.json`）：

```json
"tables": {
  "workspaces": {
    "f16f9133-…": {
      "path": "E:\\修复注意力涣散",
      "title": "修复注意力涣散",
      "sessionIds": ["session-9ec8056c-…"]
    }
  }
}
```

一张"地图" = **一个 workspace 条目 + 它挂的 session 文件**。

## 但地图绑死在绝对路径上——这是搬运的真正难点

同一个 workspace 有三个地方记着它的路径：

| 锚点 | 位置 | 形式 |
|---|---|---|
| 1 | `storages/workspace.json` → `tables.workspaces[*].path` | 明文绝对路径 |
| 2 | `sessions/<转义目录名>/` | 目录名就是路径的转义<br>`E:\修复注意力涣散` → `--E-~4FEE~590D~…--` |
| 3 | 每个 `session.v4.jsonl.zstd` 的**第一个 zstd 帧** | `identity.cwd`（实测约 188 字节） |

**三个都要一起改，漏一个地图就搬不过去。** 第 3 个看起来麻烦，但其实最轻——cwd 就在第一帧里，解压→替换→重压→拼回剩余字节就行，不用碰几 MB 的正文。

这也意味着：**地图包必须在清单里记 `originPath`**，装的时候才知道要替换成什么。

## 清单字段

```yaml
pack: qiuzhifangzhou-frontend
kind: content | world | both
requires:                      # 地图依赖的内容层；缺了地图跑不起来
  plugins: ["@sakikotgw/pack-agent-dsh"]
state:
  workspaces:
    - id: f16f9133-…
      title: 修复注意力涣散
      originPath: E:\修复注意力涣散    # 装的时候重映射
      sessions: 1
      tokens: 130631                   # 让接收方知道这张地图多大
```

`kind: both` 是默认推荐——只发地图不发内容，对方多半跑不起来。

## 装载策略

目标实例已经有会话时，必须显式选一个，不能默认静默合并：

| 策略 | 行为 |
|---|---|
| `append`（默认） | 地图作为新 workspace 加进去，原会话不动 |
| `replace` | 清空该实例的 workspace 再装，需二次确认 |
| `require-empty` | 目标不空就拒绝，退出码非 0 |

## 体积

一个 33 万 token 的会话压实后约 4 MB。地图包默认**只带可见会话**，并支持：

- `--max-sessions N`：只带最近 N 条
- `--max-context N`：超过 N token 的会话跳过（它们本来也搬不动）
- projcache（`storages/session_projcache/`）默认**不带**——它是缓存，重开时重建

## 与形态无关

地图是状态，`tui` / `web` / `app` 共用同一张地图。换形态不影响装了什么，换包不影响窗口长什么样（承 `SURFACE.md`）。

## 未决

1. 地图里的会话指向的工作区文件（代码本身）要不要一起打包？MC 的 world save 是自包含的，DSH 的地图不是——它只带对话，不带仓库
2. `replace` 策略要不要先做一次快照（相当于"备份存档"）
