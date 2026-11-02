# D106 · 实例 → PackForge `.dspack` 反向导出

日期：2026-11-02｜状态：**已实现并验证**（导入/导出双向闭环）

配套：[D101](../D101-packforge-adapter/README.md)～[D105](../D105-dshhome-m4-registry-pr/README.md)｜设计稿：[pack-v2-superset.md](../../agent-pack-dsh/docs/pack-v2-superset.md) §11

## 1. 功能

把 PAD 的一个**实例 home** 反向导出成 PackForge `.dspack`（v3 容器 + manifest v5），让 HDSL / PackForge GUI / official-desktop 等别的启动器也能装：

| 输入 | 输出形态 |
|---|---|
| 实例有 1 个 profile | `type:"profile"`：profile 根文件 → `overrides/`；全局 skill/preset/AGENTS.md → `home/` |
| 实例有多个 profile | `type:"dshhome"`：`overrides/profiles/<name>/` 逐 profile；home 级内容 → `overrides/` 根；`defaultProfile` 取实例 `lastProfile`（无效则第一个） |

- 依赖坐标必须精确（x.y.z / sha / `github:…#sha`），否则 **PA148** 拒绝导出（避免别家装不上）
- 容器写出后立刻用自家 `readDspack` 读回自检（类型必须一致）
- UI：实例页「导出 .dspack」按钮（SaveFileDialog）+ `pad cli packforge export-instance`

## 2. 实现

| 文件 | 内容 |
|---|---|
| `agent-pack-dsh/modpack/packforge-export.ts`（新） | `exportInstanceToDspack` / `isPinnedDependencyValue` / CLI helper |
| `agent-pack-dsh/modpack/packforge-export.test.ts`（新） | 5 组断言：pinned 判定、dshhome 导出、往返导入、单 profile、PA148 |
| `packforge.ts` CLI | 新增 `export-instance` 子命令（`--out/--profile/--name/--version/--dsh-version`） |
| `pad/Core/Packs.cs` | `PackforgeExportInstance(...)` 调 pack-runtime |
| `pad/Core/Cli.cs` | `pad cli packforge export-instance <home> --file <x.dspack> [--out json]` |
| `pad/Views/InstanceView.xaml(.cs)` | 「导出 .dspack」按钮 + `ExportPackforge_Click` |
| `modpack/pad-dspack-ui.test.ts` | 契约断言补导出接线 |

## 3. 安全 / 排除

- 不导出：`sessions/`、`storages/`、`logs/`、`workspace/`、`attachments/`、`.credentials.yaml`、`settings.yaml`、`launcher.patch.yml`、`pad-gateway.json`
- 不导出安装产物：`node_modules/`、`.plugin-manager/`、`package.json`、`pnpm-lock.yaml`、`.npmrc`
- home 级全局内容只收白名单：`skills/`、`.agent-presets/`、`data/`、`AGENTS.md` / `CLAUDE.md`（其他自定义目录暂不导出）

## 4. 验证

```powershell
bun run agent-pack-dsh/modpack/packforge-export.test.ts
# ✔ pinned predicate
# ✔ export dshhome: profiles/home content, sensitive+derived excluded
# ✔ export → import round-trip (dshhome)
# ✔ export single profile (profile form + home/)
# ✔ unpinned dependency → PA148
# ALL PACKFORGE EXPORT TESTS PASSED

bun run agent-pack-dsh/modpack/pad-dspack-ui.test.ts   # 契约（含导出按钮/命令）通过
dotnet build agent-pack-dsh/pad/Pad.csproj -c Debug -o .tmp/pad-build-z   # 0 error
```

真实实例端到端（`.tmp/pad-z-root/instances/ext-home`）：

```
TS:  export-instance … --out ext-home.dspack
     type=dshhome profiles=2 overrideFiles=4 homeFiles=3 dshVersion=0.2.1-rc.2
     inspect 读回：type=dshhome overrides=4
     重新 install 到新 home → 文件齐全（AGENTS.md / presets / skills / profile patch / package.json）
PAD:  pad cli packforge export-instance <home> --file pad-export.dspack --name pad-export
     exit 0；inspect 读回 type=dshhome；manifest v5 字段完整
```

## 5. 补充：范围依赖自动锁定（同日）

实例里的依赖若写成范围（`^1.2.0` / `latest` / `~1.0`），导出时不再直接 PA148：先从
`<profile>/node_modules/<dep>/package.json` 读**已安装的精确版本**，写进 manifest，并记一条 warning：

```
warnings: ["pinned dependency ranged: ^1.2.0 → 1.2.3（按实例里已装版本锁定）"]
```

锁不了（无 node_modules / github 非 commit / file: / workspace: / link:）才 PA148。
测试：`packforge-export.test.ts` 新增 `✔ range dependency auto-pinned from node_modules (1.2.3)`。

## 6. 边界

1. 导出**不含** workspace / sessions（工作区快照另立设计，见 MODPACK-STATE.md 的未决项）。
2. `files[]`（重内容指针）不参与导出：当前实例里没有该概念；大文件仍靠依赖安装。
3. 依赖只接受精确坐标；范围依赖会 PA148（提示先在实例里装成精确版本）。
4. 单 profile 导出时 home 级内容放 `home/`；PackForge 规范中 profile 形态的 `home/` 语义已按 v3 容器实现。
5. UI 按钮的人工点击未在无头环境验证；契约测试锁接线，功能本身由 PAD CLI 端到端覆盖。
