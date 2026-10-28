启动链三修 — 实测 (2026-10-23 02:32)

背景：点「启动 本机 DSH · web」卡在 86%「等待 Harness」。查日志：

  Error: dsh: cannot resolve profile bundle "@deepseek-ai/dsh-experimental-agent-team-profile"
  from the dsh installation or ...\dsh-desktop\harness\profiles\web
  [PAD] dsh exited 1

即：那个实例 adopted 了正在运行的桌面端 home，其 web profile 声明的是新版 DSH 的 bundle，
而 PAD 自带的 0.1.0-rc.7 没有它们 —— 用旧 DSH 启动新 home，必然起不来。
同时挖出三个真 bug。

B5 launch-*.cmd 用 UTF-8 BOM 写
  修前：首 3 字节 239,187,191；运行日志首行 '锘緻echo off' 不是内部或外部命令
  修法：按控制台 OEM 代码页写（本机 936），无 BOM
  验证：首 3 字节 64,101,99（@ec）；按 GBK 解码首行 "@echo off"、title "本机 DSH · web"

B6 等 Harness 时不看子进程死活
  修前：子进程已 exit 1，仍轮询到启动超时，卡片停在 86%
  修法：轮询期间读最新启动日志，发现 "[PAD] dsh exited with N" 立即抛 PA120 并附日志尾巴
  注：因为 B7 守卫现在会先拦住这个实例，PA120 这条路径本轮没有被真实触发，属实现完成、未实测

B7 在别人家的 home 上启动
  修前：脚本对 %DSH_HOME%\profiles\node_modules 做 rmdir、并 copy 覆盖 .credentials.yaml，
        而这次 DSH_HOME 指向桌面端正在使用的 home
  修法：Runner.Launch 先查 home 与父目录的 lockfile 是否被独占，是则 PA121 拒绝
  验证：pad cli run dsh web ->
    error[PA121]: that DSH home belongs to a running client
     --> C:\Users\Brs\AppData\Roaming\dsh-desktop\harness
      | lock held by C:\Users\Brs\AppData\Roaming\dsh-desktop\lockfile

未修（观察项）：日志里的中文在我这里读出来是乱码，是 PowerShell 按 ANSI 解码 UTF-8 文件所致，
文件本身正常；不改。
