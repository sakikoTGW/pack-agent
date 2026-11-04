# pad-cli

PAD 的跨平台命令行版本：同一套 Core，跑在 macOS / Linux 上。
Windows 上也能编、能跑，用来对比 PAD 窗口版的行为。

## 状态

| 部分 | 状态 |
|---|---|
| `Pad.Core` 可移植层 | 已完成（104 个文件，`net9.0`） |
| 平台原语 `IPlatform` | 完成：链接（junction/symlink）、终端（cmd / Terminal.app）、进程表（PEB / `ps`）、控制台附着 |
| `Runner`（起 profile） | 完成：Windows `.cmd` 与 POSIX `.sh` 两条脚本生成；web 分离/前台启动都走平台 shell |
| `Nodes`（起 node 进程/终端） | 完成：dump-config、web、terminal 三处都按平台分支 |
| `Proc`（找/停进程） | 完成：非 Windows 用 `ps`；`FocusClipboard` 的 user32 调用已按平台短路 |
| `Discover`（扫描安装） | 完成：macOS/Linux 走 `DiscoverPosix`（PATH、全局 node_modules、/Applications/.app、常见 DSH_HOME、深扫根） |
| `PnpmHeal`（链接重建） | 代码已走 `IPlatform`（junction/symlink）；macOS 布局**待真机验证** |
| 平台契约测试 | `tests/Pad.Core.Tests`：junction/symlink、`NeedsHeal`、`DiscoverPosix`；Windows 已通过，macos/ubuntu 由 CI 跑 |
| CI | `.github/workflows/pad-cli.yml`：windows + macos + ubuntu 构建/单测/冒烟；手动 `macos-e2e` job 跑 install → instance → script |
| Windows 端到端 | `release install`（pnpm add + `node bin.js --version`）、`instance create`、`profile add`、`run` → `ps` → `stop` 全链路已实测通过 |
| `portable-export` | 仍按 Windows 便携包生成 `.cmd` 启动脚本；mac 侧暂不适用 |
| Linux 实机验证（WSL Ubuntu） | `selftest` 全绿；`release install 0.2.1-rc.5` 通过（pnpm + `node bin.js --version`）；`instance create`；`script` 生成 POSIX `.sh`（`export DSH_HOME` / `cd` / `exec node bin.js`）；`ps` / `stop` 正常 |

macOS 使用说明：[docs/macos.md](docs/macos.md)

## 构建

```bash
dotnet build pad-cli/src/Pad.Cli/Pad.Cli.csproj -c Release
dotnet run   --project pad-cli/src/Pad.Cli/Pad.Cli.csproj -- version

# 平台契约测试（junction / symlink / NeedsHeal / DiscoverPosix）
dotnet test pad-cli/tests/Pad.Core.Tests/Pad.Core.Tests.csproj
```

交叉编译（在任意平台产出 macOS 可执行文件）：

```bash
dotnet build pad-cli/src/Pad.Cli/Pad.Cli.csproj -c Release -r osx-arm64
dotnet build pad-cli/src/Pad.Cli/Pad.Cli.csproj -c Release -r osx-x64
```

产物在 `src/Pad.Cli/bin/Release/net9.0/osx-arm64/`（`pad` + `Pad.Core.dll`）。

## 和 PAD 的关系

`src/Pad.Core` 是从 `agent-pack-dsh/pad/Core` **复制**出来的可移植子集。
窗口版独有的代码（WPF：`AppState`、`Skin`、`Theme`、`Tray`、`UiHang`、`ShellActivate`、`PortableBootstrap`、`InstIcon`、`AppIcon`、`Dwm`、`WinIcon`）没有带过来。

复制版的差异（收敛时要合并掉）：

| 文件 | 差异 |
|---|---|
| `CliState.cs` | 最小 `AppState`：只有 `Launcher` + `Settings` |
| `CliRunVm.cs` | 最小 `RunVm`：只有 `Profile` |
| `BundleIcon.cs` | SVG 光栅化返回 false（WPF Imaging 不可用） |
| `LaunchPolicy.cs` | 拖放用的 `IDataObject` 方法已删 |
| `Jump/JumpModels.cs` | `FocusClipboard.SetClipboard` 为 no-op |
| `Launcher.cs` | 任务图标 fallback 为 null |
| `ProjectRunner.cs` | 不抢窗口焦点 |

## 目标

`pad install / run / stop / export` 在 macOS 上可用。
PAD 的 GUI 不在本项目的范围内（WPF 不能跨平台）。
