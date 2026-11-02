# D102 · PackForge 扩展层：市场 / registry / files[] / compat 安装

日期：2026-11-02｜状态：**已实现并通过验证**（TS/pack-runtime 层；PAD C# 尚未接线）

配套：[D101-packforge-adapter](../D101-packforge-adapter/README.md)（读取/校验/转换/导出）｜设计稿：[pack-v2-superset.md](../../agent-pack-dsh/docs/pack-v2-superset.md) §11

## 1. 交付

| 文件 | 内容 |
|---|---|
| `agent-pack-dsh/modpack/packforge-ext.ts` | 市场索引/下载、launcher registry、files[] 下载、compat plan/apply |
| `agent-pack-dsh/modpack/packforge-ext.test.ts` | 10 项断言（stub fetch / 夹具 home / 真实包夹具） |
| `packforge.ts` CLI 扩展 | `market`、`fetch <id>`、`registry [--id]`、`install --home [--apply] [--deps]` |
| `package.json#scripts.test` | + `packforge-ext.test.ts` |

新增诊断码：PA150（home/profile 不合法）、PA151（dshhome 未支持）、PA152（市场索引/查找）、PA153（下载/hash）、PA154（registry 回落 warning）、PA155（dsh plugin add 失败）、PA156（profile package.json 非法）。

## 2. 验证（全部实际执行）

### 2.1 单元测试

```powershell
bun run agent-pack-dsh/modpack/packforge.test.ts        # 17 项 → PASSED
bun run agent-pack-dsh/modpack/packforge-ext.test.ts    # 10 项 → PASSED
```

ext 覆盖：v4 历史格式读取；市场索引解析（含跳过缺字段条目）；包下载 sha256/size 校验与 hash 不符 PA153；registry 正常读取 / 网络失败回落内置；files[] 成功/坏 hash/不安全路径/无 URL；compat plan；compat apply（overrides 落盘、home 落盘、bundles 写 package.json、deps runner、敏感文件跳过）；dry-run 零写入；已存在 profile → PA150；dshhome → PA151。

### 2.2 真实市场（联网）

```powershell
bun run agent-pack-dsh/modpack/cli.ts packforge market
# market schemaVersion=2 packs=8 generatedAt=2026-10-23T17:11:27.445Z
# yukitakasama.better-deepseek-harness-codex … 1115294B … （8 行）

bun run agent-pack-dsh/modpack/cli.ts packforge registry --id dshl --json   # 读到真实 registry，含 padClaim
bun run agent-pack-dsh/modpack/cli.ts packforge fetch pokemon --out .tmp/packforge-real/cli
# sha256=3d6a2748…87f（与 index.json 一致）
bun run agent-pack-dsh/modpack/cli.ts packforge inspect .tmp/packforge-real/better-sidebar-1.0.0.dspack
# type: profile / overrides 1 → v4 历史格式现已可读
```

### 2.3 真实包 compat 安装

**pokemon（2.5KB，`--apply`）**：

```
profileDir: .tmp/packforge-real/home-pokemon/profiles/pokemon
copiedOverrides: 2   bundles: 4   deps.planned: 2
落盘：profiles/pokemon/{.dshpkcfg, cordis.patch.yml, package.json}
package.json: dsh.profile.bundles = [dsh-base, dsh-web-app, dsh-wildmon, @hellosz/dsh-pets]
```

**better-deepseek-harness-codex（1.1MB，dry-run）**：

```
[dry-run] …/home-codex/profiles/better-deepseek-harness-codex
bundles=26 deps=24 overrides=1 home=343 skipped=0
dry-run 后目标目录文件数 = 0（零写入）
```

### 2.4 pack-runtime bundle

```powershell
bun build src/cli.ts --target=bun --outfile .tmp/packforge-bundle/packagent.bundle.js
# 0.87 MB；bundle 内 `dsh packforge market` 联网正常
```

## 3. 边界（未验证/未做）

1. **真实依赖安装**：只实现了 `--deps` 走 `dsh plugin add` 的调用；本次验证的机器没有对其执行网络安装（避免污染），计划文本已验证。真实 pnpm/reconcile 需 PAD 侧接。
2. **PAD C# 接线**：PAD UI/CLI 还没调用 pack-runtime 的 `packforge install`；PAD 的实例创建 + `--home` 传入是下一步。
3. **dshhome 安装**：显式 PA151（M4）。
4. **launcher ID 认领**：registry 里还没有 `pad`；`packforge registry` 已输出 `padClaim.record` 与 `REGISTRY_CLAIM_ROW`，需向 DSH-PackForge 提 PR 后才进官方表。
5. **files[] 真实网络包**：测试用 stub；市场当前 8 个包均无 `files[]`，未做真实下载验证。
