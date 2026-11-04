# 在 macOS 上跑 pad-cli

## 前置

| 项 | 要求 |
|---|---|
| 系统 | macOS 12+（arm64 或 x64） |
| .NET | 9.0 运行时；或用自包含发布，见下 |
| Node | 22.19+ 或 24+（`node` 在 PATH 里） |
| pnpm | 没有也行：`pad` 会用 npm 装一次 |

## 构建 / 发布

```bash
# 框架依赖（需要 .NET 9 运行时）
dotnet publish pad-cli/src/Pad.Cli/Pad.Cli.csproj -c Release -r osx-arm64 --self-contained false

# 自包含（不依赖机器上的 .NET）
dotnet publish pad-cli/src/Pad.Cli/Pad.Cli.csproj -c Release -r osx-arm64 --self-contained true -o out/mac
```

产物在 `out/mac/`，可执行文件是 `pad`。

> 未签名/未公证：第一次双击会被 Gatekeeper 拦住。终端里跑没问题；
> 或 `xattr -d com.apple.quarantine ./pad`。

## 快速验证

```bash
./pad selftest          # 链接（symlink）/ NeedsHeal / sh / ps
./pad where             # launcher root 在哪
```

期望 `"platform": "macos"`、`"ok": true`。

## 装一个 DSH、跑起来

```bash
./pad release install 0.2.1-rc.5
./pad instance create demo 0.2.1-rc.5
./pad profile add demo dsh-tui tui
./pad script demo                       # 生成 launch-dsh-tui.sh，可先读一眼
./pad run demo                          # 打开 Terminal.app 跑 TUI
./pad ps                                # 看到 pid / profile
./pad stop demo dsh-tui                 # 关掉（整棵进程树）
```

web 形态：

```bash
./pad profile add demo web web
./pad run demo web                      # 后台起服务，读日志等 "dsh web: http"，再开浏览器
```

## 平台的实现方式

| 事情 | macOS 怎么做 |
|---|---|
| 链接 | `ln -sfn`（Windows 用 junction） |
| 起终端 | 生成 `.command` 脚本交给 `Terminal.app`（`open -a Terminal`） |
| 运行命令 | `/bin/sh -c`（Windows 是 `cmd /d /c`） |
| 找进程 | `ps -eo pid=,command=`（Windows 是 PEB/WMI） |
| 找端口占用 | `lsof -nP -iTCP:<port> -sTCP:LISTEN -t`（Windows 是 `netstat`） |
| 找安装 | 扫 PATH / 全局 node_modules / `/Applications/*.app` / `~/.dsh` 等 |
| 打开 Finder | `open -R`（Windows 是 `explorer /select`） |

## 已知限制

- `portable-export` 仍按 Windows 便携包生成 `.cmd`，mac 上用不到；
- 没有 GUI：PAD 窗口是 WPF，mac 上只有 CLI；
- 代码签名/公证没做；
- `run` 依赖 `Terminal.app`；纯 SSH 无窗口环境请用 `pad script` + 自己起进程。
