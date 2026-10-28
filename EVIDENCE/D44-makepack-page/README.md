# D44 · 2026-10-28 · 学 PCL 做「创建整合包」页：自己选 核心 / 容器 / 插件

用户：*"学 PCL 做个创建整合包的功能，可以快速把已有的插件配置和容器和核心打包。
当然要有一个页面给自己选，自己组。同时遵守我们的前端设计规则。"*

## 页面（内页 `makepack`；用途 = 导出）

版本选择的整合包区「导出」→ 内页「导出整合包」，一页内自己组；完成后弹保存位置，把 `.pack.zip` 存到用户选的地方（不塞回 PAD 库）。

```
整合包   名字 / 版本 / 图标（点预览块选图）/ 说明
核心     单选：PAD 版本库发行号 + 本机探测到的内核（版本号 mono + 来源小字：版本库/DSH Desktop 自带/启动脚本）
容器     单选：不指定 + 本机桌面端 / TUI（InstMark 图标 + 呼吸绿灯；tooltip 带详情）
插件     多选：现有实例每个 --profile 的 bundles + 探测容器的 home profiles；可手动加 spec
底部     一行短提示（只有缺核心/缺插件时才说） + 取消 / 导出
```

"快速打包已有配置"的预填：选中实例的钉的版本 → 默认核心；该实例当前 profile 的 bundles → 默认勾选；
容器命中实例 home → 默认选中那个容器；换容器时把该容器 home 的插件配置自动勾上（已有的保留）。

## 遵守的前端规则（D42）

- **不摆说明文**：页面上只有分节标题（整合包 / 核心 / 容器 / 插件）和一句条件提示；技术细节
  （来源、profile、home、pid）全部进小字或 tooltip。
- **图像**：容器行用 `InstMark`（整合包图 → 载体图 → 鲸鱼）；图标点选后有预览并 `Fx.Reveal` 淡入。
- **动画/状态**：在跑的容器只亮 `RunLamp` 呼吸绿灯，不写"在跑"；选中反馈走既有 `RowItem`
  （accent 边框 + 左侧竖条 + 位移）。
- **动作**：底部唯一主动作「制作」；成功后 toast 报文件名并回版本选择（版本选择会自动刷新整合包列表）。

## 接线

- `Views/MakePackView.xaml/.cs` 新页；`MainWindow` 工厂加 `"makepack"`，`BackToVersions()` 回内页；
- 版本选择「制作」按钮 `ShowInner("makepack", "制作整合包", "versions")`；
- 导出仍走 Go 的 `packmaker`（至少一个插件；`dsh.version` 必填；插件 spec 写进 `dsh.plugins[]`），
  产物 `<launcher root>/library/packs/*.pack.zip`；
- `pad-window` 新增断言：页面必须有 核心/容器/插件 三块、必须用 `Discover.Run` 和 `Carriers.Bundles`
  （不许又退化成只填名字的弹窗）、MainWindow 必须挂 `"makepack"`。

## 验证

`pad-window` / `pad-pack` 绿；`dotnet build` 0 warning / 0 error；PAD 只构建一次、重启一次（pid 33824）。
