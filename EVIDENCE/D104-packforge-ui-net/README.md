# D104 · PAD 窗口 `.dspack` 拖入接线 + 网络验证（files[] / github 依赖）

日期：2026-11-02｜状态：**已实现并验证**（UI 窗口 + 网络两条链路）

配套：[D103-pad-packforge-wiring](../D103-pad-packforge-wiring/README.md)（CLI 面）｜设计稿：[pack-v2-superset.md](../../agent-pack-dsh/docs/pack-v2-superset.md) §11

## 1. UI 接线（拖入 `.dspack` → 新实例 + compat 安装）

| 文件 | 改动 |
|---|---|
| `pad/Core/LaunchPolicy.cs` | `IsDspackDrop`、`DspackPaths(IDataObject)`；与 `.pack.zip` 判定分开 |
| `pad/MainWindow.xaml.cs` | 已有窗口拖放处理扩展：拖入 `.dspack` → `TakeDspacks`（去重/忙锁）→ `PacksForge.ImportAuto`；原生 `.pack.zip` 路径不变 |
| `pad/Views/VersionsView.xaml.cs` | 「装整合包…」文件框支持 `*.dspack`，选择后走 compat 引擎（不落 native 投影） |
| `pad/Core/PacksForge.cs` | 新增 `Inspect` / `ImportAuto`：manifest 取 name/profile，发行号取 `dshVersion`∩已装 或 `dshVersions`∩已装；实例名冲突自动 `-2/-3…` |
| `pad/Core/Cli.cs` | `pack cli packforge import-auto`（与 UI 同一函数）+ `pad cli version --json`（registry 认领要求的自报版本） |
| `modpack/pad-dspack-ui.test.ts` | 源码契约测试：UI 必须走 `PacksForge.ImportAuto`，不能落回原生 import |

验证：

```powershell
bun run agent-pack-dsh/modpack/pad-dspack-ui.test.ts   # 16 项契约断言全过
dotnet build agent-pack-dsh/pad/Pad.csproj -c Debug -o .tmp/pad-build-z   # 0 error
# 与 UI 同函数的 CLI 路径（隔离 root，真实 pokemon 包）：
#   import-auto #1 → instance pokemon（release 0.1.5-alpha.2，manifest 声明）
#   import-auto #2 → instance pokemon-2（重名自动后缀）
#   profile/package.json bundles + overrides 落盘
```

注：拖拽交互本身没有在桌面里人工拖动验证（无头环境），但窗口事件、文件框、`import-auto` 共用同一个 `PacksForge.ImportAuto`，且契约测试锁住接线。

## 2. `files[]` 真实网络下载

构造 pad-pack v2（`dsh.files[]` 指向真实 CDN）→ 导出 `.dspack` → 安装：

```
url    = https://cdn.jsdelivr.net/npm/semver@7.7.3/package.json
sha256 = 7e0b9f6b53e048b53b3ca1174a2faf1fcf0206497297615d9b5d02fda8f265c5
size   = 1663
```

结果：`files.written=["assets/semver-package.json"]`，`failed=[]`；落盘文件 sha256/size 与声明一致（1663B）。

## 3. `github:` commit 依赖真实安装

（本机无全局 `dsh`；shim 指向真实 DSH 0.2.1-rc.2 CLI，node 24 / pnpm 11.7）

```
deps = { "is-number": "github:jonschlinkert/is-number#98e8ff1da1a89f93d1397a24d7413ed15421c139" }
→ dsh 实际调用：plugin --profile gh add is-number@github:jonschlinkert/is-number#98e8ff1…
→ pnpm 安装成功；home2/profiles/gh/node_modules/is-number 存在；package.json 记录 github spec
```

## 4. 真实市场包的兼容性发现

用真实市场包 `pokemon@1.0.0` 跑 `--deps` 失败，根因不在 PAD/compat：

```
pnpm add "@hellosz/dsh-pets@0.3.1"
→ ERR_PNPM_FETCH_404 GET https://registry.npmjs.org/@deepseek-ai%2Fdsh-type-meta
```

即该包的传递依赖 `@deepseek-ai/dsh-type-meta` 是**非公开**包，公网无法安装。PA155 已把 `dsh` 的 pnpm 日志路径带出来，用户能看到真实原因。这说明市场包质量参差，PAD 无需为每包兜底，但需要在安装失败时保留可诊断输出（已满足）。

## 5. 边界

1. 拖拽只做 `.dspack`；原生 `.pack.zip` 保持原逻辑（同一窗口事件里分流）。
2. `dshhome` 仍 PA151（M4）。
3. 网络验证：`files[]` 用真实 CDN；github 依赖用真实 GitHub commit；真实市场包因上游私有依赖无法安装（记录如上）。
4. `pad cli version` 在 Debug 构建返回程序集版本 0.1.0；正式 release 由 `build-release` 注入版本号。
