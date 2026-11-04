# 热重载集成：实现与测试证据（2026-11-04）

方案见 [hot-reload-integration.md](../../agent-pack-dsh/docs/hot-reload-integration.md)。
本目录只放实现后的证据；能力探测的行为前提见 `EVIDENCE/hot-reload-spike/`。

## 1. 实现范围

| 文件 | 内容 |
|---|---|
| `pad/Core/HotCapability.cs` | 读盘能力判定（`launchSource` / `hmr` / `manager` / `hotAllowed`）+ `pad.hot-capability/v1` 缓存（TTL 10 min） |
| `pad/Core/Checkpoints.cs` | 最小 checkpoint（instance.json / profile package.json / cordis.patch.yml / pack-ledger / trials）、restore、undo 语义、环形保留 20 |
| `pad/Core/PluginVerify.cs` | V1–V4：`config-compiles` / `bundle-row` / `tools-inventory` / `runtime-pid`，输出 `pad.plugin-verify/v1`，退出码 0/3 |
| `pad/Core/HotReload.cs` | 默认冷、`--hot` 才驱动官方 HMR；settle（3 s 轮询、最多 15 s）、失败自动回滚、冷降级 |
| `pad/Core/Launcher.cs` | `PluginOp(..., bool hot = false)`：热路径不写 `restart-required`，cold 保持原行为 |
| `pad/Core/Cli.cs` | `plugin status` / `plugin verify` / `plugin undo`；`add/remove/update` 支持 `--hot`，并统一走 HotReload（保留冷路径退出码 0） |
| `modpack/registries/pa-codes.json` | PA201–PA205 |
| `pad.tests/HotReloadTests.cs` | 11 个单测（判定表、探测、checkpoint 往返、undo 语义、行匹配） |

## 2. 完成即测

```text
dotnet build agent-pack-dsh/pad/Pad.csproj -c Debug   -> 0 警告 0 错误
dotnet test  agent-pack-dsh/pad.tests/Pad.Tests.csproj -> 219/219 通过
```

单测覆盖的硬语义：

- `HotCapability.Classify` 8 个分支（pad/on → hot；config-only/off/absent/external/mygo/other → 冷）；
- 探测读盘：无运行 → `launchSource=none` + `hmr=absent`；装了 `dsh-mygo` → `manager=mygo`；PAD 账本有活 pid + dsh-hmr + patch `root` → `pad/on/hot=true`；patch `disabled: true` → `hmr=off`；
- checkpoint 往返（package.json + cordis.patch.yml 内容级还原）且 restore 会先留 `pre-restore`；
- `LatestBeforeChange` 跳过 `pre-restore`（否则 undo 会把刚回滚的状态又装回去——实测踩到过）；
- 行匹配按 bare name（`@acme/x@1.0.0` 与 `@acme/x` 视为同一行）。

## 3. CLI 冒烟（scratch root）

`PACK_LAUNCHER_ROOT=E:\pack-agent\.tmp\pad-hot-e2e\.pack-launcher`

```text
pad cli plugin status ev-hot --json   -> launch=none / hmr=absent / manager=none / hotAllowed=false
pad cli plugin undo ev-hot            -> error[PA203] no checkpoint to undo（exit 1）
```

## 4. 真机 E2E（同一台机器上的独立 dsh web，端口 3126）

环境：scratch home + `versions/0.2.1-rc.2`（junction 到独立 npm runtime），手动起 `dsh web --port 3126`，并按其真实 pid 写 PAD 账本（模拟 PAD 自己启动的 run）。

| 步骤 | 结果 | 证据 |
|---|---|---|
| `plugin status --json --refresh`（运行中） | `launchSource=pad, hmr=on, hotAllowed=true, reason=hot` | `cli-plugin-status.json` |
| `plugin add ev-hot web file:<local bundle> --hot` | 走完整热路径：checkpoint → `dsh plugin add` → settle → verify；`bundle-row` 失败（首次 local `file:` 安装 pnpm 写进 deps 但未 reconcile 进 bundles）→ **自动回滚**；dsh 进程全程存活、pid 未变 | `job-plugin-add.log`、`checkpoint-*.json` |
| 直接 `dsh plugin add file:<local bundle>` 后 `plugin verify` | `config-compiles pass / bundle-row pass`，exit 0 | 会话记录（`plugin verify` 输出） |
| `plugin undo`（修复后） | 恢复 pre-add 快照：bundles 回到 base+web-app；再 verify → `bundle-row fail`，exit 3 | checkpoint `20261103-212522-490` |

结论：

1. **默认冷 + 场景化热**在真机上跑通；能力判定全部来自读盘（运行账本 + profile 文件 + 安装包），没有运行时探针。
2. **失败静默确实存在**：`dsh plugin add` 成功（exit 0）但没产生 bundle 行时，只有 V2 能发现；这正是 verify 存在的理由。
3. **回滚可用**：热验证失败后 checkpoint 恢复，dsh 不受影响；undo 语义修正后能回到真正的"变更前"。

## 5. 已知环境边界（不是本功能的问题）

- `pad cli run` 用管道接子进程 stdout，CLI 退出后子进程会被写死（既有设计，`--shot/窗口` 路径才 detach）。E2E 因此改用"手动起的 dsh + 真实 pid 写账本"。
- 首次对 `file:` 本地包执行 `dsh plugin add` 时 pnpm 写入 dependencies 但官方 reconcile 未把行加进 bundles（第二次 add 才加）。PAD 的 verify 会把它判为失败并回滚——符合设计，但说明 **`--hot` 之后必须 verify，不能只看 dsh 退出码**。
- 本机还没有对 `--hot` 成功装官方 npm bundle 的样本；下次用真实插件市场包补一条 E1（热装成功）。

## 6. 产物

`cli-plugin-status.json`、`job-plugin-add.log`、`runtime-ledger.json`、`checkpoint-20261103-*.json`、`profile-cordis.patch.yml`、`dsh-web.out.log`。

## 7. UI（2026-11-04 落码后真机截图，含一次「能删就删」收敛）

`--shot --shot-page instance-plugins --shot-exit` 在真实 PAD 窗口渲染：

| 文件 | 状态 | 应看到 |
|---|---|---|
| `ui/00-before-green-bar.png` | 收敛前，hot-state mode=hot | ❌ 绿色条「已热装…无需重启」常驻 + 行药丸「已热装」——被用户否掉 |
| `ui/02-real-pad-pending.png` | mode=cold | ✅ 琥珀条「hot-bundle 待重启生效」+ 重启；行药丸「待重启」 |
| `ui/04-real-pad-hot-no-bar.png` | 收敛后，mode=hot | ✅ **不占屏**：无条、无药丸，行就是普通已装（成功只留 Toast） |
| `ui/03-real-pad-bar-only.png` | 无插件行 | 条正常显示（行列表为空时） |

结论（D109「能删就删」）：成功态不常驻——热/冷是实现细节，重启后药丸消失反而困惑用户；常驻只留给「待重启」（唯一需要人做决定的状态）。撤销退出常驻 UI，保留 CLI `plugin undo`。

构建/测试（收敛后）：`dotnet build` 0 警告 0 错误；`dotnet test` 219/219。
## 8. 插件行图标：官方 `icon.svg`（2026-11-04 修复）

用户指出「这个插件有图标」——不是热重载的问题，是插件行没读官方图标。

**三个退化点（都已修）**：

1. `LaunchPolicy.BundleIconFiles` 没有 `icon.svg`。官方插件（`dsh-experimental-*`）把图标放包根 `icon.svg`，PAD 只认 icon.png/logo.*，于是有图标的插件也回落 Monogram。
2. `BundleIcon.TryRasterizeSvg` 把根节点 `fill="none"` 当成“整棵子树不画”（官方 SVG 全是这种写法）→ 光栅成空白 png。现在 `fill="none"` 只表示本层不填，并支持 `<g transform>`、`fill="url(#grad)"` 渐变、`stroke` 描边、`fill/stdstroke-opacity`。
3. `using var fs` 未关流就读 `FileInfo.Length`（缓冲未 flush 时为 0）→ 把成功当失败。改成 `using (...) { enc.Save(fs); }` 后再检查。

**另外**：SVG 光栅结果写到 `library/plugins-meta/<spec>/icon.png` 缓存，不再往包的 node_modules 里写 `icon.png`。

证据：`ui/05-row-icon-before-blank.png`（修复前，图标位空白）→ `ui/05-row-official-icon-after.png`（修复后，显示 `dsh-experimental-auto-review` 自带的蓝色盾牌图标）。

回归测试：`pad.tests/BundleIconTests.cs`（2 例：官方样式 SVG 光栅后应有可见像素；Resolve 应返回缓存 png）。全量 221/221。