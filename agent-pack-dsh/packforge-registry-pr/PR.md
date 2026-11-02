# PR 材料：认领 launcher ID `pad`

> **已提交（2026-11-02）**
>
> - registry 表行：https://github.com/DSH-PackForge/DSH-PackForge/pull/4（分支 `sakikoTGW:registry/claim-pad`，1 file +1/-0，mergeable）
> - 市场 JSON：https://github.com/DSH-PackForge/dsh-pack-market/pull/2（分支 `sakikoTGW:market/launchers-add-pad`，2 files +14/-0，mergeable）
>
> 以下为可复现的 patch 与提交说明，保留作审计/重提之用。

本目录是可直接提交的 PackForge 启动器注册表认领材料。**注意两个 patch 属于两个仓库**，需要分别提 PR（或按维护者流程只提交 0001，让脚本同步 0002）。

## PR 1 · `DSH-PackForge/DSH-PackForge`

- 文件：`specs/launcher-registry.md`
- patch：`0001-registry-claim-pad.patch`
- 提交方式：

```bash
git clone https://github.com/DSH-PackForge/DSH-PackForge.git
cd DSH-PackForge
git checkout -b registry/claim-pad
git apply /path/to/0001-registry-claim-pad.patch
git add specs/launcher-registry.md
git commit -m "registry: claim launcher ID pad (PAD · pack-agent for DSH)"
git push -u origin registry/claim-pad
```

**PR 标题**：`registry: claim launcher ID pad (PAD · pack-agent for DSH)`

**PR 正文**：

```markdown
## 认领 launcher canonical ID `pad`

- 启动器：PAD · pack-agent for DSH（Windows WPF 启动器 + 整合包管理器）
- 仓库：https://github.com/sakikoTGW/pack-agent
- 版本自报：`pad cli version --json`（输出 `{"id":"pad","name":"PAD · pack-agent for DSH","version":"…"}`）
- 支持形态：launch / 实例管理 / 整合包投影；已实现 PackForge `.dspack` 的读取、转换、导出与 compat 安装（profile + dshhome）

按 registry §2 规则在本表加一行（ID + 启动器名 + 版本自报方式），未改动其他行。

如果 `launchers.json` 由脚本从本表生成，请按你们的流程重跑；否则可一并合并配套的 `dsh-pack-market` PR。
```

## PR 2 · `DSH-PackForge/dsh-pack-market`

- 文件：`index/launchers.json`、`web/launchers.json`
- patch：`0002-market-launchers-add-pad.patch`
- 提交方式（同上，仓库换 `dsh-pack-market`，分支 `market/launchers-add-pad`）

**PR 标题**：`launchers: add pad (PAD · pack-agent for DSH)`

**PR 正文**：

```markdown
同步 registry 认领：为 launcher ID `pad` 增加机器可读记录（index 与 web 两份保持一致）。

- id: `pad`
- name: PAD · pack-agent for DSH
- url: https://github.com/sakikoTGW/pack-agent
- support: Windows
- 版本自报：`pad cli version --json`

对应 `DSH-PackForge/DSH-PackForge#<PR1>`（registry 表行）。
```

## 验证

两个 patch 都已做 `git apply --check`（在各自仓库的原始文件上）→ 通过；patch 为 UTF-8、行尾 LF。
