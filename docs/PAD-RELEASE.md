# PAD 发布流程（单文件 exe + latest.json）

## 1. 出包

```powershell
bun run release -- --version 0.4.5 --base-url https://你的域名/pad
```

不给 `--base-url` 也能出包：latest.json 里的 url 写占位符，上传后手动改。

产物 `dist/release/<version>/`：

```
pack-agent-for DSH.exe     固定名，feed 就指向它（用户只下这一个）
latest.json                PAD 自动更新的 feed
notes.md / SHA256SUMS.txt
README.txt
```

## 2. 上传（三选一）

| 方式 | 怎么做 | feed 地址 |
|---|---|---|
| GitHub Releases | 新建 tag，上传 whole folder（release 支持 assets；latest.json 用 raw 链接放固定分支也行） | `https://raw.githubusercontent.com/<org>/<repo>/main/pad/latest.json` |
| 静态对象存储（OSS/COS/R2/S3） | 整个目录 sync 上去，公开读 | `https://<bucket>/pad/latest.json` |
| 内网/本机 HTTP | `python -m http.server 8000` 指向目录（仅测试） | `http://<ip>:8000/latest.json` |

**关键：`pack-agent-for DSH.exe` 的下载地址要固定**（latest.json 里的 `url` 就是它）；内容每次更新覆盖。

## 3. 让 PAD 知道 feed

用户在「设置 → 其他 → 升级」填「更新源」= 上面的 latest.json 地址；
或发布时用 `--base-url` 把地址写进 latest.json 后，默认配置里预填（改 `OtherPrefs.UpdateFeed` 默认值）。

## 4. 行为（用户自己选）

- 启动时后台检查 → Toast 提示「PAD 有新版 x.y：设置→升级 里可以下载」
- 设置里：更新源 / 自动下载（默认关）/ 手动「下载 PAD 更新」/ 更新通道 off
- 下载后校验 sha256+size → 提示重启 → 下次启动替换 exe 并重启（数据不动）

## 5. 验证

```powershell
# 本地验证 feed（不需要上传）
$env:PAD_UPDATE_FEED = "E:\pack-agent\dist\release\0.4.5\latest.json"
pad cli pad-update check
# API 自检：latest.json 的 sha256/size 必须和 exe 一致（build-release 已内置检查）
```

## 6. GitHub Releases 实操（本仓库已用 v0.4.5 走过一遍）

仓库：`sakikoTGW/pack-agent`（public；`sakikoTGW/PAD` 是 private 源码镜像，不发资产）。

```powershell
# 1) 出包（base-url 指向 latest release 的固定资产地址）
bun run release -- --version 0.4.5 --base-url https://github.com/sakikoTGW/pack-agent/releases/latest/download

# 2) 推代码 + 建 release（gh 已登录）
git push origin main
gh release create v0.4.5 --repo sakikoTGW/pack-agent --target main `
  --title "PAD v0.4.5" --notes-file dist/release/0.4.5/notes.md `
  "dist/release/0.4.5/pack-agent-for-DSH.exe" `
  "dist/release/0.4.5/latest.json" `
  "dist/release/0.4.5/SHA256SUMS.txt"
```

固定地址（每次覆盖同资产名，`latest` 自动指向最新 release）：

- feed：`https://github.com/sakikoTGW/pack-agent/releases/latest/download/latest.json`
- exe：`https://github.com/sakikoTGW/pack-agent/releases/latest/download/pack-agent-for-DSH.exe`

**坑**：GitHub 会把资产名里的空格自动换成点（`pack-agent-for DSH.exe` → `pack-agent-for.DSH.exe`），
feed URL 就对不上了。所以发布资产统一用 **`pack-agent-for-DSH.exe`**（dash 名），本地/用户侧显示名无所谓。
`scripts/build-release.ts` 已按这个命名。

## 7. 已实测链路（2026-11-02）

用 0.1.0 开发版对着线上 feed：

1. `pad cli pad-update fetch` → 从 GitHub 下载 179MB（70s），校验通过，写 pending；
2. 正常启动 → 自动替换 exe（hash = release 的 `8e10adc7…`），pending 清除；
3. 替换后的 exe 自报 `current: 0.4.5, newer: false`。

默认 `updateFeed` 已指向上面的 latest.json，0.4.5 之后的新用户开箱即自动检查。

## 8. 一句话发版（推荐）

```powershell
# 自动 patch +1，构建、提交、push、建 GitHub Release、校验 latest 都做掉
bun run release:github -- --bump patch --notes "这一版改了什么"

# 指定版本 / 只预演
bun run release:github -- --version 0.4.6 --notes-file notes.txt
bun run release:github -- --bump patch --notes "…" --dry-run
```

脚本：`scripts/release-github.ts`；默认仓库 `sakikoTGW/pack-agent`（`--repo` 可换）。
它内部依次执行：`build-release.ts`（发布包）→ `git commit/push`（版本号）→ `git tag/push` →
`gh release create --latest`（exe + latest.json + SHA256SUMS）→ `gh api .../releases/latest` 校验 tag。
