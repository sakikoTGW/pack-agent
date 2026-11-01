# D88 · 便携包自带 pack 运行时（整合包体系"带得走"）

日期：2026-11-01
承接：D84 端到端验收发现"便携包里整合包管理断链"；D85 修了 list/启停的最小可用；
本轮把 **打包后的 TS CLI + bun + 预编译 pack-index** 塞进便携包，目标机器零依赖。

## 做了什么

1. **运行时打包**（`Launcher.TryStagePackRuntime`，导出时执行）
   `<dest>/pack-runtime/` 包含：
   - `packagent.bundle.js`（`bun build src/cli.ts --target=bun`，~0.8MB）
   - `bun.exe`（~115MB）
   - `pack-index.exe`（预编译，~2.1MB）
   - `package.json`（`@sakikotgw/pack-agent-dsh` 包根标记）、`agent-pack-dsh/modpack/registry.yaml`、`skills/agent-pack/SKILL.md`
   - `runtime.json`
   导出机缺 bun/仓库/索引时自动跳过（便携包仍有原生 list/启停兜底）。

2. **PAD 运行时模式**（`Packs.RuntimeDir/Entry`）
   exe 旁边的 `pack-runtime/` 存在时，所有 pack 操作改成：
   `bun.exe packagent.bundle.js dsh launcher --root ... --json ...`，并设 `PACK_INDEX_BIN`；
   单文件 exe 用 `Environment.ProcessPath` 找 exe 目录（`AppContext.BaseDirectory` 在自解压时是临时目录）。

3. **导出重索引**（`Launcher.ReindexPortableCatalog`）
   导出复制实例后，用 `pack-index index --root <新根>/modpacks --db catalog.sqlite` 重写绝对路径，
   保留 enabled allow 集；否则便携包里 DB 指向源机路径。

## 验证（证据目录同名前缀）

- 导出的便携包（`export5`，725.5 MB / 30676 文件，223s）在**无仓库、无环境变量**下：
  - `pack list e2e3` → 能列出，Dir 指向 export5 新根；
  - `pack allow/deny e2e3 e2e-pack` → 状态正确切换（走包内 TS + SQLite，真实白名单）；
  - `instance create e2e5` + `pack project` → 投影成功（完整安装链）。
- 单文件主机需重新 `dotnet publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`（本轮已发布到 E:\tmp\pad-win-x64-onefile）。

## 代价 / 边界

- 便携包 +115MB、导出多约 60-90s（复制 bun）；
- 导出机需要：仓库（TS 源码）+ bun + 预编译 pack-index（有仓库时自动找）；
- 目标机零依赖，但若目标机不运行 bun 子进程（被安全软件拦），仍回落原生 list/启停（只 PAD 侧）。
