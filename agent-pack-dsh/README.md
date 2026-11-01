# agent-pack for DSH

本仓 DSH 全部在这个文件夹。根目录 `packagent dsh` 转到这里。

```
agent-pack-dsh/
  pad/          PAD 启动器：.NET 9 WPF 窗 + pad cli（pack-agent-for DSH.exe）
  modpack/      投影、白名单、TS 侧 launcher API
  plugin/       实例内管理器 @sakikotgw/pack-agent-dsh
  gateway/      管理口 @sakikotgw/pad-gateway（包住官方 apiproxy）
  pack-index/   SQLite 检索
  cordis.ts     通用 install 用的 overlay
  scripts/      编插件 / 编检索 / stage npm
  tests/
  docs/         产品设计（先读 docs/PRODUCT.md）
```

人路径：`.pack.zip` → 隔离实例 → PAD 启动页点一下，或 `pad cli run <instance>`。

无头任务可由脚本调用，使用已有实例的 DSH 版本、工作区和 DeepSeek API Key：

```powershell
& 'pack-agent-for DSH.exe' cli task run <instance-id> --input 'prompt.txt' --out 'result.json' --timeout 120
```

输入文件须为 UTF-8。输出 JSON 的 `status` 为 `success`、`failed` 或 `timeout`，另含 `exitCode`、`output`、`error`、`elapsedMs`；仅成功时进程退出码为 0。超时范围为 1–3600 秒，默认 120 秒。Key 从 PAD 实例选择的凭据读取，通过子进程环境传递，不写进任务参数或输出；使用前需在 PAD 的 API Key 设置中配置 DeepSeek API 凭据。此入口使用 `deepseek-official` / `deepseek-flash`，与桌面账号登录分开。

无头任务使用所选实例的工作区和 DSH 配置，可能运行 DSH 可用的工具；应只向可信的实例和工作区提交任务。`task run` 本身是一条同步调用，队列接口见下文。

### 无头队列与 Mesh

`pad cli queue submit <instance> --input <prompt.txt> --out <receipt.json> [--timeout 120]` 将任务与输入快照写入当前启动器根下的 `headless-queue`。`pad cli queue worker` 持续处理队列；`worker --once` 清空当前可运行任务后退出。通过 `queue list --out <file>`、`queue status <id> --out <file>` 查看状态，`queue cancel <id> --out <file>` 取消尚未开始的任务；`queue retry <id> --out <file>` 显式重试失败、超时或中断的本机任务。

队列同一时刻只允许一个 worker，按提交时间执行。worker 重启后能保留排队任务；对上次运行中断的任务，如果有完整的当次结果则恢复结果，否则标记 `interrupted`，不会自动重跑。强制结束 worker 可能留下仍在执行的子进程，重试前应先确认它已停止。队列持久保存任务输入与结果，请按本机敏感文件管理。

显式指定对端的 Mesh 地址，可用 `pad cli queue submit-mesh --url http://peer:45917 --input <prompt.txt> --out <receipt.json> [--thread <name>]` 入队。启用配对时，在启动 worker 的环境中设置 `PAD_MESH_TOKEN`；不要将其放进命令行或文件。发送前先访问对端健康接口：未探测到 Mesh 时任务保持排队，持续运行的 worker 每 30 秒再次探测；`--once` 不等待下次探测。Mesh 返回确认时状态为 `accepted`，仅表示对端接收，不表示已完成；发送请求后连接中断、响应异常等标为 `delivery_unknown`，不会自动重发。检查对端任务历史后再决定是否新建任务。它不做设备发现或远端结果回传，真实跨机和 tsnet 路径需按具体网络单独验证。

设计稿：

- 产品：[docs/PRODUCT.md](docs/PRODUCT.md)
- 装包链：[docs/modpack.md](docs/modpack.md)
- 启动器功能：[docs/launcher-design.md](docs/launcher-design.md)
- 引擎：[docs/launcher-architecture.md](docs/launcher-architecture.md)

装管理器（开发）：

```sh
bun run build:dsh
dsh plugin --profile web add ./agent-pack-dsh/plugin -w
```
