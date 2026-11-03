# D107 真机闭环 · dsh-tauri 0.30.2 × PAD tauri surface

date: 2026-11-04
状态：**通过**。安装 + 数据全部落在 E 盘；C 盘只剩 junction（0 字节实体）。

## 装的是什么

| 项 | 值 |
|---|---|
| 上游 | `github.com/dsh-tauri/deepseek-harness-desktop` release `v0.30.2` |
| 安装包 | `Deepseek.Harness.Desktop_0.30.2_x64-setup.exe`（123,271,286 B，与 GitHub API 一致） |
| sha256 | `F53B4B79C06A25A2D3FA2D98A88212C42B828D5586E71F5A83064DD440F430C5` |
| 下载通道 | GitHub 直连超时 → `ghfast.top` 镜像（大小与 API 一致） |
| 安装 | `setup.exe /S /D=E:\DSH-Tauri`（NSIS 静默、装到 E） |

## 落盘位置（不在 C 盘）

| 内容 | 实际路径 | 大小 | C 盘上是什么 |
|---|---|---|---|
| 程序 | `E:\DSH-Tauri`（exe = `E:\DSH-Tauri\deepseek-harness-desktop.exe`） | 399.3 MB | 无 |
| 运行时/内核（Tauri 的 app data 根） | `E:\DSH-Tauri-data`（含 `dependencies\dsh` 149,754,699 B 内核 zip 解压） | 454.7 MB | `%APPDATA%\dsh-tauri` → **Junction** |
| WebView2 / 本地缓存 | `E:\DSH-Tauri-localdata` | 4.0 MB | `%LOCALAPPDATA%\dsh-tauri` → **Junction** |

> 说明：Tauri 的 app data 路径由程序按 identifier 写成 `%APPDATA%\<id>`，改不了；做法是**整目录搬到 E + 建目录 junction**，程序仍然看得见原路径，实体字节全在 E。`pad cli` / PAD 的实例文件全在 PAD 自己的 root，与此无关。
> 安装包里只有 20 MB 是 exe；`runtime\`（Electron Runtime）+ `resources\` 是大头；首启时它自己从 GitHub 下了 `deepseek-harness-pkg-windows.zip`（149 MB）解压到 `dependencies\dsh`。

## 闭环结果（scratch root：`.tmp/d107-tauri-real`）

| 步骤 | 预期 | 实际 | 证据 |
|---|---|---|---|
| `01-open` 拉起 | exit 0、账本记 tauri、进程活 | **exit 0，6.1s**，pid 72444 | `01-open.txt`、`02-ledger.json` |
| `DSH_HOME` 是否被当真 | 应用在指定 home 里初始化自己的 profile | ✅ home 下出现 `profiles/desktop`（带 dsh-tauri 系列插件）、`storages/workspace.json`、`.credentials.yaml` | `04-home-tree.txt` |
| 同 home 再 open | PA099（同 profile 已在跑） | ✅ PA099，0.5s，不重启 | `05-open-second.txt` |
| 另一个 home 再 open（单实例） | PA149（激活转发后退出，不出现第二扇窗） | ✅ PA149，0.9s，exit code 0 | `06b-open-other-home.txt` |
| 账本 | surface=tauri / pid / alive | ✅ | `06-list.txt` |
| `node close` | 杀壳进程树、删账本 | ✅ `closed tauri port 0 pid 72444 (was alive)`，之后 0 进程 | `07-close.txt`、`08-processes-after-close.txt` |
| close 后 list | 空 | ✅ | `09-list-after.txt` |

## 真机跑出来的两个 PAD 代码问题（已修，`Core/Nodes.cs`）

1. **stdio 继承导致 `pad cli node open` 挂 120 秒**：用普通 `Process.Start`（CreateProcess 继承句柄）拉起窗时，GUI 继承了 PAD 的 stdout 管道；PAD 退出后管道不 EOF，脚本侧要等窗关。改成 `UseShellExecute = true` 启动 exe（壳式启动，不继承 PAD 句柄），`DSH_HOME` 在调用期间用 `Environment.SetEnvironmentVariable` 临时设置——实测 open 从 120s 降到 **6.1s**。
2. **`File.AppendAllText` 写 tauri.log 失败**：最初用 `cmd > log` 重定向，cmd 独占日志文件，PAD 启动后再追加就 `being used by another process`。现在日志只写一行 PAD 侧启动记录（`File.WriteAllText`），壳自己的日志在它自己的 app data `logs\` 下。

## 边界（如实记）

- **两套内核管理并存**：这个壳自己管 DSH 内核/依赖/更新（`E:\DSH-Tauri-data\dependencies\dsh`），PAD 的版本库管的是 dsh CLI release（web/tui 面）。本次用 scratch home，PAD 实例文件未被它改动；但真用同一个 home 时，壳会按自己的内核启动——两者不是一套东西，文档里已写明。
- 单实例：同一时间只有一扇窗，第二个项目要等第一扇关（PA149 点名）。
- 没有会话级深链：上游只认 `dshtauri://open`，PAD 只声明 focus-clipboard。
- 应用首启会自己联网下载 149 MB 内核（本次已下到 E 盘）；断网首启会失败。

## 文件清单

`00-run.txt` 全流程转录 · `01–09` 每步 stdout/exit · `10-build.txt`（0 警告 0 错误）· `11-test-dotnet.txt`（219/219）· `12-test-tauri.txt`（两段绿）
