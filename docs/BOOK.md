# APL 入门教程

updated: 2026-07-30

从零写一个最小 skill 包，经处理器校验、解释、打包。规范细节见 [LANGUAGE.md](./LANGUAGE.md)；术语见 [GLOSSARY.md](./GLOSSARY.md)；stdlib Kind 列表见 [KINDS.md](./KINDS.md)；愿景见 [APL_VISION.md](./APL_VISION.md)。

---

## 1. 准备目录

在项目根建一个空包目录，例如 `hello-skill/`：

```
hello-skill/
├── Pack.toml
└── skills/
    └── hello/
        └── SKILL.md
```

`SKILL.md` 是 `agent.skill` Kind 的必填文件，至少写标题与用途：

```markdown
# Hello

一个演示 skill，供 packagent 校验与打包。
```

---

## 2. 写 Pack.toml

APL 源语言是 TOML 子集。最小清单需要 `edition`、`name`、至少一个 `[[unit]]`：

```toml
edition = "2026"
name = "hello-skill"

[[unit]]
name = "hello"
kind = "agent.skill"
path = "skills/hello"
```

| 字段 | 含义 |
|------|------|
| `edition` | 源语言版本，当前唯一合法值 `"2026"` |
| `name` | Pack 标识，供 explain 与分发 |
| `[[unit]].name` | Unit 在包内唯一名 |
| `[[unit]].kind` | Kind id，stdlib 见 [KINDS.md](./KINDS.md) |
| `[[unit]].path` | 相对 Pack 根的实体目录 |

可选 `[world]` 声明 export/import 符号，用于跨 Unit 链接；见 [LANGUAGE.md §5](./LANGUAGE.md)。

---

## 3. 校验：packagent check

在包目录外或根目录执行：

```bash
packagent check hello-skill/Pack.toml
```

成功输出 `OK: check`。失败时打印稳定错误码，例如：

- `E-EDITION-UNKNOWN` — edition 不是 `"2026"`
- `E-KIND-NOT-FOUND` — Kind id 未在 stdlib 或 `kinds/` 找到
- `E-ABI-UNSATISFIED` — `[world]` import 无人 export

check 只做语义校验，不写文件。

---

## 4. 解释：packagent explain

```bash
packagent explain hello-skill/Pack.toml
```

输出 Pack 名、edition、各 Unit 的 Kind 标题与 ABI 摘要，例如：

```
pack: hello-skill
edition: 2026
units:
  - name: hello  kind: agent.skill  title: Agent Skill  abi.exports: skill
world: (none)
```

陌生人打开仓库也能看懂「包里有什么」，无需读内核源码。

---

## 5. 打包：packagent build

```bash
packagent build hello-skill/Pack.toml --out hello-skill.pack.zip
```

处理器 lowering 后写入 `.pack.zip`，内含 IR 格式的 `pack.json`（schema `agent-pack-ir/2026`）与实体文件树。build 前会隐式跑 check；有错则拒编。

---

## 6. 安装（现状）

旧 `ccui-pack/v0.2` 导出的 `.pack.zip` 可直接 `packagent install` / `eject`。由 `Pack.toml` `build` 产出的 IR zip 可分发，install 经 Kind Trait 全链路仍在收编（见 [KINDS.md](./KINDS.md)）。

---

## 7. 延伸阅读

| 主题 | 文档 |
|------|------|
| 语法、错误码、World/ABI | [LANGUAGE.md](./LANGUAGE.md) |
| 术语 | [GLOSSARY.md](./GLOSSARY.md) |
| stdlib Kind | [KINDS.md](./KINDS.md) |
| 愿景 | [APL_VISION.md](./APL_VISION.md) |
| IR 字段 | [PACK_SPEC.md](./PACK_SPEC.md) |
| 验收表 | [APL_VERIFY.md](./APL_VERIFY.md) |

方言样例：`examples/kinds/cos.collab.kind.json`。
