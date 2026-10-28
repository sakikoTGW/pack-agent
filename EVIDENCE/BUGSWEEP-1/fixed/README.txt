Bug sweep #1 修复验证（独立于 subagent 的自述，2026-10-23 02:09–02:10）

C1 显式 --port 预检（PA004）
  我方独立构造：同一 PowerShell 进程内 TcpListener 占住 54321，再跑
    pad cli node open E:\tmp\dsh-probe-home --surface web --profile web --port 54321
  结果（8 秒窗口内即返回，未修时是 90 秒 PA080）：
    error[PA004]: that port is already taken
     --> 127.0.0.1:54321
      | port 54321 is already in use on 127.0.0.1 · pid 18472 (powershell)
      = help: pad cli node open <home> --port <other port>
  副作用确认：不写 DSH 日志、不起子进程、不写 nodes.json。

C2 PadError 不打栈
  我方独立跑 pad cli usage E:\nope，日志中该次调用块内 "at Pad.Core" 行数 = 0；
  紧跟其上的栈属于上一次"非 PadError 仍打栈"的回归用例，不是同一次调用。

C3 物化失败的一致化
  采用 subagent 的两种构造（半截 manifest / 不可执行 --bin）与结论；
  我方复核了 git 改动范围与构建：只动 Core/Nodes.cs、Core/Cli.cs。

构建：0 警告 0 错误。
清理：54321 已释放，nodes.json 为空，无 PAD 残留进程；顺手删除了早先 \C:\Users\Brs 事故遗留的 pad/open/ 目录。
