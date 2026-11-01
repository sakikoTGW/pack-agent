# D95 · GitHub Releases 发布 + 线上更新真机验证

## 发布

- 仓库：`sakikoTGW/pack-agent`（public，已有 v0.4.1 → 新 v0.4.5）
- 代码：本地 main 推 origin（58 commits）
- Tag/Release：`v0.4.5`，target main
- 资产：
  - `pack-agent-for-DSH.exe`（187,608,151 bytes，sha256 `8e10adc787729101973698c5b9271f88aca85e10f669c54248567b9bd7c28717`）
  - `latest.json`（version 0.4.5 / url / sha256 / size）
  - `SHA256SUMS.txt`

固定地址（每次覆盖同名资产）：

- Release：https://github.com/sakikoTGW/pack-agent/releases/tag/v0.4.5
- exe：https://github.com/sakikoTGW/pack-agent/releases/latest/download/pack-agent-for-DSH.exe
- feed：https://github.com/sakikoTGW/pack-agent/releases/latest/download/latest.json

坑：GitHub 把资产名里的空格换成点，feed URL 对不上 → 统一用 dash 名（build-release.ts 已改）。

## 线上真机验证（0.1.0 开发版 → 线上 0.4.5）

1. `pad cli pad-update fetch`（默认 feed）→ 下载 179MB，70s，size+sha256 校验通过，pending 写入；
2. 正常启动一次（PAD_UPDATE_NO_RESTART=1 只替换）→ 进程退出；
3. `exe` 的 sha256 变为 release 的 `8e10adc7…`，pending 删除；
4. 替换后的 exe 跑 `cli pad-update check` → `current: 0.4.5, newer: false`。

## PAD 内默认值

`OtherPrefs.UpdateFeed` 默认 = 上面的 latest.json；更新通道 release（检查开）、自动下载默认关、
用户可点「下载 PAD 更新」或关掉通道 —— 行为见 D93。

## 证据文件

- `latest.json`：发布 feed 副本
- `release-view.json`：gh release view 输出（资产名/size）
- `SHA256SUMS.txt`
