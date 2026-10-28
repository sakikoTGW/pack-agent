# D43 · 2026-10-28 · 侧栏/关系图的实例图标：项目 logo 不是图标，空白也不许留

用户指着项目侧栏截图（`pad-op-clone` / `pad-op-tmp` 是青色字母 **P**，`本机 DSH` 是空白圆角）：
*"这种地方也是，图像呢？"*

## 病根

`ProjectRail.Chip()` 的候选是「自己设的图 → **项目 workspace 的 logo.png** → 水洗色底」。
- pad-op-* 的 `workspace/logo.png` 本身就是一张字母 P 的图 → 显示字母 P（和 D38 里实例磁贴
  犯的是同一个错：**字母是图片内容**）；
- `本机 DSH` 既没设图、workspace 也没有 logo → 只剩水洗色底 = 空白。

关系图（`RelationsView.IconBrush`）和关系网页（`RelationsWebView.IconDataUrl`）同样把
`Logos.Mark(workspace)` 当图标；工作台（`WorkbenchView.MarkFor`）也是。用户说的"这种地方"就是它们。

## 修法

- 统一一枚：`InstIcon.MarkFile(inst)` = 整合包图（`InstIcon.PackImage`，D38 的候选链）→ 载体图标
  （`BundleIcon.First`，TUI/桌面端组合包自己的图）→ 调用方兜底。
- **项目 workspace 的 logo 不再进候选**（它可能是字母/品牌图）。
- 项目侧栏实例行、树里的实例节点：直接换成 `InstMark` 控件（内部已实现
  整合包图 → 载体图 → DeepSeek 蓝鲸，绝不空白、绝不字母）；侧栏与卡片页/关系图因此是同一枚图。
- 关系图 / 关系网页 / 工作台：`Logos.Mark(workspace)` 全部替换成 `InstIcon.MarkFile(inst)`。
- 顺带把"未在跑"的**灰点删掉**（没有信息就不画）；在跑统一用 `RunLamp`
  （呼吸绿灯：闪 0.9s + 缩放 1.6s + 上下抖 0.7s），侧栏行、树节点、容器列表共用这一个控件。

## 验证

- `pad-window.test.ts` 新增断言：ProjectRail 不得出现 `Logos.Mark`、必须有 `new InstMark`、
  不得再用 `LineDivider` 画状态点、必须有 `new RunLamp`；Workbench/Relations/RelationsWeb 不得再用
  `Logos.Mark`；`RunLamp.xaml` 必须带 `LampDot` + `RepeatBehavior="Forever"`。
- `pad-window` / `pad-pack` 绿；`dotnet build` 0 warning / 0 error；PAD 只构建一次、重启一次（pid 53796）。
