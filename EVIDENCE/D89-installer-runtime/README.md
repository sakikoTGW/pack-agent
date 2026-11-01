# D89 · 安装器自带 pack-runtime（导出不再现场装配）

承接 D88：上一轮每次 `portable-export` 都现装 pack-runtime（bun build + 找索引 + 拷 115MB）。
本轮改成**安装器/发布目录先装好一次**，导出时直接复制；目标机与导出机都不再需要现场装配。

## 改动

1. **安装器（portable kit，`packagent dsh launcher portable-init <kit>`）**
   `portable.ts` 新增 `stagePackRuntime(dir, repo)`：
   - `bun build <repo>/src/cli.ts --target=bun` → `packagent.bundle.js`
   - 复制 `process.execPath`（bun.exe）
   - `ensurePackIndexBin()` 预编译 `pack-index.exe` 并复制
   - 写包根 `package.json`（`@sakikotgw/pack-agent-dsh`）、复制 `registry.yaml`、`skills/agent-pack/SKILL.md`、`runtime.json`
   `initPortableKit` 在复制 host exe 后调用它 → `kit/pack-runtime/` 就位。

2. **导出时优先复制现成的**（`Launcher.TryStagePackRuntime`）
   exe 旁有 `pack-runtime/` → `CopyTreeLink` 直接复制；
   只有没有时才退回现场装配（D88 逻辑，需要仓库+bun+索引）。

3. **发布/安装器可以用 PAD CLI 装配**
   `pad cli pack-runtime stage <dest>` → `Launcher.StagePackRuntime(dest)`（发布目录里放 exe 后跑一次即可）。
   单文件 exe 用 `Environment.ProcessPath` 找自身目录。

## 验证（证据同目录）

- `pad cli pack-runtime stage E:\tmp\pad-build\lead` → exit 0，`pack-runtime/` 含
  `bun.exe 115MB / packagent.bundle.js 817KB / pack-index.exe 2.1MB / package.json / registry.yaml / skills/...`
- **不设 `PACK_AGENT_REPO`** 导出 `export6`（root3 → e2e3）→ exit 0，`export6/pack-runtime/` 与 exe 旁一致（bundle 同为 816967 字节，说明是复制不是重编）。耗时 153s。
- `packagent dsh launcher portable-init E:\tmp\kit-installer-test` → 87s，kit 内出现 `pack-runtime/`（含 bun/bundle/index/assets），说明安装器一次装好，后续导出零装配。
- `pad-pack` / `pad-window` / `pack-catalog` 绿。

## 已知（与本次无关）

`p2-p3.test.ts` 有一条旧断言 `Pad.csproj 不该再引 WebView2` 失败——当前 PAD 为内置 DSH 视图引了 WebView2（D77），断言是旧壳时代留下的，属于既有不一致，不是本次改动引入。
