# 微信公众号写法蒸馏（面向 Agent Modpack）

> 读者：Claude Code / Codex 重度用户  
> 目的：发一篇讲清 pack-agent 的公众号  
> 日期：2026-07-18

---

## 0. 研究方法与诚实边界

### 做了什么

1. **GitHub**：读了同类/相邻项目 README 原文（openskills、skillport、anywhere-agents、vercel-labs/skills，以及 agents-anywhere/skill 商店系描述）。
2. **微信**：用多组 `site:mp.weixin.qq.com` 关键词变体批量检索；对可拉取的文章做全文/长摘录；对其余保留「标题 + 链接 + 钩子/结构」。
3. **交叉**：对照先前已精读样本（c4605 MCP→Skill、Safi MCP portfolio、陈广亮五层架构、webopen/mk/矩媒）。

### 做不到什么（必须说清）

- 微信全文常超时/需登录；**不可能对 100 篇逐字精读**。
- 本报告的「100+」= **高相关条目目录**（标题可核、主题合格）+ **约 30–40 篇深读/长摘录**。
- 营销软广（纯星数吹、赋能闭环、无命令）进目录但标「低」，蒸馏时降权。

### 样本规模（本轮）

| 层 | 数量（约） | 用途 |
|----|-----------|------|
| 微信高相关目录条目 | **110+** | 标题句式、选题分布 |
| 深读 / 长摘录 | **35+** | 结构、钩子、语气 |
| GitHub README 深读 | **4+ 核心竞品** | 产品怎么自称、命令怎么放 |

完整目录见文末附录 A（按主题分组）。

---

## 1. GitHub：他们怎么讲自己（对标 pack-agent）

### 1.1 四家定位原文

| 项目 | 一句话（README） | 单元 | 和 pack-agent 的差 |
|------|------------------|------|-------------------|
| **openskills** | *Universal skills loader… Think of it as the universal installer for SKILL.md.* | 单个/一批 skill | 装 skill，不封「角色+MCP+罐头」 |
| **vercel-labs/skills** | *The CLI for the open agent skills ecosystem.* | skill → 多家 agent 目录 | 安装器/生态入口，非角色快照 |
| **skillport** | *Bring Agent Skills to Any AI Agent… Manage once, serve anywhere.* | skill 生命周期 + MCP serve | SkillOps，偏校验/检索/serve |
| **anywhere-agents** | *One config for every agent — Claude Code, Codex, and whatever comes next.* + **packs** | AGENTS.md + rule/skill packs | 最接近「pack」隐喻，但是 **config sync / bootstrap**，不是便携 `.pack.json` 角色快照 |

**anywhere-agents** 原文级痛点（可学）：

> You use more than one agent. … Without anywhere-agents, three configs to keep in sync.  
> You work across many repos. … copy-paste between repos and watch them drift.

**openskills** Quick Start（可学：前屏就给命令）：

```bash
npx openskills install anthropics/skills
npx openskills sync
```

**pack-agent 对外该钉的差（基于事实）**

- 单元 = **一个 agent 角色**（skills + rules + MCP + 经验罐头），不是单个 skill。  
- 载体 = **一份 `.pack.json` + bundle**，可拷走 / 可 eject / 有 ledger。  
- 投射 = **adapter 表**进多家 harness；默认项目作用域。  
- 可选 = **瓶口 fidelity L1–L4**；缺录标 L1。

公众号里提竞品：一句差即可，别开撕。

---

## 2. 微信样本：主题分布（110+ 目录的统计印象）

高相关流量几乎全挤在这些桶里：

| 桶 | 占比观感 | 代表标题形态 |
|----|----------|--------------|
| Skills 完全指南 / 万字 | 高 | 「【万字长文】Claude Skills完全指南」 |
| 安装合集 / 命令表 | 高 | 「OpenClaw技能包大全」「MCP 工具合集（附安装命令）」 |
| Superpowers / 工作流 skill | 高 | 「34k Star！…superpowers开源」 |
| Harness / 源码拆解 | 中高 | 「扒完Claude Code源码…Harness」 |
| MCP 实战 / 自建 | 中 | 「使用Go开发MCP Server, 太简单了!」 |
| Codex / AGENTS.md 配置 | 中 | 「告别Claude代码降智！…附完整AGENTS.md」 |
| 踩坑 / 亲测 / 体感 | 中 | 「亲测」「变蠢+摆烂」「Remote Control 实测」 |
| 跨工具同步 / 拷目录 | 中低但**与本项目最贴** | 「从 MCP 到 Skills」「复制 skills 目录即可」 |
| 开源工具首发（个人） | 中低 | OpenSkills 介绍文、自家 MCP |

**结论**：读者已被「装 skill / 写 skill / 拆 harness」喂饱。  
pack-agent 必须回答：**你不是又一个 skill 安装器，你是角色整合包。**  
跨工具同步那一小撮文，是天然入口。

---

## 3. 蒸馏：标题句式（带例）

从 100+ 标题里抽频率高、可复用的：

1. **数字 + 开源** — `7.2k Stars！…OpenSkills开源` / `34k Star！…superpowers`
2. **万字/完全指南** — `【万字长文】Claude Skills完全指南`
3. **说白了/一句话钉死** — 正文常用；标题偶用「不是X，是Y」（慎用，你讨厌套话时少用）
4. **从 A 到 B 演进** — `从 MCP 到 Skills 的技术演进`
5. **告别痛点** — `告别 AI 编程工具Agent配置烦恼` / `告别Claude代码降智`
6. **亲测/实测** — `亲测` / `深度评测 + 三方方案完整对比`
7. **源码/扒完** — `扒完Claude Code源码…` / `源码揭秘：一个 while 循环…`
8. **合集+附命令** — `必备！最常用的 MCP 工具合集（附安装命令）`
9. **场景钩子** — `陪女朋友逛街时，我带上了 Claude Code V2.0`
10. **像 X 一样** — 技术文较少当主标题；更多在正文类比（USB-C、器官/教程）

**给 pack-agent 的标题候选方向（重度用户向）**

- 偏痛点：`换 Claude Code 到 Codex，你的 skills 和 MCP 还在吗？`
- 偏产品：`我开了个 Agent 整合包：一份 .pack.json 装进多家 harness`
- 偏差：`OpenSkills 装 skill；这个装整个 agent 角色`
- 偏诚实：`装成功了，上下文却是空的——我怎么把 Hermes hook 修对`

星数标题：你们 star 不高就别硬蹭「xxk Star」。

---

## 4. 蒸馏：开头钩子（带原文）

### 高分钩子类型

**A. 场景一句 + 产品一句（Safi 型）**

> 46 tools, one Sanctum token, three abilities. Claude Code now manages my whole portfolio without me ever touching the admin panel.

**B. 听起来绕，但实用（c4605 型）**

> 最近写了个 skill，功能是把 MCP server 转成 Claude Code skill。听起来有点绕，但其实挺实用的。

**C. 说白了钉死（Skills 指南型）**

> 说白了：Skills 不是"自定义命令"，而是一套工作流编程语言。

**D. 演进/版本叙事**

> 继前两篇协作方案文章后，我们迎来了 3.0 版本的架构升级：从 MCP Server 切换到 Skills 架构。这不是简单的版本迭代，而是解决了实际痛点的调整。

**E. 源码反差**

> Claude Code 的核心架构，就是一个 while 循环。没有复杂的 DAG，没有花哨的状态机……

**F. 跨机器同步金句（与本项目极贴）**

> Skills 方案：直接复制 `cp -r ~/.claude/skills/codex ~/new-machine/.claude/skills/` # 完成！

### 低分开头（蒸馏时剔除）

- 「随着 AI 快速发展…」
- 纯星数恐吓式开场后无命令
- 「重磅发布」企业通稿腔（SOAR MCP 一类）

### pack-agent 可用钩子（基于仓库事实）

1. **假成功**：Hermes hook 报装成功，经验罐头没进上下文（PACK_SPEC 有原文）。  
2. **跨工具**：Claude Code 一套目录，Codex 另一套；拷 skill 漏 MCP。  
3. **角色边界**：一个项目多个 agent；裸 export 拒绝。  
4. **卸载**：装完不知道卸哪 → ledger + eject。

---

## 5. 蒸馏：文章骨架（圈子里反复出现的 3 套）

### 骨架 α — 工具发布 / 演进文（最适合你们）

1. 钩子（场景或假成功）  
2. 为什么 / 痛点（分节，带数字或命令对比）  
3. 我做了什么（一句话定位 + 边界：不是 skill 商店）  
4. 真实工作流（agents → export → install → show/list → eject）  
5. Gotchas（Hermes / 全局配置默认不写 / Cursor 跳过）  
6. 怎么自己跑（npx + 最小命令）  
7. 边界 / 不做什么（L1–L4、换模型会漂）  
8. 链接收尾  

证据：c4605、Safi、MCP→Skills 3.0、OpenSkills 介绍文。

### 骨架 β — 完全指南 / 万字

目录式：是什么 → vs MCP → 创建 → 最佳实践 → 资源。  
**你们别整篇走这条**（红海）；最多借「vs 表」一节。

### 骨架 γ — 源码/Harness 拆解

while 循环 / 八机制 / Harness=Model 之外一切。  
可借一句：**Harness 是壳，pack 封的是 prefunction**——但全文别变源码考古。

---

## 6. 蒸馏：正文手法清单

| 手法 | 微信高频怎么用 | pack-agent 怎么用 |
|------|----------------|-------------------|
| **前 10～15 行给入口** | GitHub / `npx` | npm + `npx … install` + GitHub |
| **对比表** | Skills vs MCP vs Subagent | 拷目录 vs OpenSkills vs Agent Modpack |
| **命令可复制** | clawhub 四条万能命令 | detect / export --agent / install / eject |
| **假成功/静默失败** | Remote Control 实测、hook 不生效 | Hermes hook 形状错误那段 |
| **「它不做什么」** | webopen；Superpowers「小脚本别用」 | 默认不写全局；缺 capture 标 L1 |
| **一层一句类比** | Tool=器官 Skill=教程；MCP=USB-C | Harness≈启动器/版本；Pack≈整合包（正文一句，别当全文主轴硬吹） |
| **版本演进叙事** | 2.0→3.0 MCP→Skills | 「手拷 → skill 安装器 → 角色 pack」 |
| **决策树/何时用** | 陈广亮五层 | 何时用 pack、何时只用 openskills |
| **安全默认** | skill-vetter 先装；confirm=true | `--global-config` opt-in |

---

## 7. 蒸馏：语气与禁忌

### 重度用户吃的语气

- 短句、命令、路径、退出码/协议形状  
- 「我亲测」「报成功但…」  
- 承认边界（未测试、默认跳过、换模型漂移）

### 禁忌（样本里差的那批）

- 赋能 / 闭环 / 一站式 / 重磅  
- 无证据的「效率十倍」  
- 通篇概念无一条可跑命令  
- 只吹 star 不讲差  

### 你们自己仓库里已经写对的语气（直接可进公众号）

PACK_SPEC Hermes 段：

> hook 配置写对了，装的时候也报"成功"，但经验罐头内容从来没有真正进过 Hermes 的对话上下文。

这比 80% 营销号开头更像「真文」。

---

## 8. 给 pack-agent 的最终配方（执行用）

**读者**：Claude Code / Codex 重度用户（已定）。

**主问题（读者打开后 3 秒要懂）**  
「skills/MCP/角色换工具就散；我打成一份 `.pack.json`，adapter 装进多家，能卸。」

**骨架**：α（工具发布），不要 β 万字指南。

**必写 4 块**

1. 钩子：假成功或跨工具散掉  
2. 差：vs 拷目录 / vs OpenSkills（装 skill）/ vs anywhere-agents（bootstrap 配置）  
3. 工作流命令：init → export --agent → install → show/list → eject  
4. Gotchas + 不做什么  

**MC 整合包**：可用，但放在「心智模型」一小节或结尾类比；**标题别只靠 MC 梗**（重度用户更吃「跨 harness / 角色 pack / 假成功」）。

**长度**：2500～4000 字公众号为宜；命令块 ≥3。

**下一动作**：正文见 `drafts/wechat-agent-modpack.md`；版面与一次跑完流程见 `drafts/wechat-pipeline.md`（Terminal Brief）。

---

## 附录 A. 微信高相关目录（节选，完整检索会话中累计 110+）

### Skills / 指南

1. Claude Code Skills 完全指南… — https://mp.weixin.qq.com/s/M-K8cDwLhpID1gkcZOdUkQ  
2. 【万字长文】Claude Skills完全指南 — https://mp.weixin.qq.com/s/x9UpqjuYzLb7I2ZZ932bNg  
3. 最适合新手的 Claude Code、MCP、Skills 全套教程 — https://mp.weixin.qq.com/s/8iM16IfgKWvESx8ZaOg5Uw  
4. Agent Skills：把你的 Claude Code升级为定制的工作平台 — https://mp.weixin.qq.com/s/oX3ADDWuKVct0-kAPUnXmw  
5. Agent Skills 终极指南 — https://mp.weixin.qq.com/s/jUylk813LYbKw0sLiIttTQ  
6. 万字译文：Anthropic Skills 构建指南 — https://mp.weixin.qq.com/s/t1YQBgt6lWKtGYejYFP1Gw  
7. [小山译] Claude Skill 编写与实战指南 — https://mp.weixin.qq.com/s/yBpO9uH3sRJmv5zRw05Srg  
8. Claude Code 第四篇：SKILL的创建，安装，查看 — https://mp.weixin.qq.com/s/GTHz88RPJXQE2Q6DjYDQZA  

### OpenSkills / 安装器

9. 7.2k Stars！…OpenSkills开源 — https://mp.weixin.qq.com/s/FcZmn-U9qif-hF3qpDK9vQ  
10. 手把手教你安装Skill（OpenSkills） — https://mp.weixin.qq.com/s/_1TeNQBOiPoRShNki7Aw-g  

### Superpowers

11. 34k Star！…superpowers开源 — https://mp.weixin.qq.com/s/KHndlg9L16qwJkrp0VejGA  
12. Superpowers，一款强大的软件开发工作流skills — https://mp.weixin.qq.com/s/guxGDr9j8B6Ud20inOthWg  
13. Superpowers 实践指南 — https://mp.weixin.qq.com/s/7e8t_xk6NjPu_s6wIylIIQ  
14. Claude Code 30k+ star官方插件… — https://mp.weixin.qq.com/s/hnCSP9vUkKuJAJsfjvKk5g  

### OpenClaw / ClawHub

15. OpenClaw Skill 四条命令 — https://mp.weixin.qq.com/s/wCoo-h4dEkxLjZo-lOsVmQ  
16. OpenClaw技能包大全 — https://mp.weixin.qq.com/s/AtSRr7ycuyF9CODb34PS8A  
17. OpenClaw 常用 Skill 安装指南… — https://mp.weixin.qq.com/s/3SI1Lq9Cd28XN4JIg9wIJw  
18. OpenClaw进阶指南！云部署vs本地 — https://mp.weixin.qq.com/s/6F8YIRJ26rWr4Rke59ElPQ  
19. OpenClaw 安装与运行教程 — https://mp.weixin.qq.com/s/aU6Fs3Gxtqw73ZIPV_Pcmg  
20. OpenClaw 定时任务没通知 — https://mp.weixin.qq.com/s/fysk15hWcaau9umYCHBI0w  
21. 聊聊OpenClaw的AgentLoop — https://mp.weixin.qq.com/s/R6JogezyL0KoeFs1TqGvRA  
22. 一张图拆解OpenClaw — https://mp.weixin.qq.com/s/61dUipTGlqWdzdpQlhA-Ow  

### MCP

23. Claude Code 必备！MCP 工具合集 — https://mp.weixin.qq.com/s/HIr2RLtb0iMho194_O8ZsQ  
24. 在Claude Code中配置MCP工具 — https://mp.weixin.qq.com/s/B5MnRFTgGPxLA5ypyoeUtg  
25. 使用Go开发MCP Server — https://mp.weixin.qq.com/s/m4dpXYoCedVVqaJJmvToVQ  
26. github开源项目-MCP（开发流程） — https://mp.weixin.qq.com/s/9J3pvrmC9r47QahjI7U1lA  
27. SOAR MCP Server — https://mp.weixin.qq.com/s/jJDaeCGRwqCn8YRm-drpQg  
28. KWDB MCP Server — https://mp.weixin.qq.com/s/JnheyoNIRkKTh4a607UWEQ  
29. 全面解读MCP TypeScript SDK — https://mp.weixin.qq.com/s/VAWGoBeTnMQKxdMsI-iJgg  

### Codex / AGENTS.md / 跨工具

30. Claude Code 与 Codex 协作 3.0：从 MCP 到 Skills — https://mp.weixin.qq.com/s/w-jv5FVU3puOqGRpJXvE5w  
31. 告别Claude代码降智！Codex…AGENTS.md — https://mp.weixin.qq.com/s/Wd_HEmmu-XDJSnOTUg5niw  
32. 从 Cursor 到 Claude Code…ASL / AGENTS.md — https://mp.weixin.qq.com/s/XoOs6psDlbLsZiL3zOEIgQ  
33. Codex CLI 配置 Azure… — https://mp.weixin.qq.com/s/Wda9rERpVxMosxfHV9oMKQ  
34. Codex AGENTS.md 指南 — https://mp.weixin.qq.com/s/1zTOM5lo0jZZ0JBAAg868g  
35. Codex 斜杠命令 /init — https://mp.weixin.qq.com/s/cC4ceBEaMRy0G_OYqKFz8g  
36. Skills+MCP 结合、拷到 .codex — https://mp.weixin.qq.com/s/E2sSJNw5WZoY6vbdAzNs0Q  

### Harness / 源码

37. 敬畏与驾驭…Harness Engineering — https://mp.weixin.qq.com/s/NhOiHVx8NrvNjhPHvd2Mag  
38. 来自字节跳动TRAE的Harness指南 — https://mp.weixin.qq.com/s/xBNtHjseMomMA-aOQyOrJg  
39. Harness 实践 AI 代码率… — https://mp.weixin.qq.com/s/rlIyIIZOXFObNIXbPI7gDg  
40. 扒完Claude Code源码…Harness尽头 — https://mp.weixin.qq.com/s/O1bJL9obrNjMwzbx-Wm3aQ  
41. Claude Code大泄露…最顶Harness — https://mp.weixin.qq.com/s/dkENShji0_SjufXwOnQIZw  
42. 源码揭秘：while 循环 — https://mp.weixin.qq.com/s/3NwfxS0RJ_9etlep8zcQtw  
43. 泄露源码分析 Agent 循环 — https://mp.weixin.qq.com/s/ldp-p2-dMJifjsd_dmmqQg  
44. 拆解 Claude Code：8 个机制 — https://mp.weixin.qq.com/s/yoS4xNGYcdgiR62awfkgQQ  
45. Claude Code 深度拆解核心架构 — https://mp.weixin.qq.com/s/uq70MDj2BWJGtEGcrkuj-w  

### Cursor / Rules

46. Cursor分享 Agent Coding 最佳实践 — https://mp.weixin.qq.com/s/a1ceXnsadLcF0qP_ejfeWw  
47. Cursor Rules 全面总结 — https://mp.weixin.qq.com/s/l8r2lJlEv5fKWJRSsSd1kQ  
48. Cursor Rules、NotePads、Project Rules — https://mp.weixin.qq.com/s/TEx1dE8Qza82XRlLf9P0GA  
49. Cursor Rules 四大原则 — https://mp.weixin.qq.com/s/vEUAZIJrb5kEjWptIhsC-g  
50. 如何高效使用Cursor — https://mp.weixin.qq.com/s/GLTo73-0LFBxqsqsaFy8BA  

### CLAUDE.md / 工作流

51. .claude文件夹使用指南 — https://mp.weixin.qq.com/s/r9s63xZgXhwGCx0g_O0zmA  
52. 终极指南：Claude Code 最佳实践 — https://mp.weixin.qq.com/s/42rhjkCU5Pyh_DVfpcKj8g  
53. 使用 Claude Code 做个 CMS（五）CLAUDE.md — https://mp.weixin.qq.com/s/I2YwHbs6GvEFNL6mvPIUoA  
54. Anthropic 最佳实践摘录 — https://mp.weixin.qq.com/s/8fEPWeSF2d7Xhbo8OBzSeg  

### 踩坑 / 体感

55. Claude越更越废？AMD…23万次调用 — https://mp.weixin.qq.com/s/fOYNImOXWh_P64lzTRd_MQ  
56. 推理深度下降讨论 — https://mp.weixin.qq.com/s/v8RYHYzDS_UlZGL6JnQIBQ  
57. /insights 复盘 300 小时 — https://mp.weixin.qq.com/s/iKytiNFw8c7SOTxRcM24kg  
58. 陪女朋友逛街…Remote Control 实测 — https://mp.weixin.qq.com/s/ZtcKNRhJaejx65lMoiQc-w  

### 其它高相关（会话内继续累加至 110+）

补充样本库（子代理落盘，去重后合计约 130+ 条）：

- [微信样本批1](drafts/wechat-samples-agent-writing.md) — 48 条  
- [微信样本批2](drafts/wechat-samples-batch2.md) — 54 条  
- [微信样本批3](drafts/wechat-batch3-samples.md) — 36 条 + 标题/开头句式归纳

---

## 附录 B. GitHub 深读清单 + 竞品空白

详见子代理 [GitHub同类项目调研](15963e28-ea37-4dca-96b0-f7b89c625347)（18+ 仓）。

**主流三类叙事**：skills 包管理器/市场 · 一份源配置 symlink 到多家 · 桌面「装一次同步到处」。

**与 pack-agent 最该对标细读**：
- agents-anywhere / agsync / skills-sync / agentsync（持续同步，不是可分享封包）
- openskills / vercel-labs/skills（装 SKILL.md）
- yzhao062/anywhere-agents（one config + packs，易被拿来比）

**相对空白（公众号可独占）**：
1. 角色级 `.pack.json` 快照（可甩给别人 / agent 自装），不是本机 symlink 源目录  
2. skills + rules + MCP 同包  
3. Harness / Prefunction / Agent 三层术语  
4. 「甩文件说装一下」零认知安装

---

## 附录 C. 一页纸：改稿检查表

- [ ] 开头 3 句内出现「角色 / .pack.json / 多家 harness」之一  
- [ ] 前 15 行有 GitHub 或 npx  
- [ ] 至少一张对比表（拷目录 / OpenSkills / 本项目）  
- [ ] 至少 3 个可复制命令块  
- [ ] 有 gotcha（Hermes 或全局配置）  
- [ ] 有「不做什么」  
- [ ] 无赋能闭环；无虚标 star  
- [ ] MC 隐喻 ≤ 1 小节，不当全文唯一钩子  
