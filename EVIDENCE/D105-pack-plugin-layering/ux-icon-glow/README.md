# D105 UX（用户 2026-11-04 截图反馈）：整合包自有 icon / 去掉「装到哪」/ 实例页已启用发光

scope: task-9（pad-ui）。全部在 scratch 里跑（`E:\pack-agent\.tmp\d105-ux`），未写 live launcher root、未重启 live PAD。

## 用户原话
「把这些换成整合包的icon。然后我觉得不用显示他装在那里了。而是装他的实例整合包那一块装的包会发光」

## 改动
1. 新 `pad/Core/PackIcon.cs`：从 `.pack.zip`/`.pinst.zip` 的 zip 中央目录读根（或唯一一级目录）图标
   （icon.png/jpg/jpeg/svg/ico、logo.*、pack.png），字节缓存到 `<root>/library/icons/packs/<sha16>.<ext>`；
   svg 复用 `BundleIcon.TryRasterizeSvg` 光栅化成 png；`.pack.json` 先看同目录候选、再看 `icon`/`logo` 字段；
   单图标 ≤2MB；webp/坏包/坏 zip 回落 null；14 天旧缓存可清。
2. 版本选择页：行左列 26×26 圆角图标（无图标回落原「包」pill）；**删除「已装到/已建实例」药丸**
   及其计算死代码；「新建实例」与右键导出保留。
3. 实例页「整合包」块：包行加同款图标（账本 Path → Source.PackPath 回落）；**已启用（Enabled=true）的
   包行 accent 呼吸发光**（DropShadowEffect，BlurRadius 14–24 / Opacity 0.35–0.7 往返 1.6s），停用行不发光
   并整体调暗（0.85）；账本药丸（不完整/未登记）与修复/卸载按钮保留。

## 证据（新 build 离屏截图；scratch fixture：`library/packs` 两个包，一个 zip 根带 icon.png、一个无 icon；
实例 ux-demo 里 Icon Pack 已启用、Plain Pack 已停用）

- `versions-pack-icons.png`：同一屏两行 —— Icon Pack 行左列是包自己的圆形图标；Plain Pack 行回落「包」pill；
  两行都没有「已装到/已建实例」药丸，只剩「新建实例」。
- `instance-packs-glow.png`：Icon Pack（已启用）行有明显 accent 外发光；Plain Pack（已停用）无发光且整体调暗；
  两行保留「未登记」账本药丸、卸载/停用按钮；修复按钮（仅 incomplete）未出现。

## 测试
- `pad.tests/PackIconTests.cs`：zip 根 icon 提取+缓存复用、一级目录、无图标 null、坏 zip 不抛、>2MB 拒绝、
  webp 回落、包更新换缓存 key、json 同目录 icon、json icon 字段、json 无图标、svg(STA) 光栅化、14 天清理。
- `pad-window.test.ts`：新增 task-9 断言（PackIcon API/缓存/svg、版本页 IconPath+HasIcon 且无 InstallStateText/
  PackStatePill、实例页 IconPath、PackRow_Loaded+DropShadowEffect+Forever、Enabled 联动调暗）；D104/P2 里旧的
  「已装到/已建实例」断言按新需求改写为「不再显示」，不是删测试。
- build 0/0、dotnet test 126/126、6 个 bun 套件全绿（数字见任务报告）。
