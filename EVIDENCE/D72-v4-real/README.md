# D72 · 「现在」页接真实 DSH home + 彩色项目块 + 悬停预览

用户指着 v4 原型里「现在 + 悬停预览」那版说「这版」。之前几轮的错误：
D69 做出来了结构但只扫 PAD 自建实例（现在页 0/0/1），被我 revert；现在恢复 D69 并按这张图补齐三样：

## 1. 真实数据源（`Core/NowProbe.cs`）
直接读 DSH 的会话投影缓存 `<home>/storages/session_projcache/sessions/*.json`（只读）：
标题、cwd、preset、model、轮/步、`openStep`（在跑）、`userQuestions`（等你输入）、
`pendingCalls`（工具调用）、`turnOutline.draft`（最近一轮输出）。
数据来自 `pad.json#launch.dshHome`（本机 `E:\DSH-Home`）+ 各项目实例 home；按 cwd 前缀映射到项目，
没归到项目的用 cwd 目录名当临时项目名（等自动收编后变正式项目）。
本机实测：**2 等你输入 / 0 在跑 / 79 刚完成**（真实会话标题：打开PAD (3)、插件引用元素显示异常 (1)…）。

## 2. 彩色项目块 + 自动收编口径
行左侧改成项目色块 + 首字（v4 同款，颜色由名字稳定哈希到 8 色调色板）；侧栏项目行同样处理。
右上按钮直接写口径：`自动收编：19 工作区 → 19 项目`（读 workspace.json + ProjectAdopt.Scan，只读）。

## 3. 悬停预览（v4）
在「现在」的每条上悬停 → 弹出预览卡：标题 + `项目 · preset · N 轮` + 「最近一轮」输出气泡 +
工具调用 chip（有 pendingCalls 时）+ 底部「点一下 → PAD 把这窗切到对这个对话」。
去掉行 ToolTip（避免和预览抢显示，符合"悬停=预览"原则）。
点一行：已归项目的进项目页；未归的提示先自动收编。

## 证据
- `01-now-real.png`：真实数据版现在页（2/0/79，彩色块，右侧自动收编 19→19）
- `02-hover-preview.png`：悬停预览（最近一轮输出气泡 + footer）
（`03-hover.png`–`13-hover.png` 是调参过程中的抓图，最终以 01/02 为准）

## 验证
build 0 warning / 0 error；新 PAD（pid 7896）在跑；pad-project 契约测试仍绿。
差异：演示图里 2/3/3 与 qwxy平台 等项目名是示例数据；我们显示的是这台机器的真实会话。
hover 的「最近两轮」目前只有最近一轮 assistant 输出——投影缓存里没有完整 user 消息。
