# PAD 文档（唯一入口）

2026-11-04 · 文档规则 + 活文档索引

> **规则**：设计类活文档只允许 **5 份**（下面是 5 份）。已落地/研究完成/被取代的方案一律进 `archive/`（只存档，不维护）。证据放 `EVIDENCE/`，不进本目录。新文档必须先加进这张索引并标状态，否则视为草稿，不进仓库。

---

## 活文档（5）

| 文档 | 内容 | 状态 |
|---|---|---|
| [PRODUCT.md](PRODUCT.md) | 产品定义、四层模型、红线、对 PCL 的取舍 | 活 |
| [launcher-architecture.md](launcher-architecture.md) | 现在是什么：形态/磁盘布局/领域模型/命令面/验证回滚/已知耦合 | 活（精简版） |
| [frontend-spec.md](frontend-spec.md) · [视觉手册](frontend-spec/index.html) | UI 法：文字纪律、状态表达、组件、图标唯一源、10 条清单 | 活 |
| [dev-mode.md](dev-mode.md) | 开发模式 = 开发会话；trial overlay → 验证 → 固化/丢弃；复现包 | 活（P0 已落码） |

> 第 5 份是仓库根的 [`DECISIONS.md`](../../DECISIONS.md)：只追加的决策历史，不当规范读，只回答“为什么当时这么定”。

## 归档（`archive/`，不维护）

| 类别 | 文档 |
|---|---|
| 已落地设计稿 | `hot-reload-integration.md`、`pack-plugin-layering.md`、`pack-v2-superset.md`、`session-jump.md`、`launcher-p0.md`、`proposals/install-concept/` |
| 被取代 | `lab.md`（被 `dev-mode.md` 取代） |
| 竞品/生态研究 | `launcher-behavior-2026-11.md`、`launcher-inventory-2026-11.md`、`launcher-landscape-2026-11.md`、`launcher-top30-2026-11.md`、`launcher-opportunities-2026-11.md`、`launcher-pcl-depth.md`、`launcher-design.md` |
| 概念/实验稿 | `ATTENTION.md`、`MODPACK-STATE.md`、`SYSTEM-MODEL.md`、`SURFACE.md`、`RELATIONS.md`、`modpack.md` |
| 架构完整历史版 | `launcher-architecture-full-2026-11.md` |

## 相关目录

- `EVIDENCE/`：每个特性的真机证据（截图/日志/JSON），按 `Dxxx-*` 命名。
- `modpack/registries/`：错误码（`pa-codes.json`）、崩溃规则等数据文件。
- 仓库根 `DECISIONS.md`：决策流水（D100+）。
