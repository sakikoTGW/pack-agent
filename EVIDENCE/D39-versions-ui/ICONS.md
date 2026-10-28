# D40 · 容器图标与命令行入口说明（用户反馈）

日期：2026-10-28 凌晨 · 相关：DECISIONS.md D40

用户看「容器」列表：*"这几个都有啥区别。哦对了，官方的桌面端用官方的小黑鲸，非官方用他们自己的图。"*

## 图标：用壳自己的 resources\icon.png

- 官方桌面端 `E:\DSH-App\resources\icon.png` → 官方小黑鲸（深色鲸鱼、白底圆角）；
- 第三方 `E:\新建文件夹 (5)\DSH Desktop\resources\icon.png` → 它自己的黑白 dashboard 鲸鱼 logo；
- 实现：`ProfileVm.IconPath`（虚拟属性）+ `DetectedShellRow` 取 `<exe 目录>\resources\icon.png`（兜底 `icon.ico`/`tray.ico`）；
  `InstMark.Paint()` 最前面支持「行自带图标」，不会再去走兜底蓝鲸；
- 命令行入口行按「bin 属于哪个壳的安装目录」继承那个壳的图标（官方两个入口带黑鲸、设置里那个带第三方 logo），
  PAD 版本库那条没有所属壳 → 兜底 DeepSeek 蓝鲸。

## 名字/副标题：把人话说清楚

- 桌面端行：行首类型从「桌面端」细分为 **官方桌面端 / 第三方桌面端**（`FileVersionInfo.ProductName` 含 DeepSeek = 官方）；
- 命令行 4 条不再都叫 `dsh <版本>`：
  - `DeepSeek Harness 自带的 dsh` — `内核 0.2.1-rc.2 · DeepSeek Harness app.asar 内自带 · 未跑`
  - `DeepSeek Harness 装的 dsh 命令` — `runtime\cli\bin\dsh.cmd`，跑的是桌面端宿主 CLI
  - `PAD 版本库 dsh` — PAD 下载的发行号（PAD 实例/改钉用）
  - `设置指定的 dsh CLI` — `pad.json#launch.dshCli`，当前指向第三方 DSH Desktop 的 asar（0.2.0-rc.2）
- 每行仍带 `在跑/未跑` 与 pid（来自 PEB 现场）。

## 验证

- `dotnet build` 0 warning / 0 error；PAD 已重启。
- 实拍：`02-containers.png`（官方黑鲸 + 第三方自家 logo + 四条命令行入口说明）。
