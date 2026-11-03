# Q1 gate: “摘 bundles 留 deps” 会不会被 DSH 反过来加回？

date: 2026-11-04 · owner: p2-core（task-5 A）· 状态：**PASS**
设计依据：`agent-pack-dsh/docs/pack-plugin-layering.md` §6 P2 / §7 Q1。
机器可复跑：`powershell -NoProfile -ExecutionPolicy Bypass -File run-q1.ps1`（scratch root 自动重建）。

## 结论

**可以这么干，会保持。** 把一个带 `dsh.bundle` 的插件从 `dsh.profile.bundles` 摘掉、只保留在
`dependencies` 里之后，跑一次无关的 `dsh plugin add` 和一次无关的 `dsh plugin remove`
（两次都会走 `@deepseek-ai/dsh-plugin-manager` 的 `reconcile()`），该行**没有被加回**；
`dsh --dump-config --profile web` 里也不再出现该 bundle 的层。手工把行加回后 dump-config 恢复
（行数与 baseline 完全一致）。

所以 P2 的「停用包 = allow=false + 从 bundles 摘掉该包插件、dependencies 保留」可行：
- DSH 启动只按 `dsh.profile.bundles` 顺序 compose，不在列表里的依赖不会加载（dump-config 实测层消失）；
- `reconcile()` 只对**本次新增依赖**补 bundle（`beforeDeps.has(name)` 直接 continue），
  已有依赖不会因为还留在 `dependencies` 里就被补回；
- 重新启用不需要重新下载（依赖还在 profile 的 `node_modules`，只改 `dsh.profile.bundles`）。

## 环境

| 项 | 值 |
|---|---|
| 复测日期 | 2026-11-04 |
| DSH 版本 | `0.2.1-rc.2`（live version store，`versions/0.2.1-rc.2`） |
| dsh bin | `agent-pack-dsh/pad/bin/Debug/net9.0-windows/.pack-launcher/versions/0.2.1-rc.2/node_modules/@deepseek-ai/dsh/lib/bin.js` |
| scratch root | `E:\pack-agent\.tmp\q1-pack-ledger\.pack-launcher`（`versions` 是到 live `versions` 的 junction，只读使用） |
| 实例 | `instances/q1`（PAD 形状的 `instance.json` + 空 home；profile 由 DSH 自己 `initialized profile web` 建出） |
| 被测插件 | `@sakikotgw/pack-agent-dsh@^0.4.3`（声明 `dsh.bundle`） |
| 无关操作 | `dsh plugin --profile web add zod` / `remove zod`（`zod` 不声明 `dsh.bundle`，只当普通依赖） |
| live PAD / live launcher root | **未重启、未写入**（本机 live pid 78308 全程在跑） |

环境变量与 PAD 调 `dsh` 时一致：`DSH_HOME=<scratch home>`、`DSH_TELEMETRY_DISABLED=1`、
`npm_config_ignore_workspace_root_check=true`（同 `PluginOp`/`AddBundles` 的 env），cwd = 实例 workspace。

## 步骤与实测（摘自 `q1-run.log`）

### 1. DSH 自己建 profile 并装插件
```
dsh plugin --profile web add @sakikotgw/pack-agent-dsh      # exit=0
dsh: initialized profile web at ...\instances\q1\home\profiles\web
bundles: [@deepseek-ai/dsh-base, @deepseek-ai/dsh-web-app, @sakikotgw/pack-agent-dsh]
deps:    [@sakikotgw/pack-agent-dsh]
```

### 2. baseline `--dump-config`
`dsh --dump-config --profile web` → exit 0，1243 行，含 `# == @sakikotgw/pack-agent-dsh` 层
（`02-dump-baseline.yml`）。

### 3. 手工摘 bundles 留 deps（等价 PAD `Trial.WriteBundles` 的改法）
```
bundles: [@deepseek-ai/dsh-base, @deepseek-ai/dsh-web-app]
deps:    [@sakikotgw/pack-agent-dsh]      # 依赖保留
```

### 4. 无关 `dsh plugin add zod`（新依赖 → reconcile 跑一次）
```
+ zod ^4.6.7                                                 # exit=0
dsh: warning: zod declares no dsh.bundle — installed as a plain dependency, not a profile layer
bundles: [@deepseek-ai/dsh-base, @deepseek-ai/dsh-web-app]   # ← 被测行没被加回
deps:    [@sakikotgw/pack-agent-dsh, zod]
```

### 5. 无关 `dsh plugin remove zod`（reconcile 再跑一次）
```
- zod ^4.6.7                                                 # exit=0
bundles: [@deepseek-ai/dsh-base, @deepseek-ai/dsh-web-app]   # ← 仍然没被加回
deps:    [@sakikotgw/pack-agent-dsh]
```

### 6. 摘除后 `--dump-config`
exit 0，1229 行；`# == @sakikotgw/pack-agent-dsh` 层 **0 命中**（`06-dump-stripped.yml`）。
1229 < 1243：少了 14 行，就是 `pack-agent` 那个 bundle 的层。

### 7. 手工加回后再 `--dump-config`
exit 0，1243 行；该层重新出现，与 baseline 行数一致（`07-dump-restored.yml`）。

## 复跑输出（15/15 PASS）

```
PASS  profile web is created with dsh.profile.bundles
PASS  @sakikotgw/pack-agent-dsh is in dependencies
PASS  @sakikotgw/pack-agent-dsh is in bundles (baseline)
PASS  baseline dump-config carries the '@sakikotgw/pack-agent-dsh' layer
PASS  @sakikotgw/pack-agent-dsh still in dependencies
PASS  @sakikotgw/pack-agent-dsh no longer in bundles
PASS  unrelated add: @sakikotgw/pack-agent-dsh NOT re-added to bundles
PASS  unrelated add: @sakikotgw/pack-agent-dsh kept in dependencies
PASS  unrelated add: zod is a dependency now
PASS  unrelated remove: @sakikotgw/pack-agent-dsh NOT re-added to bundles
PASS  unrelated remove: zod is gone from dependencies
PASS  stripped dump-config has NO '@sakikotgw/pack-agent-dsh' layer
PASS  stripped dump is shorter than baseline
PASS  '@sakikotgw/pack-agent-dsh' layer is composed again
PASS  restored dump matches the baseline layer set
== RESULT: Q1 PASS - strip bundles / keep deps survives unrelated dsh plugin add + remove
```

## 文件

| 文件 | 内容 |
|---|---|
| `run-q1.ps1` | 一键复跑（重建 scratch root、三步断言、收集 dump） |
| `q1-run.log` | 本次完整控制台输出（含 15 条 PASS） |
| `01-add.out.txt` / `04-add-unrelated.out.txt` / `05-remove-unrelated.out.txt` | 三条 `dsh plugin` 命令的输出 |
| `02-dump-baseline.yml` / `06-dump-stripped.yml` / `07-dump-restored.yml` | 三次 `--dump-config` 全文（92 KB / 91 KB / 92 KB） |

## 风险 / 备注

- **升级回归项**：结论依赖 `@deepseek-ai/dsh-plugin-manager` 的 `reconcile()` 语义
  （`if (beforeDeps.has(name)) continue;` 只给本次新增依赖补 bundle）。DSH 升级时如果这条改了，
  “摘 bundles 停用”会被反向加回 —— 已在设计 §7 Q1 记录为升级回归清单项；P2 代码不要把这里当
  永久不变量，`SetPackPluginsActive` 的结果仍以读 `dsh.profile.bundles` 为准（读时对账）。
- `zod` 被选作“无关操作”包只是因为它稳定、极小、且不声明 `dsh.bundle`（连 pnpm 都不用重新下载）。
  换任何普通依赖包结论相同。
- profile 是 `dsh plugin add` 首次运行时由 DSH 自己 `initialized profile web` 建出的，没有手工伪造
  `package.json`；所以 `dsh.profile.bundles` 的初始内容就是 DSH 的模板值
  （`@deepseek-ai/dsh-base`、`@deepseek-ai/dsh-web-app`）。
