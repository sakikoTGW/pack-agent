# D63 · 项目绑定 agent preset（PA125）

用户原话：「接着做 agent preset 绑定（PA125）」。

项目轴要的不只是「工作区隔离」，还要「这个项目的对话默认用哪个 agent」。这一步把
`project.agents` 真正绑到实例上，并且**不改 DSH 一行代码**。

## 机制：写 DSH 自己的 profile patch 层

0.2.x 的 agent preset 是 DSH 自己的组合行（`preset-standard` / `preset-ptc` /
`preset-minimal` / `preset-cordis`），当前生效的是
`agent-preset-registry.config.default`。

DSH 给用户留的官方覆盖文件就是 **profile 自己的 patch 层**：
`<home>/profiles/<profile>/cordis.patch.yml`（文件头注释写着 "Your patch layer for this dsh
profile"）。PAD 只在里面追加一段带标记的块：

```yaml
# >>> pad project preset (managed) >>>
- id: agent-preset-registry
  config:
    default: minimal
# <<< pad project preset <<<
```

- 写前先剥掉旧的管理块（幂等），再按需去掉初始化时的 `[]` 占位；
- **用户/DSH 自己写的其它行原样保留**（下面 `patch-after.yml` 里 DSH 写的
  `llm-pi-ai` / `welcomeNoticeVersion` 都还在）；
- `--clear` 只删管理块；如果没有别的行了就补回 `[]`，patch 层仍然是合法 YAML 数组。

preset 列表和合法性不靠猜：PAD 跑 pin 住的那个发行号自己的
`node bin.js --profile <p> --dump-config`，解析 `- id: preset-*` 行（拿到 id/order）和
`agent-preset-registry.config.default`。读不到、或请求的 id 不在发行号里 → **PA125**。

## 实跑（真实本机 root，`ev-project`）

1. 绑定前：`defaultId: standard`，`bound: []`
   （`presets-before.json`，4 个 preset：standard/1、ptc/2、minimal/3、cordis/4）。
2. `pad cli project preset ev-project minimal` → `{ "preset": "minimal", "verified": true }`。
3. 用发行号自己验证：`dump-after-preset.yml` 里
   `agent-preset-registry.config.default` 已变成 **minimal**（dump exit 0）。
4. seed/run 会自动带上绑定：
   `project seed` 打印 `agent preset minimal → web`，`seed.json` 里 `"preset": "minimal"`；
   `project run` 的 `run.json` 里同样 `"preset": "minimal"`，窗口正常起来
   （`dsh-window-minimal.png`，侧栏仍然只有 ws-frontend / ws-backend）。
5. 反例：`project preset ev-project nope` →
   `error[PA125]: could not bind agent presets for this project`，
   help 指向 `pad cli project presets ev-project`。

## 验证

- `dotnet build` **0 warning / 0 error**（临时输出目录）；
- `pad-project.test.ts` 新增 preset 段全绿：
  - `[]` 占位 + 用户注释 → 写块、注释保留、`[]` 清掉；
  - 已有用户 patch 行 → 原行保留、块追加；
  - 幂等：连续 set 只留一个管理块；
  - `--clear` → 管理块移除、用户行保留；
  - 非法 id → PA125；没有发行号且不开 `--force` → PA125；
- 回归：`pad-window` / `pad-depth` 绿。

## 已知限制（合流时处理）

- `project run` 起的 web 进程和 CLI 共用管道，所以**从终端跑会一直占着终端**，直到窗口关闭
  （GUI 启动不受影响）。修法：detach 时用 `start /b` 让子进程拿到新控制台句柄，目前
  `Runner.LaunchWeb` 的 `detachWebOutput` 分支留着，等合流时统一验证后切换。
- 多个 preset 绑定（`agents` 多于 1 个）目前 = 校验全部 + **第一个作默认**；选择器 UI 留 P2。
- 用户在 DSH 设置里手动改默认 preset，会在下次 `project seed/run` 时被项目绑定覆盖——
  这是「项目优先」的预期；不想被覆盖就 `project preset <id> --clear`。

## 产物

| 文件 | 说明 |
|---|---|
| `presets-before.json` | 绑定前：4 个 preset + default=standard + bound=[] |
| `set.json` | `project preset ev-project minimal` 的结果（verified） |
| `patch-after.yml` | profile patch 层：DSH 自己的行 + PAD 管理块 |
| `dump-after-preset.yml` | 发行号 `--dump-config` 验证：default=minimal |
| `run.json` | `project run`：preset=minimal、新 URL、窗口起来 |
| `dsh-window-minimal.png` | 绑定后窗口截图（工作区仍只有项目的两个） |
