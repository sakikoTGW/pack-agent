D16 node close + ledger — 全流程实测 (2026-10-22 19:24)

[1] pad cli node open  E:\tmp\dsh-probe-home --bin <bin.js> --node <node.cmd> --json
    -> port 56028  pid 55484  url http://127.0.0.1:56028/?token=koObPx_8m...
    -> 写入 E:\tmp\dsh-probe-home\.pad\nodes.json

[2] pad cli node list E:\tmp\dsh-probe-home
    -> alive  web  port 56028  pid 55484  2026-10-22T19:24:07

[3] pad cli node close E:\tmp\dsh-probe-home --profile web
    -> closed web port 56028 pid 55484 (was alive)

[4] pad cli node list E:\tmp\dsh-probe-home
    -> no nodes recorded for that home
    -> 端口 56028 已不再监听
    -> nodes.json = {"version":1,"nodes":[]}

ledger 位置：<home>/.pad/nodes.json —— PAD 自己的账本，DSH 永远看不到它。
close 的顺序：先查账本、再杀进程树、最后删记录；找不到记录时报 PA081 而不是猜。
