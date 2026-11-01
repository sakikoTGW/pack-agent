# D93 · PAD 自动更新：**让用户自己选**（默认只检查，不自动装）

问题：更新要不要用户选？——**要**。PCL 强制自动更新的后果就是网上全是「关掉强制更新/版本回退」教程；
PAD 做成：默认**检查 + 提示**，装不装、什么时候装，用户决定。

## 默认行为（可关）

| 设置 | 默认 | 说明 |
|---|---|---|
| 更新通道 | `release`（检查开） | `off` = 完全不查 |
| 更新源 feed | 空 | 留空 = 不检查；发布时填 latest.json 地址 |
| 发现新版 | 启动后 Toast 提示 | “PAD 有新版 x.y：设置 → 升级 里可以下载” |
| 自动下载 | **关** | 勾了才会后台下载（仍然重启才替换） |
| 安装 | 用户点「下载 PAD 更新」 | 下载→校验→提示“重启生效” |

## 机制（PCL 式，单文件 exe）

`latest.json`（放静态托管/仓库 release）：

```json
{ "schema": "pack-agent.pad-update/v1", "version": "0.4.5",
  "url": "https://…/pack-agent-for DSH.exe",
  "sha256": "…", "size": 178900000, "notes": "…" }
```

1. 启动（窗口模式）→ 后台读 feed；有新版 Toast 提示（或按设置自动下载）
2. 点下载 → 存到 `<root>/library/updates/<ver>/pack-agent-for DSH.exe`，校验 size + sha256
3. 写 `pending.json` → 提示“重启后自动替换”
4. 下次启动：把新 exe 放到 `PAD.exe.new`，用临时 cmd 等本进程退出后 `move /Y` 覆盖并重启；**数据（.pack-launcher/.pack-runtime）不动**
5. CLI / `--shot` 模式不触发替换（测试/截图安全）

## 验证（headless CLI）

- `pad cli pad-update check` → `newer: true`（假 feed 9.9.9）
- `pad cli pad-update fetch` → 下载到 `<root>/library/updates/9.9.9/…`，pending 写入
- `pad cli pad-update pending` → 显示版本/路径/sha256
- `pad cli pad-update apply --dry` → 生成 helper cmd（不替换）
- 设置页新增：更新源输入框、自动下载开关、「下载 PAD 更新」按钮（截图 01）
- `pad-pack` / `pad-window` / `p2-p3` 全绿；单文件 exe 已重建（178.9MB）

## 边界

- feed 还没托管：现在通过 pad.json `other.updateFeed` 或环境变量 `PAD_UPDATE_FEED` 指（http/https 或本地路径），
  发布时把 latest.json 放到固定地址即可（不写死 URL）。
- 替换依赖"用户已经下载过 pending"；没有 pending 时启动什么都不做。
