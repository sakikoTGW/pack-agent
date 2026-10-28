D17 app surface — 验证 (2026-10-23 00:19)

[1] 壳在运行时启动 app 形态 -> 必须拒绝
    pad cli node open <home> --surface app --exe "E:\...\DSH Desktop.exe"
    error[PA086]: the desktop shell is already running
     --> C:\Users\Brs\AppData\Roaming\dsh-desktop
      = help: close the running DSH Desktop window, then retry
    exit: 非 0

[2] exe 路径不存在 -> 必须拒绝
    error[PA087]: no desktop shell executable at that path
     --> E:\nope\DSH Desktop.exe

判据：独占打开 %APPDATA%\dsh-desktop\lockfile（FileShare.None）。成功=没跑；IO 异常=在跑。
不数进程名，因为 Electron 会起一堆同名的 "DSH Desktop.exe"。

未验证的部分（诚实记录）：
  真正把壳拉起来的路径没有跑过。原因：要验证它，必须先把当前这个壳关掉，
  而当前壳正在服务本对话——关掉就等于杀掉验证者本身。
  代码路径本身很短：Process.Start(exe) -> 轮询 lockfile 独占 -> 记 pid。
  等哪天壳没在跑时，跑一次 pad cli node open <home> --surface app --exe <...> 即可补上。

语义边界（D17）：app 形态不能做项目隔离。壳自己决定 DSH_HOME，PAD 只能管生命周期。
"一个项目一个 DSH" 只能用 web / tui。
