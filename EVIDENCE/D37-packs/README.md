# D37 · 版本选择 = PCL 语义（插件包 / DSH 版本），实例来自插件包或内核

日期：2026-10-27 · 相关决策：`DECISIONS.md` D37

## 目标语义

| PCL | PAD / DSH | 说明 |
|---|---|---|
| 常规版本（1.21.8） | **DSH 版本**（`@deepseek-ai/dsh@x.y.z`） | 纯内核 |
| 可安装 Mod / 整合包 | **插件包** = 组件包 + 对应内核 | `pack.json` 的 skills/mcp/plugin/rule/command/hook + `dsh.version` + `dsh.profile` |
| 版本（列表一行） | **实例** | 启动方案（内核 或 插件包）+ 一份 `DSH_HOME` + 一个工作区 |

用户决定：一个实例只挂一个包；缺内核时**跳窗询问**是否下载（不静默下载）；保留「实例」叫法。

## 实现

- **包发现/解析（TS）**：`agent-pack-dsh/modpack/pack-catalog.ts`
  `packagent dsh launcher packs --json [--query] [--roots a;b] [--deep dir]`
  默认根（launcher root / `library/packs` / exe 旁 / Downloads·Desktop 一层）、盘根名字
  含 pack/modpack/dsh/harness 的目录（≤400/盘、再下 2 层）、`--deep`（深度 4 / 20000 目录）；
  文件上限 200、总耗时 <5s；`.pack.zip/.pinst.zip` 只读 zip 中央目录取根 manifest；
  坏包进 `errors` 不抛。**只读**：不导入、不建实例。
- **投影（TS）**：`pack-ops.packProject` 现在同时支持 `.pack.json` 与 `.pack.zip`
  （zip → `readPackZip → projectPack(from: extractedRoot)`，投影完清理解压目录）。
- **C# 事实层**：`Packs.PackCard` / `Packs.Catalog`（完整捕获 stdout + `FirstJson` 取外层 JSON）、
  `Packs.LibraryDir` / `Packs.CollectToLibrary`；`Launcher.CreateInstance(name, version, profile)`；
  `Launcher.CreateFromPack`：`CreateInstance` → 写 `instance.json#source` → `Packs.Project`。
  建实例前 guard：`.pack.json`/`.pack.zip` 放行（内核判空 PA012）；`.pinst.zip` 拒绝
  （PA012 `must be imported` + help `pad cli import "<path>"`，实例目录不建）。
- **UI**：`Views/VersionsView` 右栏顶部「插件包」组（上面），DSH 版本/实例的 profile 组在其下
  （对应 PCL 上下两栏）；每行显示 `内核 x · 组件 N · profile y · 来源` + 「用这个包新建实例」；
  缺内核弹 Yes/No → `InstallRelease` → 自动重试；pinst 行路由到 `Packs.Import`；
  导入/拖包前先把包复制进 `library/packs`（导入会消费原文件，库副本让目录持续可见）。
- **CLI**：`pad cli pack list [--json] [--out]`、`pad cli instance create --pack <id|path> [--name] [--profile]`。

## 本机实测（最终构建，可直接复跑）

```powershell
$exe='E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\pack-agent-for DSH.exe'
$env:PACK_LAUNCHER_ROOT='E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher'
$env:PACK_AGENT_REPO='E:\pack-agent'
& $exe cli pack list          # packs-real.txt：本机盘根启发搜到 7 个包（仓库导出/测试 fixture）
& $exe cli pack list --json   # packs-real.json
```

真实 scratch launcher（`E:\tmp\pad-pack-create`，versions 用 junction 指到真实版本库）：
```powershell
# .pack.json 路线
& $exe cli instance create --pack example --name pack-demo2
# exit 0，5.5s：instance.json dsh.version=0.1.0-rc.7 / lastProfile=web /
# source={kind:pack, packId:example, …}，workspace 投影出
# .agent-pack/modpacks/pack-agent-modpack-example（pack-demo2.instance.json）

# .pack.zip 路线（同一个 example 打包成 zip，catalog 里两行 kind 不同）
& $exe cli instance create --pack '<root>\library\packs\example.pack.zip' --name pack-zip-demo
# exit 0：source.packPath=example.pack.zip，workspace 同样投影成功（pack-zip-demo.instance.json）

# .pinst.zip 是实例导出包，不投影：exit 1，PA012，instances/pinst-demo 不存在
& $exe cli instance create --pack '<root>\library\packs\demo.pinst.zip' --name pinst-demo
```
缺内核路径（scratch root 不挂版本库）：`error[PA001]: that release is not installed`，
`instances/x` 不创建（`pad-pack.test.ts` 断言）；
找不到包：`error[PA012]: no such pack` + help `pad cli pack list`。

UI 证据：`versions-page.png` ——「版本选择」右栏顶部「插件包」组（本机搜到 8 个，含
`example 0.1.0 / 内核 0.1.0-rc.7 / 组件 1 / profile web / library`），左侧实例列表有
从包建出来的 `pack-demo`。

## 测试

```
bun run agent-pack-dsh/modpack/pack-catalog.test.ts   ✓（发现/解析/坏包/--query/--deep
                                                       + .pack.zip 投影、.pack.json 老路径回归）
bun run agent-pack-dsh/modpack/pad-pack.test.ts       ✓（pack list JSON、缺内核 PA001 不建目录、
                                                       找不到包 PA012、源码回归）
bun run agent-pack-dsh/modpack/pad-window.test.ts     ✓
bun run agent-pack-dsh/modpack/drop-scan.test.ts      ✓（补齐 pad-pages 注册表键后由红转绿）
bun run agent-pack-dsh/modpack/import-pack.test.ts    ✓
bun run agent-pack-dsh/modpack/launcher.test.ts       ✓
bun run agent-pack-dsh/modpack/launcher-api.test.ts   ✓
bun run agent-pack-dsh/modpack/catalog.test.ts        ✓（pack-index FTS 链路未被改动）
```

已知既有红（与本次无关）：`p2-p3.test.ts` 的「Pad.csproj 不该再引 WebView2」——HEAD 的测试
禁止 WebView2，而工作区里另一条并行工作线把它加回来给「关系」页用（`RelationsWebView` 正在用），
两边还没收敛；等那条线定稿会一起改。

## 边界（诚实）

1. 一个实例只挂一个包（用户决定）；底层 allow/deny 多包能力仍在，但入口只走单包。
2. `.pinst.zip` 不投影，走 import（它是实例导出，不是包）；C# 在建实例前挡住并给 help，
   零写入。
3. 包发现是有界的（≤200 文件/5s、盘根命名启发、`--deep` 显式才深扫）；不在命名目录里的包
   需要 `--roots` 或 `--deep`。
4. 同名包的 `.pack.json` 与 `.pack.zip` 会各列一行（kind 不同，未去重）。
5. `--query` 只是目录过滤，没接 pack-index FTS；实例工作区内的 FTS 检索链路未改。
6. 缺内核的「跳窗下载」只覆盖 UI；CLI 路径按 PA001 报错退出（脚本自己决定装不装）。
7. **既有红（与本链路无关）**：`p2-p3.test.ts` 的 WebView2 断言冲突（见上）。
