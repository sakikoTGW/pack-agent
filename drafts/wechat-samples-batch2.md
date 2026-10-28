# 微信公众号样本采集 · 第二批

主题：开源发布 / 踩坑亲测 / Skill·MCP·工作流 / OpenSkills·商店·配置同步 / 独立开发者工具 / 程序员干货长文  
范围：`site:mp.weixin.qq.com` 变体检索；剔除明显软广（云厂商导购、纯项目安利无干货）。  
说明：部分链接受微信反爬影响，开头摘录优先用可抓正文，否则用检索摘要拼接（约 200–400 字）。

文章类型标注：
- **作者发布自家工具**：作者本人开源/发布自研产品或 Skill
- **科普实操**：教程、踩坑、原理、生态介绍（非自家产品首发）
- **官方/生态转述**：翻译官方文档、转述大厂产品发布（非软广推销）

---

| 序号 | 标题 | 链接 | 开头原文摘录 | 文章类型 | 可学写法(一句话) |
|:---:|:---|:---|:---|:---|:---|
| 1 | 【万字长文】Claude Skills完全指南：从概念到实战 | https://mp.weixin.qq.com/s/x9UpqjuYzLb7I2ZZ932bNg | 1万字，国内最完整的Skills指南。想了解Skills是什么、怎么用、怎么建，看这一篇就够了。说起来，Skills这个功能我关注挺久了。去年10月Anthropic发布Skills的时候，我的判断是：这东西会火，但还早。三个月过去，情况完全不一样了……我自己也凑了个热闹，把自动化写作工作流作为技能发布上去了。 | 科普实操（夹带自家Skill上架） | 先抛「判断→证伪→亲自下场」时间线，再立万字指南承诺 |
| 2 | [小山译] Claude Skill 编写与实战指南 | https://mp.weixin.qq.com/s/yBpO9uH3sRJmv5zRw05Srg | Hi，我是小山，一个学习 AI 的大叔。最近在公司里和上海的朋友聊天，发现 Skill 相关的问题挺多……Anthropic 出了一篇官方指南，我边学边翻译分享给大家。Skill 是以文件夹的形式打包的一组指令，用来教会 Claude 怎么处理特定任务或工作流。 | 官方/生态转述 | 「译者人设开场 + 官方原题」降低信任成本 |
| 3 | Claude Skills使用教程：先从学习官方文档开始，一起读 skill-creator 文档 | https://mp.weixin.qq.com/s/J_pi2C2uqnI9akNISKAinw | 我对 Claude Skills 有着浓厚的兴趣，因为它是个目前相对低门槛、未来却可能有很大应用场景、值得投入时间学习的方向。官方文档永远是第一手的知识来源。前置准备：安装 Claude Code……购买 Kimi 或智谱包月、用 CC Switch 配国产模型。 | 科普实操 | 先给「穷人路径」安装清单，再进入精读官方文档 |
| 4 | 最适合新手的 Claude Code、MCP、Skills 全套教程 | https://mp.weixin.qq.com/s/8iM16IfgKWvESx8ZaOg5Uw | 元旦之前，Cursor 一直是我的主战场。过去一年，我在 Cursor 上花了五位数的算力费用……但随着任务越来越复杂，我意识到：Cursor 开始影响我赚钱的速度了。于是我开始认真换方案——Claude Code。这篇文章记录了我真实的使用经历，包含新手怎么上手、哪些地方最容易踩坑。 | 科普实操 | 用「真金白银沉没成本」叙事推动工具切换 |
| 5 | 如何在Cursor、TRAE中，用Skills 3分钟制作精美PPT | https://mp.weixin.qq.com/s/_1TeNQBOiPoRShNki7Aw-g | Skill（技能），是Anthropic推出的一套，和MCP类似，都是可以拓展AI工具本身能力的一套机制。相比MCP，Skill最大的优势是速度快……今天骁哥就手把手教大家如何在Cursor、TRAE中使用Skill。步骤一：安装 OpenSkills……`openskills sync` 会多出一个 AGENTS.md。 | 科普实操 | 三分钟承诺 + OpenSkills install/sync 可复制路径 |
| 6 | 7.2k Stars！Claude Skills功能安装神器OpenSkills开源 | https://mp.weixin.qq.com/s/FcZmn-U9qif-hF3qpDK9vQ | 为此，今天给大家介绍一款能让所有编程智能体都拥有 Claude Skills 能力的开源神器——OpenSkills。OpenSkills 将 Anthropic 这套 Skills 系统赋能给所有 AI 编程智能体，以及所有可读取 AGENTS.md 文件的工具，堪称 SKILL.md 的通用安装器。 | 科普实操 | Star 数作钩子，一句话定义「通用安装器」定位 |
| 7 | Agent Skills实战：27个脚本不进上下文，一句话完成RAG入库前文档扫描 | https://mp.weixin.qq.com/s/yowLv5bSNhtRzURlmp2EEA | Anthropic在25年12月18日宣布把Agent Skills发布为开放标准……刚发布那会儿我在忙着录制视频课程，一月初才来得及具体测试。市面上介绍大多停留在个人效率，我更感兴趣 Skills 在 2B 企业大模型应用里能怎么用——能不能把已有业务脚本封装成 Skill。 | 作者发布自家工具（业务脚本 Skill 化） | 「开放标准日期 + 企业场景问题」双钩，再亮实战成果 |
| 8 | 太秀了，我把自己蒸馏成了 Skill！已开源 | https://mp.weixin.qq.com/s/Cz5krfeNGmJI_Nb02wPPIw | 经过一通操作，我的「鱼皮.Skill」开源了：https://github.com/liyupi/yupi-skill。Skills 简单来说就是一种 AI 的「技能包」……目前 Claude Code、Cursor、Codex 等主流 AI 编程工具都支持。建议用能力最强的大模型蒸馏；先装 skill-creator，再生成 SKILL.md。 | 作者发布自家工具 | 标题即结果「蒸馏成 Skill」+ 立即给 GitHub |
| 9 | 只用 Claude Skills，打造专属 AI 伴侣｜附完整教程 | https://mp.weixin.qq.com/s/74XfeY-QuPLJ582izR2JKw | 直到 Claude Skills 范式出现 + Kimi K2 Thinking 模型的发布，我才能把 AI Partner 打包成一个人人可用的 Agent 技能包。不需要写代码，只要把日记、文档扔进去……开箱即用，下载→拖进 Claude Code。仓库：https://github.com/eze-is/ai-partner-chat | 作者发布自家工具 | 「等技术成熟才开源」的时机叙事 + 三步上手 |
| 10 | Claude Skills｜将 Agent 变为领域专家 | https://mp.weixin.qq.com/s/bwFGcomH6BfkBzhFMiiH1g | Claude Skills 是一种基于文件系统的、可复用的知识包……用于向 Agent 注入流程化、确定性的内部知识（SOP）的标准化方案。MCP 是开放 AI 工具标准；Skills 和 MCP 可协同——Skills 提供领域知识，MCP 提供外部工具。实现可参考 openskills。 | 科普实操 | 一句话定义 + Skills/MCP 对照表建立心智模型 |
| 11 | Agent Skills 完全指南：从原理到实战彻底搞懂！ | https://mp.weixin.qq.com/s/VQSRPTf5bOyA1bjS2JH5Kw | 成长路线和 MCP 也非常像……后来社区开始涌现大量开源 Skills 以及 Skills 开放市场。不同客户端目录大同小异，基本上都是 .agentName/skills……用 OpenCode 演示：把 Skill 放到目录后，AI 会自动判断调用；再用 skill-creator 生成带脚本的 Skill。 | 科普实操 | 「像 MCP 一样走生态曲线」类比 + 现场演示创建 |
| 12 | Claude Skills 最新指南！里藏着的 10 个关键细节 | https://mp.weixin.qq.com/s/LP9ou1hdBQB2-C-fPp6nJQ | 细节1：SKILL.md 的命名一个字母都不能错；细节2：description 决定 90% 触发准确率；细节3：渐进式披露三层结构不是摆设；细节4：文件夹里不要放 README.md……用厨房比喻讲清 Skills 和 MCP 的关系。 | 科普实操 | 「N 个关键细节」清单体，每条可独立转发 |
| 13 | 423 个神级 Skills 一键下载：Agent 能力开始被「工程化」了！ | https://mp.weixin.qq.com/s/-dSJah4P-UCZ6PaRczpucg | Skills 是 Anthropic 推出的一套标准……简单说就是一个包含 SKILL.md 的文件夹。可移植：同一个 skill，Claude Code、Codex、Cursor、OpenCode 都能用。国内特赞团队推出的 skill0.atypica.ai 收录了 423 个 skills。 | 科普实操 | 数字钩子（423）+ 「工程化」概念升维 |
| 14 | Skills成为开放标准后，也可以在Antigravity和Gemini CLI中使用了 | https://mp.weixin.qq.com/s/L3IAwVxchvFU3YKceoyblg | 2025年12月18日，Anthropic正式将Skills发布为开放标准……短短一个月内，Google 的 Antigravity 和 Gemini CLI 先后宣布集成。Gemini CLI 的 skills 格式与 Claude Code 完全兼容，可直接复制到 ~/.gemini/skills/。 | 科普实操 | 「标准日→跨厂商落地」证明可移植价值 |
| 15 | Agent Skills：把你的 Claude Code升级为定制的工作平台 | https://mp.weixin.qq.com/s/oX3ADDWuKVct0-kAPUnXmw | 首先了解 skill 的结构。只有 SKILL.md 是必要的，其他都是可选的按需加载资源……可存放在个人 ~/.claude/skills、项目 .claude/skills，或作为 plugin。元数据 name/description 启动时加载，需要时才读正文。 | 科普实操 | 结构图先行，再讲个人/项目/插件三级存放 |
| 16 | Claude Code 第四篇：SKILL的创建，安装，查看 | https://mp.weixin.qq.com/s/GTHz88RPJXQE2Q6DjYDQZA | 目前 Claude Code 已经支持了 SKILL……请帮我打开 agentskills.io，总结技能相关知识，创建一个「创建技能的技能」、安装技能的技能、查看技能的技能……兼容 Claude/Codex/AgentsSkill 多标准路径。 | 科普实操 | 系列文序号 + 让 AI 自举造「造 Skill 的 Skill」 |
| 17 | 34k Star！Claude skills项目superpowers开源 | https://mp.weixin.qq.com/s/KHndlg9L16qwJkrp0VejGA | 今天要给大家介绍一个 Agent Skills 开源项目 superpowers。它重新定义了编程智能体参与软件开发的核心逻辑，将 AI 从「代码打字员」转变为严谨规范的工程伙伴。该项目在 github 上已收获 34k Star。包含头脑风暴、写计划、子智能体驱动开发、TDD 等 skills。 | 科普实操 | Star 暴击 + 「打字员→工程伙伴」人设翻转 |
| 18 | Superpowers，一款强大的软件开发工作流skills | https://mp.weixin.qq.com/s/guxGDr9j8B6Ud20inOthWg | 最近各种 skills 满天飞……我找到一款宝藏级 skill，叫 Superpowers。不是简单提示词增强，而是一套强制性软件开发工作流：先讨论需求，再写计划，然后才动手。核心三命令：brainstorm / write-plan / execute-plan。 | 科普实操 | 「满天飞里挑宝藏」对比开场，三命令速记表 |
| 19 | Claude Code 30k+ star官方插件，小白也能写专业级代码 | https://mp.weixin.qq.com/s/hnCSP9vUkKuJAJsfjvKk5g | 1月18日，Jesse（obra）更新了 Superpowers 插件，star 数刚突破3万。老金我先把话说清楚：Superpowers 不是独立工具，是 Claude Code 的插件系统。装上之后多出 brainstorming、TDD、systematic-debugging 等技能树。 | 科普实操 | 澄清「不是独立工具」防误解，再给心智模型 |
| 20 | Claude Code Skills 完全指南：从零打造生产级别 AI 编程助理工作流 | https://mp.weixin.qq.com/s/M-K8cDwLhpID1gkcZOdUkQ | 说白了：Skills 不是「自定义命令」，而是一套工作流编程语言。它能记住工作模式、自动感知上下文触发、控制 AI 调用权限、注入动态数据，甚至启动独立子 Agent。并澄清 obra/superpowers 与官方 Skills 的关系：前者是大型 Skills 框架。 | 科普实操 | 先纠错定义，再分层讲到生产级与生态关系 |
| 21 | AI 编程三剑客：Spec-Kit、OpenSpec、Superpowers 深度对比与实战指南 | https://mp.weixin.qq.com/s/NeBSi-Q8zUWlWb0mL5BPOA | 本文将系统介绍 GitHub 官方的 Spec-Kit、社区热门的 OpenSpec 以及跨平台方法论工具 Superpowers……从前言为什么需要这些工具，到安装配置与实战协同，完整覆盖 AI 编程的规范驱动开发新方式。 | 科普实操 | 「三剑客对比表」满足选型焦虑 |
| 22 | 什么时候Agent能自己写skill？｜Hao好聊趋势 | https://mp.weixin.qq.com/s/9V0xYqveQigc3yJq-2-XQA | 腾讯前哨科技关注交叉地带，寻找 AI 的确定性。问：龙虾为什么好用？答案可能是它的 skill 太丰富了。2025年12月18日 Anthropic 把 Agent Skills 发布为开放标准……Skill 是工作流和 Know-how 的结晶，一个好 skill 可以快速复制。 | 科普实操 | 趋势问答体：先抛现象问题，再落到标准与自进化研究 |
| 23 | 扣子上线 Coze Skills 和技能商店 | https://mp.weixin.qq.com/s/CmmXDIH9JntH8N78ouO_cw | 扣子今天发布大升级到 2.0，竟然有了 skills 功能，而且增加了各种职场专用 skill。打开扣子编程，选择「技能」后创建……还可以分享出去。为了让技能形成生态，扣子推出技能商店，专业人士可上架售卖。 | 官方/生态转述 | 「大厂跟进 Skills」事件文，手把手创建+商店闭环 |
| 24 | 扣子 2.0，让 Agent 更进一步 | https://mp.weixin.qq.com/s/Uck9Jzs_wY-68qPdfWbvyA | 扣子2.0今天正式上线。一个 Agent Skill，本质上是「场景最佳实践 + 所需工具」的封装……如何找到合适的 Agent Skills？扣子「技能商店」应运而生。同时推出 Agent Plan / Coding / Office。 | 官方/生态转述 | 产品发布体：概念一句定义 + 能力矩阵 |
| 25 | 99%的人都不知道：MCP 必须在 Claude Code 外面装 | https://mp.weixin.qq.com/s/PWQBAkshOGF-x4P6myzmgw | 解决方案其实只需要一句话：MCP 必须在 Claude Code 外面装。记住这个黄金法则：任何 MCP 操作都要先退出 Claude Code（/exit），再在系统终端执行 claude mcp add……用 /mcp 验证。 | 科普实操 | 「99%不知道」+ 单句黄金法则，极利于传播 |
| 26 | 告别Claude代码降智！OpenAI Codex深度配置与避坑指南（附完整AGENTS.md） | https://mp.weixin.qq.com/s/Wd_HEmmu-XDJSnOTUg5niw | 最近Claude code降智严重，Codex强势崛起，群里一些人还不太会用。本文将带你从零掌握 OpenAI Codex……Windows 按官方配 MCP 通常会失败：需加 SYSTEMROOT，并把 command 指到 npx.cmd。 | 科普实操 | 「降智」情绪钩 + Windows 特供踩坑补丁 |
| 27 | Claude Code 调用 Codex：分工协作开发 | https://mp.weixin.qq.com/s/TmPPvWkw7LwjNMC-1HJ6aA | 配置 Claude Code 与 Codex 分工：CC 负责 plan/search/协调，代码生成委托 Codex……踩坑记录：坑1 Claude 还是自己写代码；坑2 Codex MCP not available；坑3 conversationId 丢失——在 CLAUDE.md 加 Session Management。 | 科普实操 | 「协作矩阵 + 编号踩坑」双结构，方便收藏 |
| 28 | Claude Code 与 Codex 协作开发 3.0：从 MCP 到 Skills 的技术演进 | https://mp.weixin.qq.com/s/w-jv5FVU3puOqGRpJXvE5w | 继前两篇协作方案后迎来 3.0：从 MCP Server 切换到 Skills 架构。MCP 方案上下文消耗呈指数增长；实测多文件修改 MCP ~45K tokens，Skills ~22K（省51%）。新路径：Claude Code → Bash → codex.py → Codex CLI。 | 科普实操 | 系列升级叙事 + 省 token 数字对比说服迁移 |
| 29 | Codex 0.46.0 高级配置指南（含 MCP / Streamable HTTP） | https://mp.weixin.qq.com/s/1zTOM5lo0jZZ0JBAAg868g | Codex 0.46.0 高级配置教程。MCP 是杀手级特性……Streamable HTTP 新特性可连远程 MCP；支持 OAuth login。附热门 MCP 表：Context7、Figma、Playwright、GitHub、Sentry。 | 科普实操 | 版本号标题 + 命令速查 + 推荐表，手册感强 |
| 30 | 烧掉十亿 Token 后，我总结的 Coding Agent 高阶玩法 | https://mp.weixin.qq.com/s/B4ROBwqvPG0ZOwoFqjQMCA | 今天来继续聊聊如何用好 Claude Code，Codex，Cursor CLI……技术栈要选 boring 的；Context Engineering 要炉火纯青：该填满就填满，该 /clear 就 clear；重复 context 放 CLAUDE.md 还是注释要想清楚。 | 科普实操 | 「烧掉十亿 Token」权威感 + 高密度经验条 |
| 31 | 扒完Claude Code源码，我看到了Agentic Harness设计的尽头 | https://mp.weixin.qq.com/s/O1bJL9obrNjMwzbx-Wm3aQ | 昨天 Claude Code 源码被动开源……从 cli.js.map 还原出 4756 个源文件后，我连续几周泡在这堆代码里。很多人复刻只拿走 system prompt + 文件编辑 + Bash；真正拉开差距的是整套 Agentic Harness：权限、Hook、Skill、子 Agent、验证 Agent。 | 科普实操 | 「源码考古」人设 + 拆穿浅层复刻 |
| 32 | Claude Code 真正的玩法：.claude文件夹使用指南 | https://mp.weixin.qq.com/s/r9s63xZgXhwGCx0g_O0zmA | 项目根目录的 CLAUDE.md 是最常见用法。但你也可以在 ~/.claude/CLAUDE.md 放全局偏好…….claude/rules/ 可拆指令；路径限定规则用 YAML frontmatter；agents/ 定义子代理；settings.json 管权限与 hooks。 | 科普实操 | 目录导览体：把隐藏能力一次性摊开 |
| 33 | Claude Code 核心：深度介绍 MCP+Agent+斜杆命令+Hook 一文通！ | https://mp.weixin.qq.com/s/7g5DugzATAIX1by4yAYtTg | Claude Code 是 Anthropic 推出的 AI 编程工具……对它来说比较重要的是：Slash Command + SubAgent + MCP + Hooks。并给出项目/.claude 与用户 ~/.claude 的完整目录树与命令表。 | 科普实操 | 「一文通」四件套总览 + 配置路径表 |
| 34 | 深入了解Claude Code CLI子代理Subagent | https://mp.weixin.qq.com/s/E6yk06Sz_-XeO_vbpT-B8g | Subagent 分 Project 与 User 两级……每个子代理是带 frontmatter 的 Markdown。可在自定义命令里并行调用 code-reviewer / security-auditor / performance-engineer / test-automator，合并成统一审查报告。 | 科普实操 | 配置模板 + 「多专家并行审查」可复制工作流 |
| 35 | Claude Code超级进化！subagents 功能让你从牛马秒变老板 | https://mp.weixin.qq.com/s/ad0md8Fcy9JFZq46-13ucA | 当 Claude Code 遇到适合某个子代理处理的任务时，会智能委派……四大核心优势：上下文保护、专业化能力、可重用性、灵活权限。创建只需三步；~/.claude/agents 下看得到架构师、CR 等文件。 | 科普实操 | 「牛马→老板」情绪标题 + 四优势结构化卖点 |
| 36 | Claude Code CLI初体验 | https://mp.weixin.qq.com/s/8XU1PyYO3eniXWBsYBxaVA | 距离 Claude Code CLI 发布有一段时间了……终端更喜欢 AI 终端伴侣如 Aider、Warp、Gemini CLI。本文覆盖智谱接入、自动授权环境变量、settings.json、子代理、/ide、记忆文件类比 Cursor Rules。 | 科普实操 | 初体验体：对照已知工具降低陌生感 |
| 37 | Anthropic 社区负责人连更31条Claude Code技巧 | https://mp.weixin.qq.com/s/OO5nhjX-1zTSXBzQqH5IQA | Claude Code 会扫描代码库自动生成 CLAUDE.md……较大项目可建 .claude/rules/，用 YAML 前缀指定路径条件。也可以直接告诉 Claude「更新 claude.md，记住这个项目用 bun」。Plugins 可一键安装工作流。 | 科普实操 | 「31条」清单聚合官方/社区硬核技巧 |
| 38 | 如果Claude Code只能装一个MCP，我只装Serena | https://mp.weixin.qq.com/s/pGE1g5o24-kMvd-vVjT6UQ | 如果 Claude Code 只能装一个 MCP，我只装 Serena。Serena 提供符号级代码理解与编辑，相当于接上 IDE 级词法分析……一句 `claude mcp add serena -- uvx --from git+https://github.com/oraios/serena ...` 即可。 | 科普实操 | 极端取舍句作标题，立刻建立权威推荐 |
| 39 | Claude Code 终于有好用的 UI 了，这个开源项目牛啊！！ | https://mp.weixin.qq.com/s/GYaq_HNo5mKh2WjWtuEMyg | Claude Code 命令行很强，但纯终端对很多人反直觉……1Code 是给 Claude Code 套上 Cursor 风格 UI 的开源桌面应用：Git Worktree 隔离、实时 Diff、后台并行 Agent。GitHub：https://github.com/21st-dev/1code | 科普实操 | 痛点共鸣（盲写）→ 开源壳方案 + 仓库 |
| 40 | 如虎添翼，在 Claude Code 中设置 MCP | https://mp.weixin.qq.com/s/wNXjXNTegIAQkDXbKu-OvA | MCP 使 LLM 能访问外部工具和数据源。Claude Code 全面支持……覆盖项目 .mcp.json、OAuth、从 JSON/Desktop 导入，甚至 `claude mcp serve` 把 Claude Code 自身暴露为 MCP 服务器。 | 科普实操 | 官方能力清单式覆盖，适合当配置手册 |
| 41 | Claude Code 远程 MCP 现已支持（使用方法详解） | https://mp.weixin.qq.com/s/QLBXzbXpWV7ENV5je-jgGw | Claude Code 现在支持远程 MCP。你不再需要本地跑服务器，直接连供应商托管端点：Linear、Sentry 等 SSE URL，OAuth 一次认证即可。关键是用 `--scope project` 把配置提交进仓库。 | 科普实操 | 「远程=免本地运维」价值句 + 团队共享 scope |
| 42 | 刚刚，Cursor分享了他们使用Agent Coding 的最佳实践 | https://mp.weixin.qq.com/s/a1ceXnsadLcF0qP_ejfeWw | Cursor 通过 Rules（静态）与 Skills（动态）提供两种扩展方式。Skills 封装领域知识、工作流和脚本，仅在相关时加载以节省上下文。示例：hooks 驱动「测不过就继续」的长时间循环；以及 /pr、TDD 等常见工作流。 | 官方/生态转述 | 官方实践转述 + Rules/Skills 二元对立好记 |
| 43 | Cursor 0.49.x 自动化生成 Project Rules 实用指南 | https://mp.weixin.qq.com/s/1yTkzYzOFjty1D0gtYHuHA | Cursor 0.49 的自动生成 Project Rules 极大简化创建规则流程……输入 `/Generate Cursor Rules`，分析对话提取上下文，保存到 `.cursor/rules/` 的 .mdc。建议手动三层规则打底 + 自动生成补充。 | 科普实操 | 版本功能点题 + 「自动+手动」组合工作流 |
| 44 | 为什么你的Cursor总装不上MCP？今天分享4个终极大招 | https://mp.weixin.qq.com/s/et3Y78XrdlPq9ertTj70WQ | 升级到 Cursor 0.46 都想体验 MCP，但 npx 安装报错很常见。方法一找全局路径；方法二用 node+绝对路径；方法三 clone 源码本地跑；方法四 Windows 加 `cmd /c`（黑窗别关）。必须开 Agent 模式。 | 科普实操 | 痛点标题 + 编号终极大招，收藏率高 |
| 45 | Cursor 配置 MCP Server 指南 | https://mp.weixin.qq.com/s/b_k-8nIGqyBqfYWRVwJ6yA | 添加 MCP 有全局与项目级两种；推荐项目级 `.cursor/mcp.json`，避免污染所有项目。给出 Brave Search / GitHub 的 JSON 示例，并强调 Settings→MCP 绿点验证。 | 科普实操 | 明确「推荐项目级」决策，减少新手踩坑 |
| 46 | 【Cursor】Cursor中MCP的接入和使用 | https://mp.weixin.qq.com/s/iokC2fEB5X6Jnmga2-8JBA | Cursor 0.45.6 提供 MCP，作为客户端接入服务……限制：必须在 Composer 的 agent 模式；Chat 模式无法使用。附 filesystem / sqlite 配置与「终端先跑通再刷新」调试法。 | 科普实操 | 版本+模式限制写死，避免「配了却调不了」 |
| 47 | 从 Cursor 到 Claude Code，告别 Agent配置烦恼，ASL详解 | https://mp.weixin.qq.com/s/XoOs6psDlbLsZiL3zOEIgQ | 无论 Cursor、Claude Code、Aider、RooCode，各有 `.cursor/rules/`、`claude.md` 等指令格式，导致生态碎片化。AGENTS.md 由 OpenAI 发起追求简洁可预测；作者进一步提出通用 Agent Specification Language（ASL）。 | 科普实操 | 碎片化痛点→标准提案，适合「配置同步」选题对标 |
| 48 | MCP开发初体验：我的踩坑实录~ | https://mp.weixin.qq.com/s/Mxjtyp8GsaO_Ca1aOHHQMw | 以高德 MCP server 为例……用 system prompt 迭代多次才走对 maps_text_search→maps_geo→周边搜索。即便结构化地址，坐标仍偏 4km，半径 500m 导致结果离谱。结论：MCP 离「即插即用」还有不小差距。 | 科普实操 | 「实录」诚实失败叙事，比成功教程更可信 |
| 49 | 动手开发一个 MCP Server | https://mp.weixin.qq.com/s/EedUriN_nMWIoZgJAZP6dQ | 一般来说 AI App 已集成 MCP Client，我们只需定制 Server。以 Pixabay 图片 MCP 为例，用 `@modelcontextprotocol/sdk` + Stdio，Inspector 调试后发 npm，再在 Cursor/Cline 配 mcpServers。 | 科普实操 | 最小可运行案例：写→调→发→接全链路 |
| 50 | 【开源】我亲手开发的一个AI框架，谈下背后的思考 | https://mp.weixin.qq.com/s/d2lADFG5m8pZ31v8epUhHQ | 有不少读者问 AI Agent 框架怎么选型……市面方案要么臃肿要么黑盒。于是我们手搓了支持动态拓扑的框架 Bridgic，现已开源。本文不讲用法，而讲 worker/automa、DDG 动态拓扑等第一性思考。GitHub：bitsky-tech/bridgic | 作者发布自家工具 | 「不讲 API 讲第一性」差异化开源文 |
| 51 | 智能编程助手 Neovate Code 正式开源 | https://mp.weixin.qq.com/s/-aCu8sI12x8fz5QTIwf3zg | 蚂蚁集团支付宝体验技术部正式开源 Neovate Code……从 Claude Code 学到很多，但 Claude 未开源且门槛高；Neovate 支持主流模型与多家提供商，CLI 形态，架构可扩展到 IDE/Web/Remote Agent。 | 作者发布自家工具（团队开源） | 「对标 Claude Code 的开放替代」定位清晰 |
| 52 | 玩转OpenClaw：核心架构、运作原理、Agent部署步骤 | https://mp.weixin.qq.com/s/Q0CC0p5e-DQEYEk5ErQB1Q | 作者：独立观察。想深入了解 OpenClaw 的同学可先看源码和官方文档……Skills 按需加载，来源分 workspace / user / bundled 三级；Clawhub 是 Skills Market：search/install/list/update/sync。强调定期清理低质 skill 防膨胀。 | 科普实操 | 架构文 + 技能管控清单，贴近「配置同步」主题 |
| 53 | OpenClaw 常用 Skill 安装指南并打通本地与云端知识库 | https://mp.weixin.qq.com/s/3SI1Lq9Cd28XN4JIg9wIJw | 既然 OpenClaw 已部署、飞书机器人就绪，直接让 AI 安装 skill-vetter 与 agent-browser……到 ClawHub 找 Git 相关 Skills，用 `npx clawhub@latest install git-essentials`，再打通 Gitee + Obsidian 知识库同步。 | 科普实操 | 「装完不会用」续篇：安装→知识库闭环 |
| 54 | Claude Code 还要充会员？这个开源替代品真香！ | https://mp.weixin.qq.com/s/7OgvgLTdTxU7cIEGVm8dVw | OpenCode = 开源版 Claude Code + 多模型支持 + 终端极客风。100% 开源、不绑模型、可本地部署……附对比表帮你选型：隐私/预算选 OpenCode，要 Opus 顶配选 Claude Code。 | 科普实操 | 付费痛点钩 + 选型表，避免单边安利 |

---

## 本批统计

| 维度 | 数量 |
|:---|:---|
| 有效条目 | **54**（目标 40–50，略超） |
| 作者发布自家工具 | 约 6–8（鱼皮 Skill、AI伴侣、Bridgic、Neovate、业务 Skill 化等） |
| 科普实操 / 踩坑干货 | 约 40 |
| 官方/生态转述 | 约 5 |
| 已剔除 | 纯软广（如「效率神器+星标」安利体、云厂商导购价帖等） |

## 对「Agent Modpack」文案可直接借的写法

1. **黄金法则单句**（如「MCP 必须在外面装」）→ 可对应「一个 pack = 一个角色，裸 export 拒绝」。  
2. **Star / 版本号钩子** → 可用 npm/GitHub 数字，但别空喊。  
3. **对比表**（Rules vs Skills、MCP vs Skills、Harness vs Agent）→ 与草稿「三层」结构同构。  
4. **踩坑编号** → 装完卸不干净、目录猜错等，适合放进 eject/ledger 叙事。  
5. **开源发布体**：「我做了 X → 痛点一句话 → 最小用法 → GitHub」——与现有 `wechat-agent-modpack.md` 高度同构。
