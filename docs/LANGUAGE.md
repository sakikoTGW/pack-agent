# Agent Pack Language Reference

updated: 2026-07-30

APL 源语言与处理器语义的**规范真相**。教程见 [BOOK.md](./BOOK.md)；术语见 [GLOSSARY.md](./GLOSSARY.md)；愿景见 [APL_VISION.md](./APL_VISION.md)；Phase 见 [NORTH_STAR.md](./NORTH_STAR.md)。

---

## 1. Edition

Pack 清单必须声明 edition：

```toml
edition = "2026"
```

| 规则 | 说明 |
|------|------|
| 必填 | `Pack.toml` 顶层 `edition` 字段 |
| 当前唯一合法值 | `"2026"` |
| 未知 edition | **硬失败**，诊断 `E-EDITION-UNKNOWN`；处理器不得猜测或降级语义 |
| 与 IR 关系 | Edition 钉死**源语言**子集与语义；IR schema（如 `agent-pack-ir/2026`）由 lowering 映射，二者正交但一一对应 |

Edition 变更由 pack-agent 发布说明宣布；旧 edition 可在后续版本移除支持，但须给出 upgrade 路径。

---

## 2. 词法与表面语法

APL 源文件为 **TOML 子集**。处理器按 edition 钉死的 grammar 解析；超出子集的键或结构在语义阶段拒收。

### 2.1 文件角色

| 文件 | 角色 |
|------|------|
| `Pack.toml` | Pack 清单：edition、元数据、`[[unit]]`、`[world]` |
| `*.kind.json` | Kind 规章（类型定义）；由 Kind id 解析加载，不在 Pack.toml 内联 |

### 2.2 `Pack.toml` 顶层字段

| 字段 | 必填 | 类型 | 说明 |
|------|------|------|------|
| `edition` | 是 | string | 源语言版本，见 §1 |
| `name` | 是 | string | Pack 标识符；用于 explain、ledger、分发 |
| `version` | 否 | string |  semver 或作者约定版本串 |
| `description` | 否 | string | 人类可读摘要 |
| `[[unit]]` | 是（≥1） | array of table | 能力模块实例，见 §2.3 |
| `[world]` | 否 | table | 导出/导入边界，见 §5 |

不允许 edition `"2026"` 未定义的顶层键；出现即语义错误。

### 2.3 `[[unit]]` 表

每个 unit 绑定一个 Kind 与本地路径：

| 字段 | 必填 | 类型 | 说明 |
|------|------|------|------|
| `name` | 是 | string | Unit 在 Pack 内唯一标识；参与 World 符号解析 |
| `kind` | 是 | string | Kind id，如 `agent.skill`、`cos.collab` |
| `path` | 是 | string | 相对 Pack 根目录的实体树路径 |
| `title` | 否 | string | 覆盖 Kind 默认描述；`explain` 优先展示 |

同一 `Pack.toml` 内 `name` 不得重复；重复 → `E-RESOLVE-FAILED`。

### 2.4 `[world]` 表

| 字段 | 必填 | 类型 | 说明 |
|------|------|------|------|
| `exports` | 否 | array of string | 本 Pack 对外提供的具名符号 |
| `imports` | 否 | array of string | 本 Pack 要求外部满足的具名符号 |

符号名为 opaque string；链接规则见 §5。未声明 `[world]` 时视为无 export/import。

### 2.5 产生式摘要（Manifest）

```
PackManifest   ::= TopLevel+
TopLevel       ::= EditionDecl | MetaDecl | UnitDecl+ | WorldDecl?
EditionDecl    ::= "edition" "=" STRING
MetaDecl       ::= "name" "=" STRING
                 | "version" "=" STRING
                 | "description" "=" STRING
UnitDecl       ::= "[[" "unit" "]]" UnitField+
UnitField      ::= "name" "=" STRING
                 | "kind" "=" STRING
                 | "path" "=" STRING
                 | "title" "=" STRING
WorldDecl      ::= "[world]" WorldField*
WorldField     ::= "exports" "=" SymbolList
                 | "imports" "=" SymbolList
SymbolList     ::= "[" STRING ("," STRING)* "]"
```

Kind 规章（`*.kind.json`）不在此产生式内；由 Kind 加载器单独解析。

---

## 3. Kind 规章

Kind 是 Unit 的类型规章。处理器从 stdlib（`src/kinds/`）及工作区 `kinds/` 按 id 加载 `*.kind.json`。

### 3.1 必填字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | Kind 唯一标识，与 `[[unit]].kind` 匹配 |
| `version` | string | Kind 规章版本 |
| `traits` | string[] | 实现的 Trait 名列表，见 §4 |
| `layout` | object | 目录/文件布局约定；缺失或 unit 路径不符合 → `E-LAYOUT-MISSING` |
| `payload_schema` | object | Unit 载荷的 JSON Schema；校验失败 → `E-SCHEMA-INVALID` |
| `abi` | object | export/import 符号契约模板 |
| `install` | object | 投射到 harness 的安装模板 |
| `portability` | object | 可移植性规则（含排除路径、环境泄漏检测） |
| `describe` | object | **必须**含 `title`；供 `explain` 与 Describe trait |

### 3.2 可选字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `processor` | object | 可选 JS 沙箱钩子；超出能力声明 → `E-PROCESSOR-CAP` |

Kind 缺 `describe.title` → check 硬失败（Describe trait 强制）。

---

## 4. Trait

Trait 是处理器在管线各阶段调度的能力接口。Kind 在 `traits` 中声明实现集合。

| Trait | 职责 |
|-------|------|
| **Describe** | 产出人读描述（title、摘要、ABI 概览）；`explain` 依赖 |
| **Validate** | 按 `payload_schema` 校验 unit 树内容 |
| **Scan** | 扫描 unit 路径，发现实体文件与元数据 |
| **Embed** | 将实体嵌入 IR / `.pack.zip` 归档 |
| **Project** | 按 harness 模板投射文件到目标目录（install 阶段） |
| **Eject** | 按 ledger 反向卸载已投射文件 |
| **ExportAbi** | 声明本 unit 贡献的 export 符号 |
| **ImportAbi** | 声明本 unit 要求的 import 符号 |

未实现某 Trait 而管线需要时 → `E-PROCESSOR-CAP`。

---

## 5. World 与 ABI

**ABI** 是 Pack 间能力接口的具名约定。**World** 是 Pack 级的 export/import 闭包。

### 5.1 符号

- `exports` / `imports` 均为**具名字符串**列表。
- 符号来源：Pack 级 `[world]` 声明，与各 unit 经 ExportAbi / ImportAbi trait 贡献的符号合并解析。
- Scope Graph 在 Pack 内闭合：declarations = unit names + export 符号。

### 5.2 链接规则

| 条件 | 结果 |
|------|------|
| `imports` 中某符号无任何 export 满足 | **硬错误** `E-ABI-UNSATISFIED` |
| 多个 export 提供同一符号且不可合并 | **硬错误** `E-ABI-CONFLICT` |
| 符号解析或 unit 名冲突 | **硬错误** `E-RESOLVE-FAILED` |

链接在 `check` 阶段完成；未通过则不得 `build` / `install`。

### 5.3 可移植性

Kind `portability` 规章定义哪些路径、环境变量或绝对路径引用不得进入分发工件。违反 → `E-PORTABILITY-LEAK`。

---

## 6. 错误码

诊断码稳定、可机读；格式 `E-<CATEGORY>-<DETAIL>`。

| 码 | 含义 | 修复提示 |
|----|------|----------|
| **E-EDITION-UNKNOWN** | `edition` 非处理器支持的版本 | 改为 `"2026"`，或升级 pack-agent |
| **E-KIND-NOT-FOUND** | `[[unit]].kind` 无法解析到 Kind 规章 | 检查 kind id 拼写；安装含该 Kind 的包或放入 `kinds/` |
| **E-LAYOUT-MISSING** | unit 路径不符合 Kind `layout` | 按 Kind 文档调整目录结构或修正 `path` |
| **E-SCHEMA-INVALID** | unit 载荷未通过 `payload_schema` | 对照 Kind schema 修正字段或文件内容 |
| **E-RESOLVE-FAILED** | 名称冲突、重复 unit、符号无法解析 | 保证 unit `name` 唯一；核对 World 与 unit ABI 声明 |
| **E-ABI-UNSATISFIED** | import 符号无对应 export | 添加 export、依赖提供符号的 Pack，或移除多余 import |
| **E-ABI-CONFLICT** | 多个 export 声明冲突的同一符号 | 合并 export 或重命名符号 |
| **E-PORTABILITY-LEAK** | 包内出现 portability 规章禁止的路径/泄漏 | 移除绝对路径、密钥、本机专属路径；用相对路径或 env 占位 |
| **E-PROCESSOR-CAP** | Kind 声明的 processor 能力超出处理器支持 | 简化 processor 钩子或升级 pack-agent |

---

## 7. 语义：处理器动词

处理器 CLI：`packagent check | build | install | eject | explain | dump-ir`。

### 7.1 `check`

**作用：** parse → 语义校验 → Kind 加载 → layout/schema 校验 → Scope Graph resolve → ABI 链接 → portability 扫描。

**副作用：** 只读。不写 IR、不产出 zip、**不修改 install 目标文件系统**。

**失败：** 打印诊断（含上表错误码）；exit code 非 0。**此后不得执行 install 投射。**

### 7.2 `build`

**前提：** 等价于 `check` 全部通过。

**作用：** AST → lower → Pack IR → 写入 `.pack.zip`（含 `pack.json` 与 embed 实体）。

**副作用：** 仅写入**输出路径**指定 artifact；不触碰 harness 安装目录。

**失败：** 不产出不完整 zip；已写部分须清理或原子替换（实现负责）。

### 7.3 `install`

**前提：** 输入为已通过 `check` 的源树或 `.pack.zip`。

**作用：** IR → 按目标 harness codegen → Project trait 投射文件 → 更新 ledger。

**副作用：** 写入 harness 投射目录；记录 ledger 供 `eject`。

**失败：** **`check` 未过则不得 install。** install 中途失败时，实现应尽量回滚当次投射；ledger 仅记录成功完成的 unit。

### 7.4 `eject`

**作用：** 按 ledger 反向执行 Eject trait，删除 install 写入的文件/链接。

**副作用：** 仅移除 ledger 记录的投射；不删除 Pack 源或 `.pack.zip`。

### 7.5 `explain`

**作用：** 人读摘要：name、edition、各 unit 的 kind + title + ABI 概览 + World imports/exports。

**副作用：** 无；stdout 输出。可在 `check` 通过后对源或 zip 运行。

### 7.6 `dump-ir`

**作用：** 将 lowering 结果输出为 JSON（调试 / 工具链集成）。

**副作用：** 仅 stdout 或指定输出文件；不 install。

### 7.7 check 失败与 install 目标隔离（硬规则）

| 阶段 | install 目标文件系统 |
|------|----------------------|
| `check` 失败 | **零副作用** |
| `build` 失败 | **零副作用** |
| `install` 被调用但前置 check 未过 | **拒绝执行**，零副作用 |

作者应先在 CI 跑 `packagent check`，再分发或 install。

---

## 8. 关联文档

| 文档 | 内容 |
|------|------|
| [GLOSSARY.md](./GLOSSARY.md) | Pack、Kind、Unit、World、ABI、Edition、IR、Harness、Trait、Dialect、Processor |
| [APL_VISION.md](./APL_VISION.md) | 领域对标、终极态、成功画像 |
| [NORTH_STAR.md](./NORTH_STAR.md) | 北极星一句话、Phase 顺序 |
| [PACK_SPEC.md](./PACK_SPEC.md) | IR / 遗留 `ccui-pack` 汇编层 |
| [BOOK.md](./BOOK.md) | 教程（待建） |
