# D59 · 2026-10-31 · 统一线性图标、卡片 hover 层级、项目自定义 logo

用户（在 D58 的四个选项里选了 2 / 3 / 4）：

> "2，3，4"

## 2) 图标统一成开源库的线性风格

- **新控件 `Views/LineIco.cs`**：和 `Ico`（本仓 1024 单位填充 iconfont）并列的一套笔触——
  Lucide 24 网格、`StrokeThickness=1.9`、圆头圆角、**只描边不填充**，颜色跟随 `Foreground`
  （因此按钮 hover/press 时图标会跟着文字一起变色）。
- **`Theme/LineIcons.xaml`（新资源字典）**：25 个图标，路径数据取自
  [Lucide v1.66.0](https://lucide.dev/license)（**ISC 许可**，文件头已署名）：
  `L.Play / L.Activity / L.Cpu / L.Hash / L.Plug / L.Settings / L.Sliders / L.Clock / L.Memory /
  L.Chat / L.Folder / L.Layers / L.Home / L.Package / L.File / L.External / L.Gauge / L.Wrench /
  L.Note / L.Window / L.Image / L.Plus / L.Pencil / L.Stop / L.Share / L.Refresh`。
  `rect / circle / line / polyline` 都转换成等价的 path 极坐标串，`App.xaml` 合并到前面。
- **全部换掉**：项目页统计/卡片标题/对话行/事实行/所有按钮、页头按钮（主页 / + 卡片 / 编辑 /
  版本设置 / 实例与组合包 / 关系 / 去启动·停止）、主页七张卡标题。页头顶部的 tab 图标仍是原
  iconfont（那是全局 chrome，不在本页范围内）。

## 3) 卡片 hover 再给一层投影/描边

`HoverLift(Border)`：每张卡**独立持有**一份 `DropShadowEffect`（blur 26 / depth 6 / 0.10）
和一份 1px 描边刷（`LineBorder` 颜色、透明度 0），避免动画作用在共享的冻结资源上。
悬停：抬 2px（150ms）+ 阴影 0.10→0.22（160ms）+ 描边 0→1（160ms）；离开反向 190ms。
`Fx.Animated=false` 时直接给静止终点值。项目页四张卡与主页所有卡都接上了。

## 4) 每个项目支持自定义 logo

- 入口两个：**点页头的项目 logo**（tooltip "点击换这个项目的 logo"）、项目卡 **「换 logo」** 按钮。
- 流程：`OpenFileDialog`（png/jpg/jpeg/gif/webp/bmp）→ 拷到 `instances/<id>/logo.<ext>`
  （先清掉旧的 `logo.*`）→ `Launcher.SetDisplay(inst.Id, logo: dest)`（和实例设置里「自定义 logo」
  是同一份 `display.logo`）→ `AppState.Reload()` → 重画页头/卡片 → toast「已换 logo」。
- **logo 优先级**（页头与运行卡同一份）：`display.logo` → 正在跑的外壳 exe 真实图标 →
  `InstMark`（整合包图 → 载体图 → 鲸鱼）。
- 只改 `display`，不动 home / workspace / profile。

## 验证

- `dotnet build`：0 warning / 0 error。
- `pad-window` 新增 D59 断言：`Theme/LineIcons.xaml` 存在且含 Lucide/ISC 署名与 19+ 个 `L.*` 键、
  模板为描边（`StrokeThickness` + `StrokeStartLineCap`）、`LineIco` 控件与附加属性、`App.xaml` 合并、
  `SetupHeaderIcons`/`BtnContent`、`HoverLift` 用 `DropShadowEffect` + `LineBorder`、
  `PickLogo` + `OpenFileDialog`、`display.logo` 进优先级、项目卡有「换 logo」；三套测试绿。
- 真实窗口（`project-line.png` / `home-line.png` / `project-hover.png`）：
  页头与项目页全部线性图标；`配置`/`版本设置` 用滑杆图标（齿轮在 13px 会糊）；
  卡片 hover 有抬升 + 阴影/描边（截图 `project-hover.png` 为指针停在运行卡上的状态）。

## 边界

- 线性图标只换内容区；顶部 tab 的 iconfont 仍是品牌 chrome。
- 选图对话框无法在自动化里点选，所以 logo 上传的 UI 流程靠源码断言 + 手工路径保证；
  底层拷贝/`SetDisplay`/优先级三条都在断言里。
- hover 动画只改 TranslateTransform / Effect / BorderBrush 的透明度，不触发重排。
