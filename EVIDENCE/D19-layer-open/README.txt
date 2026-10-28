D19 layer open — 系统结构与节点启动接上 (2026-10-23 00:33)

[1] pad cli layer list E:\tmp\pad-sys\qiuzhifangzhou
    empty    backend   home ...\layers\backend\dsh-home   running 0
    empty    collect   home ...\layers\collect\dsh-home   running 0
    empty    frontend  home ...\layers\frontend\dsh-home  running 0

[2] pad cli layer open <system> frontend --surface web --bin <bin.js> --node <node.cmd> --json
    Home     ...\layers\frontend\dsh-home
    Profile  frontend          <- 由 PAD 物化出来的，之前不存在
    Port     54694
    Url      http://127.0.0.1:54694/?token=3DlJPMAK...
    Verified true
    window   msedge --app

[3] layer list 再来一次
    profile  frontend  ...  running 1        <- hasProfile 从 empty 变 profile

[4] pad cli node close <layer home> --profile frontend
    closed frontend port 54694 pid 10356 (was alive)
    端口已释放；layer list -> running 0

关键手法：
  DSH 只对五个官方模板自动初始化 profile（web/headless/sdk/sdk-minimal/acp）。
  层用的是自定义名（collect/frontend/backend），所以 PAD 调
      dsh --profile <layer> --from-default-profile web --dump-config
  用 dump 模式把模板复制进目标 profile，**compose 但不 boot**，退出码即判据。
  实测：调用前 profiles\frontend\package.json 不存在，调用后存在，且全程没有启动任何服务。

一句闭环：
  system new -> layers/<layer>/dsh-home -> layer open -> 干活 -> node close
