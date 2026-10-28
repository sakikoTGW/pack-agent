D16 tui surface — 实测 (2026-10-22 19:46)

cmd: pad cli node open E:\tmp\dsh-probe-home --surface tui --profile web --bin <bin.js> --node <node.cmd> --json

result:
  Surface  tui
  Port     0        (终端形态没有端点)
  Url      ""
  Pid      67236    ← 控制台进程 cmd.exe（不是 harness）
  window   terminal window

进程树实测：
  67236 cmd.exe
    └─ 13596 "DSH Desktop.exe" (ELECTRON_RUN_AS_NODE, --profile web)

close: pad cli node close <home> --profile web
  -> closed web port 0 pid 67236 (was alive)
  -> 67236 与 13596 均已退出；nodes.json = {"version":1,"nodes":[]}

关键 bug（已修）：
  Proc.FindDshPid 只查 Name='node.exe'，但桌面端的 node shim 起的是
  "DSH Desktop.exe"（Electron 的 node 模式），所以按 profile 查永远找不到，
  第一版 tui open 因此 45 秒后报 PA083。改为记录控制台 pid、close 时杀整棵树。

诚实的边界：
  1. 终端形态没有 web 那种 "dsh web: <url>" 握手，PAD 只能以「控制台存活 4 秒」
     作为就绪判据。启动慢但最终会失败的 profile，与正常启动无法区分。
  2. 本次测试用的是 --profile web 跑在 tui 形态下（为了绕开 tui 未初始化），
     验证的是「终端启动 + 进程树 + 账本 + 关闭」，不是真正的 TUI 交互。
  3. tui 不在官方五个自动初始化模板里（web/headless/sdk/sdk-minimal/acp），
     新 home 要先 dsh plugin --profile tui add @deepseek-harness-tui/dsh-tui。
