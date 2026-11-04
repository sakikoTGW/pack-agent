# PAD 开发模式 · P0 落码证据（2026-11-04）

AI-first 开发会话第一版。真机 scratch launcher root：`.tmp/pad-hot-e2e/.pack-launcher`（实例 `ev-hot` / profile `web`，真实 dsh 0.2.1-rc.2）。

## 跑通的闭环

| 步骤 | 命令 | 证据文件 | 结果 |
|---|---|---|---|
| 开会话 | `pad cli dev start ev-hot --profile web --json` | `12b-start.out.json` | `state=running`，`sourceHash` |
| 应用改动（只影响本次运行） | `pad cli dev apply ev-hot --profile web --action add --spec file:…/hot-bundle --json` | `13c-apply.out.json` | `changes=[{op:add}]`；profile `bundles` 不变 |
| 验证 | `pad cli dev verify ev-hot --json` | `13d-verify.out.json` | `result=pass`，退出码 **0**；checks：config-compiles / plugins-resolve / source-untouched pass，3 项 P1 skip |
| 固化 | `pad cli dev commit ev-hot --json` | `14a-commit.out.json` | `state=committed`，打点 `20261104-084745-972` |
| 存档可见 | `pad cli checkpoint list ev-hot --json` | `14b-checkpoint-list.out.json` | 5 个存档点，首条 `dev commit … · 4 files` |
| 丢弃 | `dev apply` → `dev discard` | `19b-apply.out.json` / `19c-discard.out.json` | 依赖与 `node_modules` 都被清掉：`dep=False node_modules=False` |
| MCP | `pad cli mcp`（stdio） | `06-mcp.out.jsonl` | `tools/list` 返回 7 个工具；`dev_status` 返回会话+运行事实 |
| 备份清理显式化 | `pad cli checkpoint prune ev-hot --keep 200 --json` | `05-checkpoint-prune-dry.json` | dry-run 只列将要删的，不自动删 |

## 关键事实

- **stdout 纯 JSON**：`--json` 时人类可读行全部走 stderr（见 `03-dev-verify.stderr.txt` 里的 `--- pad cli …` 行），机器可直读 stdout。
- **验证失败不拦改**：`13d` 之前曾出现 `source-untouched` fail，因为第一版指纹把 `dependencies` 也算进声明层——trial 安装会加依赖。现已改成只对 `dsh.profile.bundles` + patch 做指纹（trial 不改 bundles，commit 才改）。
- **file: 依赖的 discard 修复**：`dsh plugin remove` 对 `file:` 规范删不掉（pnpm 报 `no such dependency`）。现在按包名兜底：摘 `dependencies` 键 + 删 `node_modules/<pkg>`；`PackageName("file:…")` 有单测（`DevSessionTests.PackageName_HandlesVersionedScopedAndFileSpecs`）。
- 测试：`dotnet test` **230/230**；构建 0 警告 0 错误。

## 还没做（P1）

- `boots` / `stays-up` / `tools-inventory` 三项判据（当前明确输出 `skip`，不假装通过）。
- 逐项撤销、选择性固化。
- `dev repro export/apply`（复现包）——格式已在 `docs/dev-mode.md` 定好，未落码。
- 窗口 UI（启动方式选择、开发会话抽屉、试验历史）。
