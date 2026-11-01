# D94 · 发布包 + 托管流程 + 真机替换验证

## 1) 托管流程（无固定地址也能出包）

`bun run release -- --version 0.4.5 --base-url https://你的域名/pad` 一次产出：

```
dist/release/0.4.5/
  pack-agent-for DSH.exe    ← 固定名（feed 指向它，用户只下这一个）
  latest.json               ← {version,url,sha256,size}
  notes.md / SHA256SUMS.txt / README.txt
```

托管三选一（详见 `docs/PAD-RELEASE.md`）：

| 方式 | feed 地址 |
|---|---|
| GitHub Releases / raw 固定分支 | `https://raw.githubusercontent.com/<org>/<repo>/main/pad/latest.json` |
| 对象存储（OSS/COS/R2/S3）静态目录 | `https://<bucket>/pad/latest.json` |
| 内网/本机 HTTP（测试） | `http://<ip>:8000/latest.json` |

关键：exe 的下载地址固定（覆盖更新），用户「设置 → 其他 → 升级 → 更新源」填 latest.json 地址；
或者发布时 `--base-url` 写死进 latest.json。

## 2) 已出的发布包（0.4.4，无 URL 版）

- `dist/release/0.4.4/pack-agent-for DSH.exe`：178.9 MB，sha256 `e04d1a99…d0119459`
- `latest.json` 自检通过（sha256/size 与 exe 一致）
- 用发布包自己跑 `pad cli pad-update check` → `current: 0.4.4, newer: false`（版本号已写进 assembly）
- 用开发版（0.1.0）跑同一 feed → `newer: true`（版本比较正确）

## 3) 真机替换验证（不是 dry-run）

throwaway 目录里放一份 PAD（临时构建的输出），假 feed 指向 `packmaker.exe`（换 hash 可证）：

1. `cli pad-update fetch` → 下载到 `library/updates/9.9.9/…`，写 pending；
2. 正常启动一次 PAD（`PAD_UPDATE_NO_RESTART=1` 只替换不重启）→ 进程自己退出；
3. 结果：`exe 的 sha256 == 假新版`、`pending.json 已删除`、`exe.new 不存在`。

说明“下载 → 校验 → 下次启动替换 exe”这条链是通的，且数据目录没动。

## 4) 用户选择（D93 已实现）

通道 off / 更新源可配 / 自动下载默认关 / 手动「下载 PAD 更新」；不强制。
