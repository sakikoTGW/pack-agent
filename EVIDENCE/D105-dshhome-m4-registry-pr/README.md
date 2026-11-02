# D105 · dshhome（M4）安装 + launcher registry PR 材料

日期：2026-11-02｜状态：**已实现并验证**（M4）；PR **已提交** ✅

- registry 表行：https://github.com/DSH-PackForge/DSH-PackForge/pull/4
- 市场 JSON：https://github.com/DSH-PackForge/dsh-pack-market/pull/2

配套：[D101](../D101-packforge-adapter/README.md)、[D102](../D102-packforge-compat-market/README.md)、[D103](../D103-pad-packforge-wiring/README.md)、[D104](../D104-packforge-ui-net/README.md)｜设计稿：[pack-v2-superset.md](../../agent-pack-dsh/docs/pack-v2-superset.md) §11

## 1. dshhome（M4）安装

### 实现

`agent-pack-dsh/modpack/packforge-ext.ts`：

- `planCompatInstall` 支持 `type:"dshhome"`：
  - 校验 profiles 非空、defaultProfile 存在、目标 profile 目录非空 → PA150
  - 逐 profile 计划 `{name, profileDir, bundles, deps}`
  - `presets`（manifest.presets 的 path，缺省 `.agent-presets/<name>`）
  - `skills[]`：带 urls 的“重技能”入口复用 files[] 下载；小技能随 `overrides/skills/` 落盘
  - `instructions`（缺省 `AGENTS.md`）
- `applyCompatInstall` dshhome 分支：
  - `overrides/profiles/<name>/` → 对应 profile 目录；其余 `overrides/` → home 根（`.agent-presets/`、`skills/`、`AGENTS.md`、`data/`）
  - 每个 profile 写 `dsh.profile.bundles` 到自己的 `package.json`（保留已有字段）
  - 逐 profile 依赖安装（`runDep(spec, profile)`；`dsh plugin add` 带 `DSH_HOME`）
  - `defaultProfile` 由 PAD 侧作为实例默认启动 profile（`CreateInstance(..., profile)`）
- C# `PacksForge`：`Inspect` 接受 dshhome；`Import/ImportAuto` 用 `defaultProfile` 当实例 profile；`PA151` 现在只表示不支持的类型（如 collection）。

### 验证

```
bun run agent-pack-dsh/modpack/packforge-ext.test.ts
  ✔ dshhome plan (2 profiles / default / presets / instructions)
  ✔ dshhome apply (profiles/home root/presets/skills/per-profile deps)
  → ALL PACKFORGE EXT TESTS PASSED（12 项）
```

夹具落盘（CLI `pad packforge install ext-home.dspack --home … --apply`）：

```
copiedOverrides=4
AGENTS.md
.agent-presets/coding/agent.cordis.yml
skills/whale/SKILL.md
profiles/dev/cordis.patch.yml
profiles/dev/package.json      dsh.profile.bundles=["@deepseek-ai/dsh-base"]
profiles/writer/package.json
defaultProfile=dev  presets=[".agent-presets/coding"]  instructions=AGENTS.md
```

PAD 端到端（`pad cli packforge import-auto ext-home.dspack`，隔离 launcher root）：

```
[PAD] instance ext-home created → …\instances\ext-home\home
Instance=ext-home  Release=0.2.1-rc.2  Profile=dev（来自 defaultProfile）
instance.json: lastProfile="dev"；home 内 AGENTS.md / presets / skills / profiles 全部落盘
```

## 1.5 dshhome 多 profile 真实网络依赖（补充验证）

夹具 `dshhome-net-1.0.0.dspack`：`dev` profile 依赖 `json5@2.2.3`，`writer` profile 依赖 `is-number@7.0.0`（均为公开 npm 包）。

```powershell
bun run agent-pack-dsh/modpack/cli.ts packforge install dshhome-net-1.0.0.dspack \
  --home .tmp/packforge-dshhome-net/home --apply --deps --json
# exit 0，15s
# dsh 实际调用（shim → 真实 DSH 0.2.1-rc.2）：
#   plugin --profile dev    add json5@2.2.3
#   plugin --profile writer add is-number@7.0.0
```

落盘（两个 profile 相互独立）：

```
profiles/dev/node_modules/json5/package.json      ✅   profiles/dev/pnpm-lock.yaml    ✅
profiles/writer/node_modules/is-number/package.json ✅  profiles/writer/pnpm-lock.yaml ✅
dev package.json    dependencies={"json5":"2.2.3"}
writer package.json dependencies={"is-number":"7.0.0"}
```

PAD 端同样跑通（`pad cli packforge import-auto … --deps`，隔离 launcher root）：

```
[PAD] instance dshhome-net created → …\instances\dshhome-net\home
dsh calls: plugin --profile dev add json5@2.2.3 / plugin --profile writer add is-number@7.0.0
instance home：dev/json5 ✅  writer/is-number ✅   DepsInstalled=true
```

## 2. launcher registry PR 材料

目录：`agent-pack-dsh/packforge-registry-pr/`

| 文件 | 内容 |
|---|---|
| `0001-registry-claim-pad.patch` | `DSH-PackForge/DSH-PackForge` 的 `specs/launcher-registry.md` 加 `pad` 行 |
| `0002-market-launchers-add-pad.patch` | `DSH-PackForge/dsh-pack-market` 的 `index/launchers.json` + `web/launchers.json` 加 `pad` 记录 |
| `PR.md` | 两个仓库各自的提交命令、PR 标题/正文 |

验证：两个 patch 均在原始文件上 `git apply --check` 通过；patch 为 UTF-8、LF。`packforge registry` 输出里的 `padClaim.row` 与 0001 行一致。

### 已提交 PR

| PR | 内容 | 状态 |
|---|---|---|
| https://github.com/DSH-PackForge/DSH-PackForge/pull/4 | `specs/launcher-registry.md` +1 行（`pad`） | open / mergeable |
| https://github.com/DSH-PackForge/dsh-pack-market/pull/2 | `index/launchers.json` + `web/launchers.json` 各 +7 行 | open / mergeable |

提交方式：fork 两个仓库 → 分支 `registry/claim-pad` / `market/launchers-add-pad` → `git apply` 本目录 patch → 用 GitHub API 建 PR。若维护者脚本会从 registry 表生成 `launchers.json`，可能只需合并 #4。

## 3. 边界

1. **PAD 窗口拖拽**：接线与契约测试已过，但无头环境未人工拖动（见下节手动清单）。
2. dshhome 的**真实网络依赖安装已补充验证**：两个 profile 各自 `dsh plugin add` + pnpm 真装（见 §1.5），CLI 与 PAD import-auto 都通过。
3. registry PR 已提交（#4 / #2），等待维护者 review/merge；合并后 `packforge registry` 的 `padClaim.present` 应为 true。
4. `pad cli version` 在 Debug 构建返回 0.1.0（正式 release 由 `build-release` 注入版本号）。

## 4. 手动验证清单（拖拽 .dspack）

1. 关掉正在运行的 PAD（避免 exe 被锁），`dotnet build agent-pack-dsh/pad/Pad.csproj -c Debug`（或 `-o` 到临时目录）
2. 准备一个已装发行号（下载页装好），把 `.dspack` 拖到 PAD 窗口
3. 期望：Toast「正在装 xxx」→ 自动建实例（重名自动 `-2`）→ 跳「版本设置」→ Toast「装好了：name（DSH 版本）」
4. 版本页 →「装整合包…」→ 选 `.dspack`，同样走 compat 引擎
5. 失败时 Toast 显示 `error[PAxxx]`（如发行号未装 → `PA001`，带安装提示）
