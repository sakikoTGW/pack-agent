# EVIDENCE · 关系图视觉密度优化（2026-10-27，两轮）

改动：`pad/Assets/web/page.html`（渲染层重写）、`pad/Views/RelationsWebView.xaml.cs`（payload 视觉通道字段）、
`pad/Core/Graph.cs`（+`AgeMinutes`、会话规模三事实）、`pad/Core/ViewCatalog.cs`（+按占比视图）、
`pad/MainWindow.xaml.cs`（shot 分发认 `relations-*`）。
原则：**图面不加字**——新增信息全部走颜色 / 形状 / 图标 / 宽度 / hover。

## 第一轮：信息通道

| 截图 | 证明 |
|---|---|
| `sessions-expanded.web.png` | hover 提示（完整标题+类型+时间）、邻接高亮（祖先链+子树保持）、实例图标（data URL）、层级带标签 |
| `sessions-two.web.png` | 全树两层展开：热度点、词边界折行、展开徽标 |
| `projects.web.png` | "未落盘"幽灵卡（虚线+空心点）、虚线边、四个层级带标签 |
| `projects-table2.web.png` | 表格行内数据条（0 值不画条） |

## 第二轮：规模、搜索、占比

数据源：`Graph.AgentsOf` 读会话缓存时顺带取 `contextPressure(surfaceTokens/contextWindow)`
与 `contextTimeline(archived)` ——和 `pad cli inspect` 同源，零额外 IO。

| 截图 | 证明 |
|---|---|
| `sessions-two.web.png`（新） | 卡片底部规模条：宽 ∝ 上下文 token（相对全树最大） |
| `icicle.web.png` | 按占比镜头：宽 = token（无数据均分/消失），颜色 = 热度，大段在左，贴顶；点一段聚焦 |
| `sessions-search.web.png` | 搜索 "dsh"：5 处命中 + 祖先保持，其余淡化，计数贴框边，祖先自动展开 |

交互（截图拍不到的部分）：图卡片点击 = 聚焦该支（与侧栏点行同语义），点空白取消，Esc 清搜索/聚焦；
tooltip 增补"上下文 N token · 窗口 p% · 压缩 N 轮"与"同源 DSH"（同指纹实例）；宿主落盘图侧发起的 focus。

复现（exe 在 `pad/bin/debug/net9.0-windows/pack-agent-for DSH.exe`）：

```
pack-agent-for DSH.exe --shot <out>.png --shot-page relations-sessions-two     --shot-exit
pack-agent-for DSH.exe --shot <out>.png --shot-page relations-icicle           --shot-exit
pack-agent-for DSH.exe --shot <out>.png --shot-page relations-sessions-search  --shot-exit
pack-agent-for DSH.exe --shot <out>.png --shot-page relations                  --shot-exit
```

## 第三轮：侧栏按视角分层（D35）+ 全程动画

问题：关系页走 `ShowInner` 内层页（返回箭头 Chrome + 自带第二个侧栏实例），
从管理页进来 = 整个窗口换一层；且两个侧栏行语言不统一（一个有 logo、一个纯文字）。

定案（D35）：**三个页面三个视角**——管理=项目视角（实例清单），关系=工作区视角
（层级树），实例与组合包=整合包视角。不强行说"同一个侧栏"。
容器仍在舞台层（MainWindow 持有 ProjectRail，页面 `AttachRail` 灌入自己的视角），
保留两样东西：**行语言统一**（logo 芯片 + 名称 + 状态点 + 展开箭头，树形态也有 logo 了）
和 **FLIP 连续性**（视角切换/展开收起时，同 key 行从旧位置滑到新位置，新行错峰淡入）。
关系页脱离内层页（标签条不动），返回入口在内容区头部（`‹ 管理`）。

动画清单：侧栏 FLIP（340ms Out，错峰 14ms/行）、新行淡入+上浮、按压变虚、
悬停水洗色、聚焦行就地高亮；图内新节点/新边淡入（老节点原地不动）；
表格行错峰淡入；页面切换 `Fx.PageIn`；侧栏容器收起/展开 `WidthTo + Slide`。

| 截图 | 证明 |
|---|---|
| `stage-relations.png` | 标签条在"管理"上、舞台侧栏=树形态、返回芯片；无第二种 Chrome |
| `stage-manage.png` | 同一个侧栏=列表形态（同一实例、同一位置），卡片页全宽 |
| `stage-launch.png` | 无侧栏页面整块退场（左缘无卡片列） |

修复的拦截性 bug：shot 分发的 arm 写成 `"relations" or _ when StartsWith("relations-")`，
`or` 组合里 `when` 是整个 arm 的守卫——`relations`（无后缀）被守卫否决落到 `TabLaunch`，
标签胶囊永远停在"启动"。改成显式布尔守卫（旧代码里同样存在，被内层页 Chrome 掩盖）。

复现：`pack-agent-for DSH.exe --shot <out>.png --shot-page relations --shot-exit`（manage/launch 同理）。

## 顺带修复（三轮累计）

- 兄弟卡片重叠（nodeSize 214 < 卡宽 276）、HTML 折叠表格缩进空格
- `persist()/setFocus()` 里 `root` 未定义的引用错误、"└ "前缀冗余
- 图展开 id 与侧栏 id 在第 6 项之后不一致（CAP 截断所致）
- 展开徽标与卡片聚焦监听同元素冲突（`stopImmediatePropagation`）
- shot 分发 `when` 守卫陷阱（见第三轮）
