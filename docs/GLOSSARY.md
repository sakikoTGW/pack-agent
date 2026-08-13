# APL 术语表

updated: 2026-07-30

APL = Agent Pack Language。本文定义 pack-agent 文档与处理器共用的核心名词。

---

## Pack

**Pack** 是一份 agent 能力程序的完整单元：由 `Pack.toml` 清单、若干 **Unit** 实体树、以及可选的 **World** 链接声明组成。经处理器 `build` 后产出 **IR** 与可分发 `.pack.zip`。

---

## Kind

**Kind** 是 Unit 的类型规章，类似 LaTeX 宏包或 MATLAB 工具箱契约。定义 layout（目录布局）、schema（字段约束）、可选 processor 钩子，以及该 Kind 实现的 **Trait** 集合。stdlib Kind 如 `agent.skill`；方言 Kind 如 `cos.collab` 由社区或样例仓提供，不焊进内核枚举。

---

## Unit

**Unit** 是 Pack 内的一个能力模块实例，在 `Pack.toml` 中用 `[[unit]]` 声明，绑定一个 Kind 与本地路径。例：一个 skill 目录、一组 rules、一个 MCP 配置块。

---

## World

**World** 描述 Pack 对外可见的符号边界：哪些能力 **export** 给其他 Pack 或 harness 消费，哪些 **import** 依赖外部满足。World 闭合后由处理器做链接检查；未满足的 import 硬错误。

---

## ABI

**ABI**（Application Binary Interface，此处指能力接口契约）是 export / import 的具名约定：导出方承诺提供什么能力签名，导入方声明需要什么。链接阶段校验 import 是否被 export 满足；冲突或未满足时拒编译。

---

## Edition

**Edition** 是 APL 源语言版本号（如 `edition = "2026"`），钉死语法子集与语义规则。未知 edition → `E-EDITION-UNKNOWN`。与 IR schema 版本正交但有关联映射。

---

## IR

**IR**（Intermediate Representation）是处理器 lowering 后的中间表示，写入 `.pack.zip` 内的 `pack.json`（schema 如 `agent-pack-ir/2026`）。IR 是 codegen 与 install 的统一输入；人类作者通常编辑 `Pack.toml` 而非直接改 IR。

---

## Harness

**Harness** 是运行 agent loop 的宿主环境：Cursor、Claude Code、Codex、OpenClaw、Hermes 等。Harness 决定如何加载 skill、拼请求、管 tool；APL 处理器只负责把 Pack **投射**到各 harness 的目录布局，不取代 harness 内部运行时。

---

## Trait

**Trait** 是 Kind 必须或可选实现的能力接口，由处理器在 check / build / install 管线中调度。stdlib 约定包括：`Describe`、`Validate`、`Scan`、`Embed`、`Project`、`Eject`、`ExportAbi`、`ImportAbi`。

---

## Dialect

**Dialect** 指基于 APL 扩展机制发布的**方言 Kind 包**，为特定方法论或领域提供专用 layout 与 ABI。`cos.collab` 是方言样例，承载 Cos Collab 规章与 `.collab/**` 布局；方言与 stdlib 地位相同，均通过 Kind 规章加载，不是内核模块 ID。

---

## Processor

**Processor** 指 `packagent` 工具链：解析 Pack 源、加载 Kind、执行 typeck 与链接、产出 IR / zip、按 harness **codegen**，并提供 `check`、`build`、`install`、`eject`、`explain`、`dump-ir` 等动词。APL 的价值一半在语言，一半在处理器。
