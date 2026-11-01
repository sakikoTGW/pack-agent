# D73 · 砍掉「项目自动收编」，现在页只读 PAD 项目

用户确认：「砍掉自动收编，现在页只看 PAD 项目」。

## 为什么砍
隔离由**实例/home** 提供：项目要"窗口里只有这个项目的东西"，就必须有自己的 home。
自动收编只是把全局 home 的目录路径写成项目标签——不改 home、不迁会话，
打开全局 DSH 窗口照样能看到所有东西。它解决的是"迁移旧工作区"，不是主链路，所以没用。

## 改了什么
- 删 `Core/ProjectAdopt.cs`（Scan/Materialize/AdoptGroup）；
- 删 `pad cli project adopt` 子命令与 usage；
- 删 UI：`现在`页右上「自动收编」按钮、侧栏底部「自动收编已开启」、整个「自动收编」页（ShowAdopt/PaintAdopt*）；
- 删 `ProjectRail.AdoptClicked` / `WorkbenchView` 的 adopt 分支（Home/Header/Cards/Transition/FillRail/Load）；
- 测试：`pad-project` 去掉 adopt 源码契约与实跑用例，并加"不得再出现 adopt 代码"的反向断言。

## 现在页的新数据口径
只看 **PAD 项目实例**：`projects → ResolveProjectInstance → NowProbe.Read(inst.Home)`，
且只保留 cwd 落在该项目 `Workspaces` 里的会话（实例 home 里别处的会话不算这个项目）。
不再扫 `pad.json#launch.dshHome`，不再出现「未归类」伪项目。

## 实拍
`01-now.png`：无收编入口；现在页只有 PAD 项目 ev-project 的一条会话（ws-frontend，15 小时前，刚完成）。
项目没有会话时为空是**正确状态**（说明这个项目还没跑过 DSH），不是 bug。
