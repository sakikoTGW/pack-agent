D16 node open — 实测 (2026-10-22 16:49 / 16:54)

cmd: pad cli node open E:\tmp\dsh-probe-home --bin <app.asar>\...\dsh\lib\bin.js --node <.desktop-bin\node.cmd> --json

result (second run):
  Home        E:\tmp\dsh-probe-home
  Profile     web
  Port        62492            allocated by PAD
  Url         http://127.0.0.1:62492/?token=sa0fmN0...   read from the announcement line
  Pid         53576
  LogPath     E:\tmp\dsh-probe-home\.pad\logs\web-62492.log
  window      msedge --app

PERSISTENCE: port 62492 was still listening 30s after PAD exited -> the instance is
detached on purpose. PAD owns the window, DSH owns the endpoint (D16).

two bugs this run found and fixed:
  1. positional arg list included the subcommand, so Home became "open"
  2. child stdout as a pipe: closing PAD killed the instance on its next log write.
     Now stdout is a file (cmd.exe redirect) and PAD tails it.

note: Start-Process -Wait from the shell hangs on this command because the detached
grandchild inherits handles. That is a harness artifact, not a PAD hang — PAD had
already exited and written its result.
