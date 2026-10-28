# D38 · 实例图标：整合包图片 → 载体图标 → DeepSeek 蓝鲸；绝不画字母

日期：2026-10-27 · 相关：`DECISIONS.md` D38

## 问题

用户截图：启动页实例磁贴是白字 **P**（字母）。要求：
> 有图片就用整合包内的图片，实在不行就用载体 surface 的图标，再不济就用 deepseek 蓝色鲸鱼图标。

## 根因

不是渲染器画字母，而是 `InstIcon.PackImage()` 的"整合包图片"候选里混进了
**项目 workspace 的 logo.png**：
```
workspace/pad-op-tmp/workspace/logo.png   ← 一张字母 P 的图（早先实验留下的项目 logo）
```
`InstIcon.PackDirs()` 原先会把 `workspace` 根、实例根都当"整合包目录"，
于是这张 P 被当成"整合包图片"优先显示。**字母是图片内容，不是字体**，所以"禁止画字母"的
测试全绿也挡不住。

## 修法

1. **收紧"整合包图片"的定义**（`Core/InstIcon.cs#PackDirs`）：只认
   - 实例里显式的 `pack/` 目录
   - 实例来源包目录（`instance.Source.PackPath` 的父目录）
   - 投影出来的 `workspace/.agent-pack/modpacks/*`
   不再把 `workspace` 根 / 实例根当整合包目录 —— 那里的 logo 是项目品牌（可能就是一个字母）。
2. **兜底换成 DeepSeek 蓝鲸**（`Views/InstMark.xaml.cs`）：`kind == Ds` 与异常兜底
   一律用内置 `Assets/api/deepseek.png`（蓝色鲸鱼），不再用 PAD 自己的 `pad-logo.png`。
3. **载体图标优先于蓝鲸**：有 profile/bundle 时先取载体插件自己的图标
   （`BundleIcon.First`，如 dsh-TUI 的鲸鱼 logo）；载体图标是横条 wordmark 时 `Stretch=Uniform`
   留完整图，不裁；整合包图片仍 `UniformToFill`（按图铺满磁贴）。
4. **TUI/网页但没有载体图标**时仍是载体 surface 的 glyph（终端/窗口图标）——用户说的
   "载体 surface 图标"；只有连 surface 信息都没有（Ds）才落蓝鲸。
5. 加 `PAD_ICON_DEBUG=<file>` 诊断旋钮（不设则不写盘）：记录每个 tile 选了哪条分支；
   这次就是靠它定位到 `workspace/logo.png` 的。
6. `pad-window.test.ts` 加回归断言：
   `InstMark` 必须引 `Assets/api/deepseek.png`、不得再用 `pad-logo.png`；
   `InstIcon` 不得再 `Push(inst.Workspace.Path)` / `Push(homeParent)`；必须看 `Source?.PackPath`。

最终优先级：
```
1 整合包图片      display.logo（用户显式） > pack/ 目录 > 来源包目录 > 投影 modpack 目录里的
                 icon.png/logo.png/pack.png 或 pack.json 的 icon/logo
2 载体图标        profile bundles 的插件图标（TUI 的 logo 等），contain 不裁
3 载体 surface    tui → 终端 glyph；web → 窗口 glyph
4 DeepSeek 蓝鲸   Assets/api/deepseek.png（兜底）
（永不画名字首字母）
```

## 证据

- `before-letter-P.png`：用户截图（字母 P）
- `after-carrier-icon.png`：同一台机器、同一实例，现在显示 dsh-TUI 载体图标（像素鲸鱼）
- `after-whale-fallback.png`：没有 profile/载体信息的实例（scratch launcher 里的 pack-demo）
  落 DeepSeek 蓝鲸
- 调试日志（当时）：`pack=...\instances\pad-op-tmp\workspace\logo.png` → 改成
  `pack=-  kind=Tui -> BUNDLE image ...\plugins-meta\_deepseek-harness-tui_dsh-tui\logo.svg`

## 测试

```
bun run agent-pack-dsh/modpack/pad-window.test.ts   ✓（含新增的图标优先级/无字母断言）
```

## 边界

1. `.pack.zip` 压缩包内部 `icon.png` 暂不读取（只读外置/投影出来的图片；zip 内图需要解压，
   留给后续）。
2. 载体图标来自插件自己（`BundleIcon`），因此同一 TUI 版本的实例会共用一张图；这是刻意的。
3. 项目自己的品牌 logo 仍用于侧栏 chip / 卡片封面（D33），只是**不再冒充整合包图片**。
