# D41 · 2026-10-28 · bun 自愈、容器去掉「收编」、制作整合包用 Go

## 1. bun 找不到 → PA028（用户截图 1）

现象：版本选择页「整合包」报 `error[PA028]: the catalog command failed … Bun was not found on PATH`。
根因：PAD 调 `node <repo>/bin/packagent.js`，这层再 `spawnSync('bun')`；bun 不在 PATH 就整条链挂掉。

修法（不再要求用户先自己装 bun）：
- `Proc` 增加绝对路径解析 + `Bun` 解析器（`%USERPROFILE%\.bun\bin`、`%APPDATA%\npm`、
  `%LOCALAPPDATA%\pack-agent-dsh\tools\bun`、`Program Files\Bun`、`BUN_INSTALL`、PAD 旁）；
- 子进程 PATH 自动前置 bun 目录（`node → bun` 的孙进程也能找到）；
- 真缺 bun 时 `Bun.EnsureAsync` 自动 `npm i -g bun` 再重试（`Packs.Cli/CliJson/CliDsh` 都先 ensure）；
- `Packs.Unavailable()` 不再把 bun 当硬门槛。
实测：`node bin/packagent.js dsh launcher packs --json` exit 0。

## 2. 「这个实例的整合包」和「新增启动版本」两块删掉（用户截图 2）

用户原话：*"感觉这俩没存在的必要了"*。版本选择页只留：整合包目录、DSH 版本、容器。
`pad-window.test.ts` 改为反向断言：这两块再出现就红。

## 3. 容器学 PCL（用户原话：收编啥意思 / CLI 算容器吗 / 点击就选中 / 右边只会是设置）

- **命令行不算容器**：`DetectedCliRow` 删除，容器列表只列「本实例启动配置 + 本机桌面端 + TUI」；
- **去掉「收编」按钮**：识别到就在列表里；不再让人先"收编"才能用；
- 列表从 `ItemsControl(Button)` 换成 `ListBox`（PCL 风格 `PlainList`/`RowItem`）：**点击 = 选中**（accent 边框+左侧竖条），
  行右侧**只有设置齿轮**：实例→版本设置页，探测容器→只读「容器设置」卡（类型/内核/home/在跑）；
- 修了一个栈溢出：选中容器会回写实例 → 触发实例列表刷新 → 再重建容器列表 → 互相递归
  （`SelectionChanged` 回写实例又重建容器列表 → 0xC00000FD）；加 `_containerSync` 重入闩。

## 4. 制作整合包：Go 写 `packmaker`，PAD 里点「制作」

用户：*"那就要支持制作整合包，一个整合包至少要有插件，最多就是给你哪个结构"*。

新工具 `agent-pack-dsh/packmaker`（Go 1.22，只用标准库 `archive/zip`）：
- `packmaker make --spec <spec.json> --out <library/packs> [--icon file]`，stdout 打印 zip 路径；
- **校验**：name/version/`dsh.version` 必填；`dsh.plugins[]` **至少一个**，否则 exit 2；
- **可选结构**：skills/rules/mcp/commands/hooks/subagents/settings 勾了才写进 `pack.json`（空数组占位），
  另写 `modules` 标志、`dsh.profile`、`bundle.portable=true`、`meta.author=PAD`；
- 图标写进 zip 根 `icon.png|svg|ico`（给实例图标链读）；
- 单测：`go test ./...`（至少一个插件 / pack.json 内容 / MCP 开关 / 图标 / 插件去重）。

PAD 接线：
- 整合包区右上「制作」→ `MakePackWindow`（名字/版本/说明/图标/DSH 版本/profile/插件多行/结构勾选）；
- `Core/PackMaker.cs` 只负责写临时 spec 调 `packmaker.exe`（PAD 旁 / 仓库 / `PACKMAKER_EXE`），
  出错把校验语义原样抛出来；
- `Pad.csproj` 构建时把 `packmaker/bin/packmaker.exe` 拷到输出目录；
- `AppState.ReloadPackCatalog` 只保留**带 dsh.version（或 pinst）**的包——exports 里 Cursor/Claude
  的 agent 配置包没有 dsh 层，不算 DSH 整合包（用户："一个组件一个整合包"）。

实测（scratch launcher，无 UI）：

```
packmaker make --spec demo-pack-spec.json --out ...\.pack-launcher\library\packs
→ tui-全家桶-0.1.0.pack.zip   (pack.json 887B + icon.png)
pack.json: dsh.version=0.1.0-rc.7 profile=dsh-tui plugins=[dsh-tui,pad-gateway]
           modules.mcp=true, knowledge.skills=[]
```
`go test ./...` ok；`pad-window`/`pad-pack`/`pad-depth` 绿。

## 5. 关于"你开着的那几个里面毛都没检测到"

那批窗口是我用 `--shot` 截图模式验证页面时起的：`--shot-page versions` + 默认 `shotDelayMs=3200`，
页面在**异步探测/包扫描完成前**就被渲染了；且当时 `PACK_LAUNCHER_ROOT` 指向 `.tmp` 下的空 launcher，
顶部整合包区没有包 → 看起来整页空白。不是探测坏了。
同一份探测逻辑 headless 跑（不开窗）可以证明：
`pad cli discover --json` → 4 个内核（官方壳 0.2.1-rc.2 / 第三方壳 0.2.0-rc.2 / TUI 启动脚本 0.1.0-rc.7 /
PAD 版本库 0.1.0-rc.7）、2 个桌面端（官方在跑 pid 28032、第三方已停）、TUI、CLI 入口都在。
以后验证不再反复开窗口。
