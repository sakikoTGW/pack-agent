D17 app surface — 第二版结论：不可分家 (2026-10-23 00:25)

尝试：让每个项目一个桌面壳，各用自己的 userData。
  期望：home = <userData>\harness（依据 harness.log 里 DSH_HOME 与 userData 同源）
  做法：DSH Desktop.exe --user-data-dir <项目目录>

实测：
  --version                          -> exit 0        （说明正常启动没问题）
  --user-data-dir <新目录>            -> exit 9        目标目录未创建
  pad cli node open --surface app    -> PA089 the desktop shell exited immediately (exit code 9)

asar 全文扫描（281,491,121 字节）：
  requestSingleInstanceLock  找到
    原文: const singleInstance = app.requestSingleInstanceLock();
          if (!singleInstance) { console.warn("[desktop] Another instance is already running;
          focusing existing window and exiting."); app.quit(); }
  --user-data-dir            未出现（0 次）

结论：
  壳用 Electron 标准单实例锁；asar 里没有任何 user-data-dir 的处理。
  把 userData 定死之后，壳的 home 也就定死了 —— 换 home 只能换壳，而壳不可多开。
  所以"每个项目一个桌面壳"这条路走不通。

可用路径（已实测）：
  web 形态 —— PAD 分配 --port、每项目一份 DSH_HOME、Edge --app 独立窗口。
  这才是"一个项目一个 DSH"。

app 形态的最终定位：
  日常桌面端入口 + 壳在跑时明确拒绝（PA086）。不承担隔离。
