# D81 · 命名修正：插件归插件，整合包归整合包

用户纠正：「组件包 → 不叫组件包，叫插件！」并确认「插件归插件、整合包归整合包」。

## 结论
| 概念 | 叫法 | 态 |
|---|---|---|
| npm 包（dsh-base / dsh-web-app / dsh-tui / pad-gateway…） | **插件** | 持有态：叠在某个 profile 里，一个实例可以有多个 |
| 可移植打包产物（.pack.zip / .pack.json，含 skills/rules/MCP…） | **整合包** | 持有态：装进实例后可启停 |
| 实例进程 | **运行态**：在跑 / 已停 | 与上面独立 |

## 改了什么
- 管理页那块从「整合包」改回 **「插件」**：插件行、npm 输入占位「插件（npm 包名 / 本地路径）」、
  空态/提示/全部更新文案；`LaunchPolicy` 的 kind 标签 = 插件；任务日志 job 标签（plugin-add/remove/update）= 装/移除/更新插件；
- **整合包独立成一行**：插件区下面单独的「整合包 + 装整合包…」，并注明"整合包不是插件，装进来后到实例页启停"；
- 术语同步：设置页 profile 提示、dsh-tui 插件对话框、钉版本 toast「插件未验证」、下载页 pnpm 装插件、任务页文案、
  版本设置/管理页标题「实例与插件」、空状态、LaunchPolicy/Models 标签。
- 实例状态保持只有 在跑 / 已停。

## 实拍
- `EVIDENCE/D81-plugin-naming/01-manage-plugin.png`：实例列表（在跑/已停）、插件区、整合包一行、正在聊的

## 验证
build 0 warning / 0 error；pad-window / pad-pack / pad-project / pad-depth / pad-headless 全绿。
