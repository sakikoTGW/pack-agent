# D96 · 插件页/实例页也做可折叠（+ 插件列表 54 个/页）

用户：「把类似可折叠的做法也用到插件页/实例页」；「应该支持一定程度的下滑，一页显示 54 个左右，下一页就到下一页」。

## 插件页（版本设置 → 插件）
- 搜索框右侧加**旋转箭头**（无文字）：收起/展开「更多操作」——打开文件夹 / 从文件安装 / 下载新插件 / 全选；
  默认收起，过滤 tab、profile 下拉、npm 添加框、列表始终可见。
- **分页：54 个/页**：列表底部出现上一页 / `x / y` / 下一页；过滤/搜索/切 profile 时回到第 1 页。

## 实例页（版本设置 → 概览）
- 身份卡右上角加**旋转箭头**：收起/展开「个性化 + 快捷方式 + 高级管理」三张次要卡片；
  身份卡（图标/名字/版本）始终可见。

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`
- 后台自截：`--shot-page instance-plugins` → `01-plugin-collapsed.png`；`--shot-page instance` → `02-instance-collapse.png`；退出码 0
- 说明：本机 DSH 只有 12 个插件（<54），所以分页器暂不显示；超过 54 个才出现。

## 状态
- 分支 `pad/lead-repo-images`，**未合入 main**（主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`）。
