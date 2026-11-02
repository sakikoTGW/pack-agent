# D107 · 工作区快照导出（workspace + sessions）

日期：2026-11-02｜状态：**已实现并验证**（导出侧；安装/路径重写不在本次范围）

术语：**工作区快照（workspace snapshot）**。英文标识 `workspace snapshot` / `pad-workspace-snapshot`；不再使用比喻性叫法。

设计依据：[MODPACK-STATE.md](../../agent-pack-dsh/docs/MODPACK-STATE.md)｜配套：[D106](../D106-instance-dspack-export/README.md)

## 1. 功能

把一个 PAD 实例里的「workspace 条目 + 它的 session 文件」导出成 `.workspace.zip`：

```
snapshot.json                                     ← schema pad-workspace-snapshot/v1 + originPath/sessionIds/requires
state/storages/workspace.json                     ← 裁剪成只含选中的 workspace 与 session
state/sessions/<escapedPath>/<sessionId>/...      ← 选中 session 的原始文件（含 .zstd）
```

- `--workspace id[,id]` 只导指定 workspace；缺省全部
- `--max-sessions N` **按 session 文件 mtime 倒序**，每个 workspace 保最近 N 条
- 记录 `originPath`（`<workspace>.path`）与 `encodedDir`，供安装端做三处路径重写（安装侧未实现）
- `requires.profiles` / `requires.plugins` 从实例 `lastProfile` 的 package.json 取，安装端可据此检查环境
- 不导出：凭据、`settings.yaml`、`launcher.patch.yml`、启动器目录；只碰 `storages/workspace.json` + 选中 sessions

## 2. 实现

| 文件 | 内容 |
|---|---|
| `agent-pack-dsh/modpack/workspace-snapshot.ts`（新） | `exportWorkspaceSnapshot` / `readWorkspaceSnapshot`（自检读取）/ 类型 |
| `agent-pack-dsh/modpack/workspace-snapshot.test.ts`（新） | 3 组：全量导出+排除、单 workspace + maxSessions、未知 workspace PA150 |
| `packforge.ts` CLI | `packforge export-workspace <home> --out x.workspace.zip [--workspace] [--max-sessions] [--name] [--version] [--json]` |
| `pad/Core/Packs.cs` | `PackforgeExportWorkspace(...)` |
| `pad/Core/Cli.cs` | `pad cli packforge export-workspace <home> --file x.workspace.zip [--workspace] [--max-sessions] [--out json]` |
| `pad-dspack-ui.test.ts` | 契约补 export-workspace 接线 |

## 3. 验证

```powershell
bun run agent-pack-dsh/modpack/workspace-snapshot.test.ts
# ✔ export all: 2 workspaces / 3 sessions, trimmed workspace.json, sensitive excluded
# ✔ export filtered: originPath kept, newest session only, workspace.json trimmed
# ✔ unknown workspace → PA150
# ALL WORKSPACE SNAPSHOT TESTS PASSED
```

真实实例（`.pack-launcher/instances/123`，1 workspace / 4 个真 session）：

```
TS:  export-workspace … --out 123.workspace.zip
     name=123 kind=workspace dshVersion=0.1.0-rc.7
     workspaces=1 sessions=4 bytes=65061 originPath=E:\pack-agent encodedDir=--E-pack-agent--
     zip: snapshot.json + state/storages/workspace.json + 4 个 session.jsonl.zstd
     无凭据/settings/launcher.patch.yml

PAD: pad cli packforge export-workspace <home> --file pad-123.workspace.zip \
       --workspace <id> --max-sessions 2 --name pad-workspace
     exit 0；zip 里只剩最新 2 个 session（按 mtime），workspace.json 同步裁剪
```

`dotnet build` 0 error；UI 契约测试通过。

## 4. 边界（重要）

1. **只做了导出**。安装端（写回 workspace.json、重写 sessions 目录名、改 zstd 首帧 `identity.cwd`）未实现——这是 MODPACK-STATE.md 里列出的三个锚点，需要单独一轮。
2. `kind` 固定 `workspace`；内容层（整合包）仍走 `.dspack`；`both` 需要两包组合，未做。
3. `--max-context`（按 token 跳过大 session）未实现；当前用 `--max-sessions` 按 mtime 控制。
4. workspace 里的**代码文件本身**不打包（MODPACK-STATE 未决项 1）。
5. `encodedDir` 只记录导出时目录名，不实现转义算法；安装端可据此原样落盘，但目标路径变化时需要重算（未做）。
