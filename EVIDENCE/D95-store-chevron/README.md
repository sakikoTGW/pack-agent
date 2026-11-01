# D95 · 商店折叠：去掉文字，改成旋转箭头

用户：「收起太难看。当文字/解释不是必要的时候，删除它，用前端表示」。

## 改动
- 折叠按钮从 `筛选 ▾ / 收起 ▴` 文本按钮，改成**纯图标**（`IcoBtn` + 自绘 chevron）：
  - 收起时箭头朝下；展开时箭头旋转 180°（`RotateTransform`），没有文字；
  - ToolTip 仍保留「高级筛选」给鼠标用户。
- 删掉头部右侧重复的 `共 N 个 · 第 x/y 页` 说明：结果区已有 `N 个插件`，底部已有分页器，这行是多余解释。
- 结果是收起态只剩：标题 / 箭头 / 搜索框 / 搜索按钮；展开态才显示 版本/装到/重置/排序/分类/只看已安装。

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；后台自截 `--shot-page market` / `market-more`，退出码 0
- 产物：`01-collapsed-icon.png`、`02-expanded-icon.png`
