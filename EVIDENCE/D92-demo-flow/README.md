# D92 · 演示：单文件 exe + 整合包 的完整流程

产物：`dist/single/pack-agent-for DSH.exe`（178.9MB，PCL 式单文件）。
下面整个流程在**空目录**里跑，没有仓库、没有安装 node/bun。

## 用户拿到的形态

```
demo-flow/
  pack-agent-for DSH.exe          ← 一个文件
  e2e-pack-0.1.0.pack.zip         ← 整合包（几 KB ~ 几十 MB，随便发）
```

## 流程

1. **双击 exe**（第一次用到整合包功能时自动解压 `.pack-runtime/`，250MB，几秒）
2. **把 .pack.zip 拖进 PAD 窗口**（等价：`cli import <pack.zip>`；也支持 管理→装整合包…）
   - 包里有：1 个本地插件 + 1 个 skill；PAD 自动：建实例 → 投影 → 装插件 → 启用整合包
3. **看实例**：`e2e-pack`，状态 `ready`，`0.2.1-rc.2`（截图 01）
4. **看插件/整合包**：插件列表里有 `@e2e/demo-plugin`（本地插件，pnpm file: 装进去），整合包区有「装整合包…」（截图 02）
5. **启停整合包**：`pack deny` / `pack allow` → 状态在「已启用 / 已停用」间切换（不删文件）
6. **实例本身**：`status` → `Stopped`；点「去启动」就是在跑

## 制作侧（发给别人之前）

`管理 → 导出整合包`（截图 03）：填名字/版本/说明 → 勾核心/容器 → 加插件（npm 或本地）→ 导出得到 `.pack.zip`。
这个 zip 就是上面第 2 步拖进去的文件。

## 这次演示暴露并修掉的 4 个真问题

1. **便携 runtime 少带 registries** → `import` 报 `PA009 no format-sniff match`；
   现在 `pack-runtime/registries/` 和 `pack-runtime/agent-pack-dsh/modpack/registries/` 两处都带。
2. **DSH 0.2.1 起 `--dump-config` 要求 `--profile`** → 安装 manager 后 dump 校验失败；
   TS/C# 两侧都补了 `--profile <name>`。
3. **包里本地插件按包名装**（会去 npm 找 `@e2e/demo-plugin` 失败）→ 改成 `pnpm add file:<解压目录>/plugins/...`。
4. **失败时 job 日志并发写崩溃**（`IOException: jobs/import-*.log used by another process`）→ AppendJobLog 重试 6 次。

## 验证

- `cli import` → exit 0，实例 `status=ready`；
- `cli pack list e2e-pack` → `Enabled: true`（已启用），`pack deny` 后 `false`（已停用），`allow` 再 `true`；
- 三张 UI 截图 = `--shot` 自渲染（不抢前台、不点真实窗口）。
