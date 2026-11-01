# D85 · 修 D84 三个断点（便携整合包管理 / id 解析 / installed）

隔离方式：全程 CLI + 隔离根（`E:\tmp\pack-e2e`），无窗口、不抢前台；最后统一编译到 `E:\tmp\pad-build\lead`。

## 1) 便携包不再需要仓库也能看/启停整合包（断点 1）
- `Packs.List`：CLI（TS + 仓库）失败时退回 **原生扫描** `workspace/.agent-pack/modpacks/<dir>/catalog.json`；
- 启用状态存 `workspace/.agent-pack/pad-packs.json`（导出时随实例一起带走）；
- `Packs.Allow/Deny`：仓库可用走原链路并同步 sidecar；仓库不可用走原生启停；
- `Unavailable()` 不再整块禁用（投影/导入仍会各自报“需要仓库”的明确错误）。
实测（导出包目录、不设 PACK_AGENT_REPO）：
`01-native-list.txt` 能列出投影包；`02-resolve-id-allow.txt` 启停成功并写 sidecar。
**边界**：原生启停只改 PAD 侧状态（不重写 DSH 的 SQLite 白名单）；真正影响 DSH 会话仍需仓库工具。投影/导入同理。

## 2) id 解析（断点 2）
`Allow/Deny` 先解析：接受 投影 id / 包名（`e2e-pack`）/ `name@version` / `name-version`；
解析失败时错误里列出可用 id：
`03-bad-id-error.txt` → `error[PA012] ... 可用：pack-agent-modpack-e2e-pack` + help。

## 3) `pack list --json` 的 installed（断点 3）
- TS `readInstalledIndex` 新增 `noteProjected`：读 `instances/<id>/workspace/.agent-pack/modpacks/*/catalog.json`，
  按 `name` / `name-version` / `name@version` / 目录名建索引；
- 修了“实例没有 source.packId 就直接 continue，导致投影包不被记录”的 bug。
实测：`04-installed-field.txt` → `e2e-pack-0.1.0` 的 `installed: ["e2e"]`。

## 验证
- 最后统一编译：`dotnet build -o E:\tmp\pad-build\lead` → 0 warning / 0 error；
- `pad-pack` / `pad-window` / `pack-catalog` 全绿。
