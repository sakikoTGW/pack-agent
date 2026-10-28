本机 DSH 收录 — 实测 (2026-10-23 02:39)

命令：pad cli discover [--deep <root>] [--json] [--out <path>]

默认扫描（已知位置）：
  shells  1
    running  0.11.1   E:\新建文件夹 (5)\DSH Desktop\DSH Desktop.exe
              ← 自定义安装目录，按已知位置扫是 0 命中，靠"问运行中的进程"找到
  cli     2
              <app.asar>\...\@deepseek-ai\dsh\lib\bin.js       (pad.json#launch.dshCli)
    0.1.0-rc.7 <pad root>\versions\0.1.0-rc.7\node_modules\...\bin.js  (pad version store)
  homes   6
    running  [.generations,web]  C:\Users\Brs\AppData\Roaming\dsh-desktop\harness   (desktop home)
    idle     [desktop]           C:\Users\Brs\.dsh                                (~/.dsh)
    idle     [web]               C:\Users\Brs                                      (userprofile，profiles/ 在用户根)
    idle     [dsh-tui]           <pad root>\instances\123\home                     (pad instance)
    idle     [pad-op-tui]        <pad root>\instances\pad-op-clone\home            (pad instance)
    idle     [pad-op-tui]        <pad root>\instances\pad-op-tmp\home              (pad instance)

加 --deep E:\tmp 后：homes 6 -> 23
  额外收录 E:\tmp\dsh-probe-home（[collect,web]）与 E:\tmp\pack-agent\ 下十余个测试 home
  —— 也说明深度扫描会收进噪音，正式界面里需要按"有会话/有 profile 版本"再筛。

三个刻意的判断（D25）：
  · 桌面端安装位置不可枚举 -> 问运行中的进程
  · home 的运行状态不能只看 PAD 账本 -> 账本 pid 或父目录 lockfile
  · 全盘遍历必须显式给根，且有深度上限/跳过名单/20000 目录硬上限

已知缺口：app.asar 内的 CLI 读不到 package.json，版本号为空。
