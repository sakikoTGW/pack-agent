# 热重载 spike：live `cordis.patch.yml` 能不能热挂/热卸（E1–E3）

2026-11-04 · 结论：**能，而且不用改 DSH 一行、不用 in-process 管理器。**

## 环境

| 项 | 值 |
|---|---|
| DSH | `@deepseek-ai/dsh@0.2.1-rc.2`（npm 独立 runtime，未用桌面壳） |
| Home | `E:\pack-agent\.tmp\lab-e2e\home`（scratch，`profiles/node_modules` → runtime 的 junction 作安装回退） |
| Profile | `web` = `@deepseek-ai/dsh-base` + `@deepseek-ai/dsh-web-app` |
| 实例 | `node lib/bin.js --profile web --port 3099 --no-open`，PID **70744**，全程未重启 |
| 试验插件 | `hot-e2e`（apply 写 marker + `ctx.effect` 清理）、`hot-e2e-bad`（apply 先写文件再抛错） |

## 结果

| # | 实验 | 操作 | 结果 | 判定 |
|---|---|---|---|---|
| E1 | 热挂载 | 往 live `profiles/web/cordis.patch.yml` 写 `- insert: [{id: hot-e2e, name: hot-e2e}]` | marker 出现（时间戳 `2026-11-03T20:33:41Z`）、stdout 出 `[hot-e2e] apply called`、**PID/启动时间不变** | ✅ |
| E2 | 热卸载 | patch 写回 `[]` | 插件 disposer 执行 → marker 被删除；进程存活、PID 不变 | ✅ |
| E3 | apply 抛错 | 只插 `hot-e2e-bad` | `bad-applied.txt` 生成（证明 apply 确实执行并抛错）；进程存活、HTTP 仍响应；**stdout/stderr 没有任何报错** | ✅（失败静默） |
| E3b | 混合插入原子性 | 同一个 patch 里同时插 good + bad | good 挂上了（marker=True）、bad 失败；**不是全有或全无** | ⚠️ 宿主不做事务 |
| E3c | 失败后重试 | 失败后改插 good | 仍能挂上（第二次 apply 日志） | ✅ 树没有被污染 |
| 时延 | 热挂延迟 | 写 patch → apply 执行 | **≈ 2.9 s**（100 ms 轮询，含 HMR 去抖） | 记录 |

> 说明：`http://127.0.0.1:3099/` 返回 401 是正常现象（web 需要 token），401 只用于证明**服务进程还活着**；token URL 在 `dsh.out.log`，属 scratch 实例，不入库。

## 对 PAD 的含义

1. **热重载不必 in-process。** DSH 每个长寿命 surface 都挂了 `watchUserPatches`（`profiles/<p>/cordis.patch.yml` + home 层 patch），文件一变就走 `hmr.registerConfig` → `include.update({patches})`；patch 支持 `insert`，loader 按 `id` 只挂载/卸载变化的部分（官方教程 `docs/cordis-tutorial/06-composition-and-hmr.zh.md` 也写了 "编辑 cordis.yml 本身也会触发更新"）。
2. **宿主不提供事务。** 一次 update 里多行各自成败，失败的行不会回滚已成功行（E3b），而且**失败不写 stdout/stderr**（E3）。所以 PAD 必须自己做两件事：
   - 写 patch 之后**行为验证**（插件声明的服务/工具是否出现），不能靠日志；
   - 验证失败**自己撤回**（把该行从 patch 里删掉，E2 证明可行）。
3. **能热的是 patch 注入的插件。** `dsh.profile.bundles` 是 boot 期合成的 base 层，改它仍需重启；web surface 的 HMR 是 `root: []` 的 watch-only 实例，**代码级换版本也不会热更**。
4. 因此 PAD 的能力边界应表述为：**装/卸/改配置热生效；换版本、改 bundles、换内核 → 需要重启**（并给"重启 2 秒、会话不丢"的提示）。

## 复现

```powershell
# 1) 装独立 runtime
npm i --prefix .tmp\lab-e2e\runtime @deepseek-ai/dsh@0.2.1-rc.2

# 2) scratch home + profile（bundles 走 profiles/node_modules 回退）
#    .tmp\lab-e2e\home\profiles\web\{package.json,cordis.yml,cordis.patch.yml,pnpm-workspace.yaml}

# 3) 启动
$env:DSH_HOME = "E:\pack-agent\.tmp\lab-e2e\home"
node .tmp\lab-e2e\runtime\node_modules\@deepseek-ai\dsh\lib\bin.js --profile web --port 3099 --no-open

# 4) 热挂：直接改 live patch
#    - insert:
#        - id: hot-e2e
#          name: hot-e2e
```

## 未验证 / 下一步

- `disabled: true` 能否热禁用**已有 bundles 层条目**（patch 覆盖语义，未测）。
- 插件注册的 **tool / service 在运行中是否真的对 agent 可见**（本 spike 只用 marker 证明 apply/disposer）。
- 桌面壳（Electron 长寿命 surface）下 patch 热重载是否同样生效（本次是独立 `web`）。
- 失败时是否需要注入 PAD 探针插件来判定（因为宿主不报错）。

---

## 第二轮：官方 `@deepseek-ai/dsh-hmr` 的能力验证（2026-11-04）

### 结论

**DSH 自带的热重载比 patch 层更成熟，而且连 `dsh.profile.bundles` 的增删都能热生效。**

`@deepseek-ai/dsh-hmr@0.2.1-rc.2`（MIT，first-party，README 15 KB）——"Coordinated module and profile configuration hot reload"：模块替换、Include 刷新与 profile 配置变更共用一个协调队列，包安装在该队列之外。

### 实测（scratch web 实例，PID 68520，04:38:49 启动，全程未重启）

| # | 动作 | 结果 |
|---|---|---|
| R1 | 热挂探针插件读宿主服务 | `hmr=true profileContext=true appReady=true loader=true pluginPackages=true` → 官方 HMR 在普通 `dsh` CLI 实例里是激活的 |
| R2 | 往 live `profiles/web/package.json` 的 `dsh.profile.bundles` 加一个**真 bundle**（`dsh.bundle.patch` 声明） | 新 bundle 的 patch 被热组合并挂载：`[hot-bundle-probe] mounted via bundle layer`，PID 不变 |
| R3 | 从 bundles 列表移除它 | 进程存活、PID 不变 |
| R4 | 再加回来（新代码带 disposer） | 重新挂载：`mounted (with disposer)` |
| R5 | 再次移除 | disposer 执行、`bundle-marker.txt` 被删 |

> 第一次测的时候失败，是因为测试包**没有 `dsh.bundle.patch` 声明**——不是 bundle 就不会被 bundle 层组合。这个坑值得记：`dsh plugin add` 装进来的包必须按官方 bundle 契约声明，否则加进 bundles 也不会挂。

### PAD 必须处理的三条官方边界

1. **失败静默**（E3）：apply 抛错时 stdout/stderr 什么都没有——验证必须靠行为探针，不能看日志。
2. **非原子**（E3b）：同一 patch 里 good+bad，good 会挂上、bad 失败——事务由 PAD 自己做（删行/回滚）。
3. **替换失败旧版本不恢复**（官方 README 明说）：旧代码不再运行，直到文件或依赖被修正——所以 PAD 的 checkpoint 是必需品，不是锦上添花。

### 官方列出的"需要重启"场景（PAD 的 `restart-required` 判据来源）

框架依赖变化（`loader.exit()`）、打包 worker loader、被移动的保留 profile 映射（`restartReason: 'package-moved'`）、Worker/TSX loader 线程各自的模块缓存、以及 `root: []`（默认）时的插件源码变化。