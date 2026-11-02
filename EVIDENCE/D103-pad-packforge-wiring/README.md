# D103 · PAD C# 侧接线：packforge CLI + 实例创建 + 真实依赖安装

日期：2026-11-02｜状态：**已实现并通过端到端验证**（CLI 面；PAD 窗口 UI 尚未接入）

配套：[D101-packforge-adapter](../D101-packforge-adapter/README.md)、[D102-packforge-compat-market](../D102-packforge-compat-market/README.md)｜设计稿：[pack-v2-superset.md](../../agent-pack-dsh/docs/pack-v2-superset.md) §11

## 1. 改动

| 文件 | 内容 |
|---|---|
| `pad/Core/Packs.cs` | 新增 `RunCapture(tail)`（完整 stdout 捕获）、`PackforgeJson/Inspect/Market/Registry/Install` 封装；Entry 自动选「便携 pack-runtime」或「仓库 node 模式」 |
| `pad/Core/PacksForge.cs`（新） | `Import(...)`：inspect → 选 dsh 发行号（`--release`/`dshVersion`/`dshVersions`）→ 校验已安装 → **先建隔离实例** → 调 pack-runtime `packforge install` 装进该实例 home；dshhome → PA151 |
| `pad/Core/Cli.cs` | 新命令组：`pad cli packforge inspect / market / registry / install / import`，全部支持 `--out`（WinExe 的可脚本通道） |
| `pad/Cli help` | 命令面提示 |

## 2. 构建

```powershell
dotnet build agent-pack-dsh/pad/Pad.csproj -c Debug -o .tmp/pad-build-z
# 0 warning / 0 error
```

（直接 `bin/Debug` 被正在运行的 PAD 进程锁住 exe，故用 `-o` 到临时目录验证；正式构建在关闭 PAD 后进行。）

## 3. 端到端验证

环境：`PACK_LAUNCHER_ROOT=.tmp/pad-z-root`（隔离）、`PACK_AGENT_REPO=E:\pack-agent`、真实包 `pokemon-1.0.0.dspack`。

### 3.1 inspect

```powershell
pack-agent-for DSH.exe cli packforge inspect ...pokemon-1.0.0.dspack --out ...\inspect.json
# exit 0；inspect.json: type=profile, dshVersion=0.1.5-alpha.2, profileName=pokemon,
#                    layout.overrides=2, rootFiles=3
```

### 3.2 import（实例创建 + compat 安装）

```powershell
pack-agent-for DSH.exe cli packforge import ...pokemon-1.0.0.dspack --name pf-demo2 \
  --release 0.2.1-rc.2 --out ...\import2.json
# [PAD] instance pf-demo2 created → ...\instances\pf-demo2\home
# exit 0；import2.json 1380 bytes
```

落盘结果：

```
instances/pf-demo2/instance.json
instances/pf-demo2/home/profiles/pokemon/package.json   ← bundles = [dsh-base, dsh-web-app, dsh-wildmon, @hellosz/dsh-pets]
instances/pf-demo2/home/profiles/pokemon/.dshpkcfg
instances/pf-demo2/home/profiles/pokemon/cordis.patch.yml
```

### 3.3 真实依赖安装（`--deps`）

本机没有全局 `dsh`，用 shim 指向真实已装 DSH 0.2.1-rc.2 的 `bin.js`（node v24.12.0、pnpm 11.7.0）：

```powershell
# shim: dsh.cmd → node <…0.2.1-rc.2…>/node_modules/@deepseek-ai/dsh/lib/bin.js %*
bun run agent-pack-dsh/modpack/cli.ts packforge install deps-demo-1.0.0.dspack \
  --home .tmp/packforge-deps/home --apply --deps --json
# exit 0
# deps.planned = ["json5@2.2.3"]   deps.installed = ["json5@2.2.3"]
# dsh 实际调用记录：plugin --profile demo add json5@2.2.3
```

落盘结果（真实 pnpm 安装）：

```
home/profiles/demo/node_modules/json5/package.json   ← 存在
home/profiles/demo/pnpm-lock.yaml                    ← 生成
home/profiles/demo/package.json
  dsh.profile.bundles = ["@deepseek-ai/dsh-base"]
  dependencies = {"json5":"2.2.3"}
```

## 4. 边界

1. **PAD 窗口 UI 未接**：CLI 已通；把 `.dspack` 拖进窗口/列表按钮的 UI 流程是下一步。
2. **dshhome**：仍显式 PA151（M4）。
3. **真实网络安装只用单 npm 依赖验证**（json5）；github: commit 依赖、`files[]` 真实下载未跑。
4. **launcher registry PR** 未提交（材料已就绪）。
5. 构建产物：`.tmp/pad-build-z/`（正式 `bin/Debug` 需关闭正在运行的 PAD 后重建）。
