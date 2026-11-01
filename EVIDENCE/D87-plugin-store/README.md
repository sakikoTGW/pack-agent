# D87 · 插件商店美化（隔离环境验证）

用户：「类似的修复一下插件商店，尽量美化」。改动在 worktree 分支 `pad-lead-verify`，commit `454c568`（**还没合并进 main**）。

## 改了什么
1. **搜索卡片重排**：名称独占一行（回车搜索），版本 / 装到并排带标签，搜索/重置一行；
   右侧常驻统计「共 N 个 · 第 X/Y 页」。
2. **结果卡片**：顶部一行「N 个插件」+「点击一行查看详情并安装」；
   每行 104px：48px 真图标 + 名字 + 发布者 + 两行简介 + 版本/时间 chips + 右侧 `›`，
   行 hover 高亮、行间发丝线（替换原来的 72px 单行简介布局）。
3. **错误卡改人话**：标题「npm 搜索没连上」/「搜索出错了」+ 一句原因
   （如「请求被取消或超时，可能是网络 / 代理 / registry 不通。」）+ 一条建议 + 「重试」按钮；
   去掉 `error[PA033] / --> / | / = help` 这类命令行堆栈。
4. **市场标签去噪**：复用插件页的 noise 过滤（dsh、dsh-plugin、cordis、deepseek-harness… 不再当分类 chip）。

## 验证方式（按 docs/PARALLEL.md）
- worktree `E:\worktrees\pad-lead`（分支 `pad-lead-verify`）+ 私有输出 `E:\tmp\pad-build\lead`；
- 数据根 `E:\tmp\pad-roots\lead`（junction + 独立 pad.json）；
- 没有网络：起了一个**本机 stub npm registry**（127.0.0.1:4789，返回 8 个插件）喂数据；
  再把它指到死端口重拍错误态；
- 截图全部用 PAD 自己的 `--shot … --shot-page market --shot-exit`（后台自截，无前台/无点击/没动正式 PAD），
  进程退出码 0。

## 图
- `01-market.png`：搜索结果页（搜索表单 / 统计 / 结果行）
- `02-market-error.png`：错误态（人话 + 重试）

## 验证
- worktree build 0 warning / 0 error；`pad-window` ✓、`pad-pack` ✓
- **未合并 / 未进正式 PAD**：按 PARALLEL.md，合并与最终统一编译需要你点头（一次）。
