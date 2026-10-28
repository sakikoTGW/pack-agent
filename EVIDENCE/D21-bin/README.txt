D21 dsh CLI 路径解析 — 实测 (2026-10-23 01:20)

解析顺序：--bin 参数 > pad.json#launch.dshCli > release 记录 bin > 报 PA094

[1] pad.json 里 launch.dshCli = <app.asar>\...\@deepseek-ai\dsh\lib\bin.js
    pad cli node open E:\tmp\dsh-probe-home --surface web --json      ← 没有 --bin
    -> Port 51578  Verified true  window msedge --app
    证据：asar 感知的 Exists() 让 asar 内的路径通过

[2] 清空 launch.dshCli 后再跑同一命令
    -> 立刻 error[PA094]: PAD does not know where the dsh CLI is
       | set launch.dshCli in pad.json, or pass --bin
    这是预期行为：不静默退化，点名要改哪个开关

[3] node close -> closed web port 51578 pid 41840 (was alive)，端口释放

过程中发现的一个坑（已修）：
  第一版解析链最后一层是 DshSniff.Bin()，它会 shell out（which / npm root -g）。
   实测让 node open 卡住 60 秒以上，日志里连命令头之后一行都没有。
   修法：把嗅探整个从启动路径里拿掉（D21）。找不到就报错，比卡住好。
   对照 PCL：它嗅探 Java 是一次性、有缓存、有进度的设置动作，不在每次启动里跑。

改动：
  Models.cs  LaunchPrefs.DshCli
  LaunchPolicy.Exists()   asar 感知的存在性判断
  Cli.ResolveBin()        三层解析，无嗅探
