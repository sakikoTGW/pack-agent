# D46 · 2026-10-28 · 已建好的包也要能导出（右键导出，不加新按钮）

用户："那我们已经建立好的包，这不没办法导出了吗？你又陷入设计误区了，不要想着加东西解决。"

上一版只给「新组一个包」做了导出：`makepack` 页 + 保存位置。已经存在的整合包（`library/packs`
里的 `.pack.zip` / `.pack.json`，也就是版本选择整合包列表里那些行）反而没有导出口——
这是设计死角，不是缺功能。

## 修法（不新增可见界面）

整合包列表每一行**右键 → 导出…**（`ContextMenu`，默认隐藏，PCL 也是这么放的）：
- 弹 `SaveFileDialog`，默认文件名用原文件名，起始目录 Downloads；
- 直接把原文件 `File.Copy` 到用户选的位置（`.zip` 就存 zip，`.pack.json` 就存 json）；
- toast 报文件名；不重新组包、不改库里的那份。

没有新增按钮、没有新增页面、没有新文案；行的左键行为（选中/新建实例）不变。
需要"从已有实例的配置组一个新包导出"时，仍走整合包区「导出」→ 内页（预填当前实例的核心/profile/插件）。

## 验证

`pad-window` 新增断言：整合包行必须有 `<ContextMenu>` + 「导出…」、`VersionsView` 必须有
`ExportPack_Click` 且 `File.Copy`；`pad-window` exit 0、`pad-pack` 绿；`dotnet build` 0/0；
PAD 单次重启（pid 68796）。
