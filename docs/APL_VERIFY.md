# APL 方法论验证表

updated: 2026-07-30

对应实现计划 [2026-07-30-agent-pack-language.md](./superpowers/plans/2026-07-30-agent-pack-language.md) §3。每项给出可证伪实验、证据与结论。

| 方法主张 | 实验 | 证据 | 结论 |
|----------|------|------|------|
| **这是语言** | Book / Reference / IR 三分；`check` 拒非法程序 | `docs/BOOK.md` `docs/LANGUAGE.md` `docs/GLOSSARY.md` 已落地；`bun test test/lang` 23 项全绿（含 `E-EDITION-UNKNOWN` `E-KIND-NOT-FOUND`） | **PASS** |
| **别人能看懂** | 陌生包跑 `explain` | `bun test test/lang/explain.test.ts` 绿；CLI：`packagent explain` 输出 pack/edition/units/kind title/abi | **PASS** |
| **可扩展** | 不改内核，只加 `examples/kinds/*.kind.json` | `examples/kinds/cos.collab.kind.json` + `checkPackToml` 绿（`test/kinds/cos-collab.test.ts`）；方言 install/eject 经 Kind Trait 全链路未测 | **PARTIAL** |
| **跨层通话简单** | import 断链 → `E-ABI-UNSATISFIED`；修好后 check 过 | `test/lang/link.test.ts` 3 项全绿 | **PASS** |
| **一源多后端** | 同一 zip install 到 ≥2 harness | `bun test/install.ts`：claude + codex skill 就位；`test/commands-module.ts`：cursor + claude commands 就位。**局限**：走 legacy `ccui-pack/v0.2` export 路径，非 `Pack.toml` build 的 IR zip | **PARTIAL** |
| **方言非内核** | 无 `PackModuleId = 'collab'` | `src/modules.ts` 无 collab；`test/kinds/cos-collab.test.ts` 断言 `DEFAULT_PACK_MODULES` 无 collab | **PASS** |
| **旧世界不炸** | 升格 v0.2 → IR 2026；旧包 install 仍可用 | `bun test test/upgrade/v02.test.ts` 绿（`upgradePackDocToIr`）；`bun test/pack-zip.ts` + `test/install.ts` 绿（legacy zip/json）。IR zip 经 `build` 产出后 `installPackFile` 仍返回 `ok: false`（install 未接 IR 入口） | **PARTIAL** |

---

## 命令复现

```bash
# 语言管线（Task 3–8）
bun test test/lang test/kinds

# 旧包升格（Task 11 函数层）
bun test test/upgrade/v02.test.ts

# 多 harness 安装（legacy 路径）
bun test/install.ts
bun test/pack-zip.ts

# APL CLI 端到端（agent.skill 最小包）
packagent check path/to/Pack.toml
packagent explain path/to/Pack.toml
packagent build path/to/Pack.toml --out out.pack.zip
```

---

## 已知缺口（不阻塞 Task 12 文档收编）

| 缺口 | 说明 |
|------|------|
| scan/install Trait 全调度 | stdlib Kind 规章与 `getStdlibInstallTemplate` 已落地；`scan-modules` / `install-modules` 仍走 legacy 模块路径（见 [KINDS.md](./KINDS.md)） |
| IR zip → install | `build` 产出 `agent-pack-ir/2026`；`install` 尚未读 IR zip |
| 中央包仓库 | 计划非目标，未做 |
| explain 金样目录 | 计划提及 `test/golden/explain/`，当前以单元测试字符串断言代替 |

---

## 总评

核心语言管线（parse → check → link → lower → build → explain）与 stdlib/方言 Kind 样例 **已落地**；legacy export/install 多 harness **仍可用**；IR 驱动的 install 与 Trait 收编 **部分完成**。Board 任务标记为核心完成、Trait 收编待续。
