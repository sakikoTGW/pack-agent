# D107「安装整合包」验证（verifier）

状态：**M5 = PASS；已部署上线（新 pid 77272，2026-11-04 05:22，部署块见 SUMMARY/verify/deploy/），task-13 完成。**
- 机器摘要：`verify/SUMMARY.txt`
- 冻结源码：HEAD 54cf34ca…；diff_sha_full `c7a31a3a…`、diff_sha_product `fec33eef…`（全程稳定）
- 新 build：`.tmp\pad-build-d107`（dll sha `9AFFBE6E…`）；build 0/0；dotnet test **203/203**；6/6 bun
- CLI E2E（`verify/cli/`）：preflight Test=8 ok/1 需授权/1 缺失；dry-run 无落盘；无 flag=exit2 无落盘；
  `--accept-risk --skip-missing`=exit0/213.6s/State=incomplete/Added=9/Exempted=dsh-find-skill@0.2.1/
  Skipped=@yuxianglin/dsh-bridge-browser/Failures=0 + ledger(10 插件记录/state incomplete) + dump-config 层 hits=10；
  兼容包 install=installed；人类可读一行摘要；pack/instance list 回归 exit0。
- UI（`verify/ui/`，全部 EXIT0）：versions「安装 ▾」；instance-packs 已装包发光；pack-picker 徽标
  （1 需授权/1 已下架 + 可装）；pack-preflight 阶段1（8/1/1、内核匹配通过、版本冲突无、两个勾选）；
  阶段2 报告（9 新增/0 保留/1 豁免/1 跳过/0 失败，已装到 inst-empty、不完整·缺1、账本已写入）。
  与原型对照：布局/信息一致；允许差异（picker 未显式「8 个可装」徽标、按钮文案「按勾选开始安装」、offshot 不展开菜单）。
- 回归：D104 灯=0（scratch 无 PAD 启动）；D106 源码断言（无装到哪文字/IconPath/glow Forever+Enabled）全 PASS。
- 完整性：live PAD pid 75696 未动；live root 未写；无 commit。
- 部署（等 deploy-go）：产物 `.tmp\pad-build-d107\*` → 覆盖 `.tmp\pad-build-z\`（保留 .pack-launcher 等）→
  停 75696 → `cmd /c run-pad-new.cmd`（不 pipe 捕获）→ 新 pid / crash-log 无 delta / settings 哈希不变 /
  sisyphus web 61272 alive / live `--shot-page versions` 渲染正常；证据 `verify/deploy/`。
