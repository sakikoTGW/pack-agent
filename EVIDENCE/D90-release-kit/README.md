# D90 · 发布形态：p2-p3 旧断言修正 + 一键 kit（zip）

日期：2026-11-02

## 1) p2-p3 旧断言修正（让测试全绿）

p2-p3.test.ts 里两条旧壳时代的断言已经和现在的 PAD 不一致，改成当前事实：

- Pad.csproj 不该再引 WebView2 → 允许 WebView2，但**必须**存在 Views/ProjectDshView.xaml（D77 内置 DSH 视图）；
  旧壳（Host.cs/tauri）仍禁止回来。
- 市场行不是 64px → 市场结果现在是 104 高卡片（D91/D92），改为断言
  ResultList / MinHeight="104" / Row_OpenDetail。

结果：p2-p3 全绿（含 portable unzip kit 用例）；pad-pack / pad-window / pack-catalog 也绿。

## 2) 发布用一键 kit（zip）

新增 scripts/make-kit.ts，un run kit：

- 用 initPortableKit 生成 kit（PAD 单文件 exe + .pack-launcher + 示例整合包 + 打开.bat）；
- **自带 pack-runtime**（bun + bundle + 预编译 pack-index + 包根资源），目标机不需要仓库/node/bun；
- 额外写 安装.bat、使用说明.txt；
- 删掉开发机专用的 .pack-agent-repo（发布包靠内置 runtime）；
- 打 zip：dist/pack-agent-kit-<version>-win-x64.zip（7z 可用走 7z，否则 tar）。

实测：
- un run kit → 51s，产物 dist/pack-agent-kit-0.4.4-win-x64.zip 106.1 MB（压缩后；解压 251 MB / 17 项）；
- 解压到空目录后 pack-agent-for DSH.exe cli instance list → exit 0 []；
- 顶层包含：exe、pack-runtime、打开.bat、安装.bat、使用说明.txt、example-pack、.pack-launcher；无 .pack-agent-repo。
