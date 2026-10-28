UI：管理页显示正在跑的节点 (2026-10-23 02:24)

位置：管理页头部第二行，跟在「还没启动」后面
  示例：· pad-op-tmp · 0.1.0-rc.7
          还没启动   (运行中 web:51578)

数据：实例 home 下 .pad/nodes.json（PAD 自己的账本），只显示 pid 仍存活的条目。
形态：只给事实（profile:port），没有"待验收/通过"这类判定（D18）。
验证：用 --shot --shot-page manage 自渲染，EVIDENCE/UI-nodes/manage-running-line.png

过程中发现并修掉一个真 bug：
  Ledger.Load 原先用 JsonDocument.Parse(File.ReadAllBytes(path))。
  第一次截图时那行没出现——因为测试账本由 PowerShell 写出、带 UTF-8 BOM，
  而解析字节流不会跳过 BOM，整个文件读成空数组，**正在跑的节点会全部丢失**。
  改为 File.ReadAllText（会按 BOM 识别编码）后再解析。
  这和之前那个坏掉的 profiles/web/package.json 是同一类问题。
