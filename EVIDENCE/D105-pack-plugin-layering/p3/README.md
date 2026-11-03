# D105 · P3 证据（allow-version 通道 / 冲突三选一 / 换内核重试）

date: 2026-11-04
scope: task-7（pad-ui）。全部在 scratch 里跑，未写 live launcher root、未重启 live PAD。

## 1) E2E：allow-version → dsh plugin add（真实 DSH 0.2.1-rc.2）

**成功路径（fresh DSH_HOME，DSH 自己初始化 profile）**
- 命令：
  - `dsh plugin --profile web allow-version dsh-find-skill@0.2.1 --dsh-version 0.2.1-rc.2 --accept-risk`
  - `dsh plugin version-exemptions`
  - `dsh plugin --profile web add dsh-find-skill`
  - `dsh --dump-config --profile web`
- 结果（文件见 `fresh-allow-version-e2e.txt` / `fresh-add-after-allow.log` / `fresh-add-e2e.txt` / `fresh-dump-config-after-add.txt`）：
  - allow-version exit=0，stderr 有 DSH 的风险警告，stdout：`dsh: allowed dsh-find-skill@0.2.1 for DSH 0.2.1-rc.2`
  - `profiles/web/compatibility.json` = `{"dsh-find-skill@0.2.1":["0.2.1-rc.2"]}`，`version-exemptions` 原样返回
  - `dsh plugin add dsh-find-skill` **exit=0（3.2s）**，`dependencies` += `dsh-find-skill: ^0.2.1`，`dsh.profile.bundles` 末尾 += `dsh-find-skill`，`node_modules/dsh-find-skill` 存在
  - `dsh --dump-config --profile web` 出现 dsh-find-skill 层（3 处匹配）

**负控（没有 allow-version，直接 add）**
- `nocompat-add-fails-with-hint.txt`：exit=1，输出含
  `dsh: to accept the risk, run: dsh plugin --profile web allow-version dsh-find-skill@0.2.1 --dsh-version 0.2.1-rc.2 --accept-risk`
  —— 与 `PackCompat` 的解析目标（pad.tests/PackCompatTests.cs 的 fixture）逐字一致。

**关于一份拷贝 home 的尝试（如实记录）**
- 先拷贝 verifier 的 p1-full home 做 E2E：allow-version 成功（`allow-version-e2e.txt`），但随后
  `dsh plugin add` 被 pnpm 拒绝：`ERR_PNPM_UNEXPECTED_VIRTUAL_STORE`（拷贝后的 node_modules 仍指着
  原路径的 virtual store；`add-after-allow.log`）。这不是 allow-version 通道的问题，改用 fresh home
  后完整通过（上面的成功路径）。PAD 自己的 PnpmHeal 会在真实实例里处理这种拷贝路径修复。

## 2) 截图：catalog 残留行过滤（P3 add-on，M2 发现）

fixture：`E:\pack-agent\.tmp\d105-p3\stale-root\.pack-launcher`（用 `pad cli instance create --pack`
建两个实例，再删掉 stale-demo 的投影目录；`pack list` 仍从 catalog DB 返回该行，dir 已不存在）。

- `instance-packs-stale-hidden.png`：stale-demo 的整合包列表**不再出现**这条残留行（目录已不在 + 账本
  无条目 → 列表里隐藏）。
- `instance-packs-unregistered-kept.png`：keep-demo（投影还在、无账本）仍显示「未登记」——证明过滤
  没有把老实例的合法提示一起吞掉。

## 3) 代码/测试

- 解析/API 单测：`agent-pack-dsh/pad.tests/PackCompatTests.cs`（提示行/`--dsh-version=`/warning 兜底/
  尾标点/`FromApplyResult`/`AllowVersionArgs`/`MarkInstanceStatus` 保 TS 字段）
- UI 接线断言：`agent-pack-dsh/modpack/pad-window.test.ts` 的 D105 P3 block
- build/tests 数字见任务报告。
