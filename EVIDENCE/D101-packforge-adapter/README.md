# D101 · PackForge `.dspack` 适配器（M1/M3 先行版）

日期：2026-11-02｜状态：**适配器已落地并验证**（尚未接入 C# compat 安装引擎）

设计稿：[`agent-pack-dsh/docs/pack-v2-superset.md`](../../agent-pack-dsh/docs/pack-v2-superset.md)

## 1. 交付

| 文件 | 行数/大小 | 内容 |
|---|---|---|
| `agent-pack-dsh/modpack/packforge.ts` | 701 行 | `.dspack` 读取/校验、manifest v5 校验、路径安全、导出过滤、版本决策、launcher 判定、`.dspack ⇄ pad-pack v2` 转换、CLI |
| `agent-pack-dsh/modpack/packforge.test.ts` | 344 行 | 17 项断言（夹具自建，覆盖坏包/安全/决策/往返/导出守卫） |
| `agent-pack-dsh/modpack/cli.ts` | +12 行 | `packagent dsh packforge inspect|convert|export|decide|launchers` |
| `package.json#scripts.test` | +1 项 | 测试链加入 `packforge.test.ts` |

诊断码：PA140（容器/manifest 读取失败）、PA141（manifest 不合规）、PA142（不支持的版本/type）、PA143（不安全路径）、PA145（导出缺精确 dshVersion）、PA148（依赖未锁死）。

## 2. 验证 1：单元/夹具测试

```powershell
bun run agent-pack-dsh/modpack/packforge.test.ts
```

结果：**ALL PACKFORGE TESTS PASSED**（17 项）：

- 读 profile / dshhome `.dspack`；layout（overrides/home/rootFiles）、launchers 简式与全式
- 坏包：缺 `dspack.json`→PA140；容器 v4→PA142；profile bundles 空→PA141；dshhome 含 `web` profile→PA141
- 路径安全：`../`、绝对路径、盘符全部拒（PA143）；导出排除 `.credentials.yaml` / `sessions/`，保留普通文件
- digest：sha256 + size 校验（错 hash / 错 size 均失败）
- 版本决策：pinned / set-latest / needs-install / latest-installed / none
- launcher 判定：ok / notice（minVersion 不足）/ warn（supported:false，放行） / 未声明
- 转换：`.dspack → pad-pack v2`（profile 与 dshhome，dshhome 带 M4 警告）
- 导出：round-trip；拒绝未锁死依赖（PA148）；拒绝缺精确 dshVersion（PA145）

## 3. 验证 2：CLI 端到端

```powershell
bun run agent-pack-dsh/modpack/cli.ts packforge inspect .tmp\packforge-fixtures\out\profile.dspack
bun run agent-pack-dsh/modpack/cli.ts packforge convert <dspack> --out profile.padpack.json
bun run agent-pack-dsh/modpack/cli.ts packforge decide <dspack> --installed "0.2.2-alpha.2,0.1.0-rc.7"
bun run agent-pack-dsh/modpack/cli.ts packforge launchers <dspack> --id pad --version 0.4.5
bun run agent-pack-dsh/modpack/cli.ts packforge export profile.padpack.json --out reexport.dspack
```

结果：

- `inspect`：type/dshVersion/dshVersions/layout/launchers 全部正确
- `decide`：`0.2.2-alpha.2 source=set-latest`（交集内取最新；加引号传逗号列表，Windows 下不加引号会被 PowerShell 吞）
- `launchers`：`pad` → `ok`；`hdsl`（supported:false）→ `warn …继续安装`
- `export` 产物：PowerShell `Expand-Archive` 可正常解压，根含 `dspack.json`（`{"format":"dspack","version":3}`）+ `manifest.json`（v5，字段齐全）

## 4. 验证 3：pack-runtime bundle

```powershell
bun build src/cli.ts --target=bun --outfile .tmp/packforge-bundle/packagent.bundle.js
```

- 216 modules / 0.85 MB，`packforge` 被内联进单文件 bundle
- 用 bundle 跑 `... dsh packforge inspect` 与 `decide` 均正常 → pack-runtime（kit/单文件 exe）可直接携带

## 5. 验证 4：真实 PackForge 市场包

市场索引 `https://dsh-packforge.github.io/dsh-pack-market/index.json`（2026-10-23 生成，schemaVersion 2）：

- 8 个包，全部 `type: profile`；**manifest v5 = 7，v4 = 1**；**dshVersions 0 个、launchers 0 个**（索引层面）→ 印证“规范已出、生态采纳率低”。
- 下载 5 个包（sha256 全部与索引一致）：
  - `pokemon` 2.5KB、`desktop-pack` 3.9KB、`dsh-simple-drawing` 185KB、`better-deepseek-harness-codex` 1.1MB：v5 → `inspect` 全部通过
  - `better-sidebar` v4 → 按预期拒绝 `PA141`（v4 导入是后续决策，不在 M1）
- 真包往返（`better-deepseek-harness-codex-2.8.1.dspack`）：

```
source:  dshVersion=0.2.0-rc.2  versions=[0.2.0-rc.2,0.2.0-rc.1]
         launchers={dshl,dsh-packforge-app}  overrides=1  home=343
convert: 26 bundles / 24 deps / 0 files  warnings=[归档根目录带额外文件：package.json, pnpm-workspace.yaml]
export:  warnings=[]
back:    dshVersion/versions/launchers 全保留  overrides=1  home=343
REAL PACK ROUND-TRIP OK
```

## 6. 明确没做的（下一步）

1. **compat 安装引擎（C# 侧）**：把 `dsh.profile.bundles` / `deps` / `overrides` / `home/` 真正写进实例 profile/home（pnpm 安装 + reconcile）。当前只到“解析 + 转换 + 导出”。
2. **`files[]` 下载**：已有 digest 校验函数与 manifest 校验，未接 DownloadDock/网络抓取与失败回滚。
3. **v4 导入**：按设计只做 v5；v4（历史单 profile）是否兼容待定。
4. **市场/registry 读**：市场索引用脚本验证过，PAD UI/CLI 尚未接入；launcher canonical ID 尚未认领。
5. **转换不携带文件字节**：`convert` 只产出元数据 JSON；要带文件需 `export --layout <解压目录>`（真包往返就是用 `readDspack({keepExtracted:true})` 的目录做的）。
6. **dshhome 安装**：转换会提示 M4；安装引擎未接。

## 7. 复现命令

```powershell
cd E:\pack-agent
bun run agent-pack-dsh/modpack/packforge.test.ts
bun run agent-pack-dsh/modpack/import-pack.test.ts   # 回归
bun build src/cli.ts --target=bun --outfile .tmp/packforge-bundle/packagent.bundle.js
```
