D22 写设置不得触发依赖修复 — 实测 (2026-10-23 01:24–01:27)

现象（修前）：
  experimental enable  -> 慢几秒，且把 PnpmHeal 的非 PadError 异常打到 CLI
                          用户在"设置已生效"的同时看到一坨栈
  rewrite-scripts      -> Wait-Process 90 秒超时仍未退出，日志只有命令头

根因链：
  SaveSettings -> RewriteLaunchScripts -> Runner.WriteScript
                                      -> PnpmHeal.Relink(version node_modules)
                                      -> PnpmHeal.Relink(每个 profile 的 node_modules)
  写一个文本文件，触发了整个依赖图的 junction 重建。

修法：
  1. SaveSettings 不再调 RewriteLaunchScripts（只写 pad.json）
  2. RewriteLaunchScripts 的 catch 放宽到所有异常（尽力而为）
  3. Runner.WriteScript(heal:false) —— 重写脚本不复核依赖；heal 只在真正启动前保留

实测（修后）：
  experimental disable 1.01s  exited=True
  experimental enable  0.89s  exited=True
  rewrite-scripts      1.38s  exited=True   -> "rewrote"
  （修前 rewrite-scripts >90s 未完成）

对照 PAD 自己的教训（D21）：低频修复动作不能挂在高频路径上。
