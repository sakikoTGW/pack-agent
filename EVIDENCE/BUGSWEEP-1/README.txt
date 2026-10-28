Bug sweep #1 (2026-10-23 01:41–01:44) — 边界输入与重复操作

依据：刚修的两个 bug（D21/D22）都是"东西放错位置"，所以这一轮专测
"输入不对 / 操作重复"时会发生什么，而不是数 grep 命中。

已修（都带实测）：
  B1  inspect <非 home>       -> 原先打印一排 0，看起来像合法 home
      修：Measure 用 profiles/ 或 storages/ 判断"是不是 home"；CLI 找不到就 PA098
      验：inspect E:\nope -> error[PA098] that is not a DSH home
          inspect <真 home> -> 正常输出

  B2  同一 home/profile 二次 open
      原先：账本按 profile 覆盖，第一个进程变成孤儿——还在跑、不再被记录、
            也无法通过 PAD 关闭
      修：OpenSurface 先查账本里该 profile 是否活着，是则 PA099 并给出关闭命令
      验：第二次 open -> error[PA099] that profile is already running ... pid 68644

  B3  layer list <不存在的系统>  -> 原先只打印路径，退出 0
      修：目录下没有 layers/ 就 PA100
      验：layer list E:\nope\system -> error[PA100] no such system

  B4  报错 help 指向不存在的命令
      PA051（system 重名）建议 pad cli system list，但当时没这个命令
      修：补上 pad cli system list [--root <dir>]
      验：system list --root E:\tmp\pad-sys -> qiuzhifangzhou  layers 3

已排除（看起来危险，实际是有意的）：
  App.xaml.cs:36   Task.Run(...).GetAwaiter().GetResult()   注释解释了为什么必须这样
  UiHang.cs        Thread.Sleep(6000/2000)                  卡死看门狗自托管对话框

待验（下一轮）：
  C1  显式 --port 指向已占用端口 -> 现在会等满 90 秒才失败，应当先检查端口
  C2  usage <非 home> 的报错会附带整段调用栈（PA070 本身正确）
  C3  layer open 时 profile 物化失败但目录已半建成 -> 状态可能不一致
