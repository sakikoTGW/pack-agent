# D62 · 项目轴 `project run` 实跑（真实本机 root）

用户原话：「用真实本机 root 跑一次 project run，留 EVIDENCE」。

这一步验收的是项目轴最核心的承诺：**一个项目窗口只能看到这个项目的工作区**。
PAD 界面还没接线（UI 是并行线的域），所以证据由 CLI + 真窗口 + 盘上注册表三部分组成。

## 环境

| 项 | 值 |
|---|---|
| launcher root | `E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher`（真实本机 root，PAD 正在用同一个） |
| 内核发行号 | `0.2.1-rc.2`（已安装、verified） |
| 被测 exe | `E:\tmp\pad-proj-build\pack-agent-for DSH.exe`（临时输出目录构建，没覆盖正在跑的 PAD） |
| 项目 | `ev-project`，绑定 `ws-frontend`、`ws-backend` 两个目录 |
| profile | `web`（先 `node bin.js --profile web --dump-config` 物化 web 模板，见 `profile-init.log`） |

## 步骤与结果

### 1) 建项目 → seed（写项目的 workspace 注册表）

```
pad cli project add ev-project --workspace ...ws-frontend --workspace ...ws-backend \
    --version 0.2.1-rc.2 --profile web --json --out ...add.json
pad cli project seed ev-project --json --out ...seed.json
```

- 新建实例 `instances/ev-project/`（自己的 home + workspace），`instance.json` 见产物；
- `seed.json`：`kept:0 added:2`，两个工作区各自拿到新 uuid；
- 关键：**第一次** seed 之后 DSH 自己把会话写进注册表（`workspace.json` 里 ws-frontend
  已有 `session-a36714d1-…`），PAD 的 uuid 和 DSH 的会话归属都在。

### 2) `project run` → 起窗口（真实浏览器打开）

```
pad cli project run ev-project --json --out ...run.json
```

`run.json`（本次构建，耗时约 12s：启动 → DSH 报端口）：

```json
{
  "status": "started",
  "instance": "ev-project",
  "profile": "web",
  "home": "...\\.pack-launcher\\instances\\ev-project\\home",
  "workspaces": 2,
  "pid": 22656,
  "url": "http://127.0.0.1:3080/?token=jBjRxQov7Vt98_y7pw-WMUWDNnb0qb-3u4Tlm05fTX4",
  "script": "...\\instances\\ev-project\\launch-web.cmd"
}
```

窗口截图：`dsh-window.png`（1440 宽，DSH 侧栏「工作区」只有 `ws-frontend` / `ws-backend`，
没有本机 `E:\DSH-Home` 的十几个工作区）。CLI 退出后 web 进程仍活着：

- node pid 68128 监听 `127.0.0.1:3080`；
- `web-launch.log` 里是 DSH 自己打的 `dsh web: http://127.0.0.1:3080/?token=…`。

### 3) 再跑一次 → 不重复起进程，报「在跑」

```
pad cli project run ev-project --json --out ...run-focus.json
```

```json
{
  "status": "already-running",
  "instance": "ev-project",
  "Say": "在跑",
  "pid": 68128,
  "port": 3080,
  "address": "127.0.0.1:3080",
  "uptime": "4 分钟",
  "focused": false
}
```

`pad cli status --json` 同时认这条实例：`ev-project · Running · PAD 账本 · 进程现场`（`status.json`）。

## 盘上事实（`workspace.json`）

- `unit: workspace/2`、`global.initialized: true`、`workspaceIds` 两条、`defaultWorkspaceId` = 第一条；
- 两条记录 path 指向 `E:\tmp\pack-agent\proj-evidence\ws-frontend|ws-backend`，title 是叶子名；
- 再 seed 一次不会换 uuid、不会丢 `sessionIds`（`pad-project.test.ts` 里也有这条断言）。

## 这轮踩到并修掉的三个坑

1. **`WriteScript` 在 0.2.1-rc.2 上要 167s**（`PnpmHeal.Relink` 的 junction 修复在大
   `node_modules` 上极慢，实测 `pad cli script ev-project web` = 167.5s）。项目启动不该等它：
   `Runner.Launch(..., healScripts: false)`，DSH 启动脚本本身会清/修 profiles fallback，
   实测 12s 就起好。**建议**：所有 CLI 启动也走 `healScripts:false`，只在 UI 的「修复」动作里 heal。
2. **LaunchWeb 的管道读取者会随 CLI 退出消失**，URL 行还没读出来进程就没了。`project run`
   现在在退出前等 `dsh web:` 就绪行（`WaitForWebUrl`），拿到 URL 才返回。
3. **旧日志的 token 陷阱**：第一次实现按「最新日志」找 URL，读到上一轮死进程的日志，
   拿到过期 token（HTTP 401）。现在只读 **本次启动之后** 写过的日志（`sinceUtc` 过滤），
   `run.json` 里的 token 是新进程的。

## 产物

| 文件 | 说明 |
|---|---|
| `run.json` | 第一次 run：started + 真实 URL + pid |
| `run-focus.json` | 第二次 run：already-running + 端口 + 已跑时长 |
| `status.json` | `pad cli status --json`：ev-project Running |
| `workspace.json` | 项目的 DSH 注册表（2 条、initialized、DSH 写入的 sessionIds） |
| `project.json` / `instance.json` | 项目索引与实例记录 |
| `launch-web.cmd` | PAD 生成的真实启动脚本（DSH_HOME/凭据/工作区都钉在实例上） |
| `web-launch.log` | DSH 自己打的监听行（新的 token） |
| `profile-init.log` | 物化 web 模板的 `--dump-config` 输出 |
| `dsh-window.png` | 真窗口截图：侧栏只有项目的两个工作区 |

## 清理（保留现场，用户先看）

```powershell
# 关掉这次起的 web 进程（先关浏览器标签更干净）
Stop-Process -Id 68128 -Force
# 删项目绑定；实例目录等 PAD 下次重启后可在管理页删，或实例停掉后手动删
pad cli project remove ev-project
```

> 注意：`ev-project` 是真实 root 里的新增实例，项目/实例都留着方便复看；
> 不需要时按上面顺序清掉。DECISIONS 条目等与并行 UI 线合流时统一补 D62。
