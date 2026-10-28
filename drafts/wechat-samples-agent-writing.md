# 微信公众号样本采集（Agent / Skills / MCP / Harness）

> 用途：蒸馏写作方法。过滤纯营销水文。优先：工具发布、踩坑实录、架构拆解、工作流分享。  
> 采集时间：2026-07-18 · 条目数：48

| 序号 | 标题 | 链接 | 钩子原文(1-3句) | 结构骨架 | 质量 |
|---|---|---|---|---|---|
| 1 | 最适合新手的 Claude Code、MCP、Skills 全套教程 | https://mp.weixin.qq.com/s/8iM16IfgKWvESx8ZaOg5Uw | 元旦之前，Cursor 一直是我的主战场。过去一年，我在 Cursor 上花了五位数的算力费用。但随着任务越来越复杂，我意识到：Cursor 开始影响我赚钱的速度了——于是完整跑了一轮 Claude Code、MCP 和 Skills。 | 痛点→安装→MCP→Skills对照 | 高 |
| 2 | 【万字长文】Claude Skills完全指南：从概念到实战 | https://mp.weixin.qq.com/s/x9UpqjuYzLb7I2ZZ932bNg | 1万字，国内最完整的Skills指南。去年10月发布时我判断「会火但还早」；三个月后做成开放标准，Simon Willison 说 Skills 可能比 MCP 更重要。 | 判断→概念→对照MCP→实战 | 高 |
| 3 | 一文看懂 Claude Agent Skill：是否可替代 MCP？ | https://mp.weixin.qq.com/s/biVRcKM4XyW4S9JxxqQtmw | 这个让大厂纷纷跟进的 Agent Skill，到底解决了什么核心痛点？它和我们熟悉的 MCP 又有什么区别与联系？ | 痛点→概念→Skill vs MCP→选型 | 高 |
| 4 | Agent Skills 完全指南：从原理到实战彻底搞懂！ | https://mp.weixin.qq.com/s/VQSRPTf5bOyA1bjS2JH5Kw | Skills 成长路线和 MCP 很像：先自家支持，后 Cursor/Codex/OpenCode 跟进，再出现开放市场。 | 演进→结构→安装演示→creator | 高 |
| 5 | Agent Skills 终极指南：入门、精通、预测 | https://mp.weixin.qq.com/s/jUylk813LYbKw0sLiIttTQ | 应该是全网最好的 Skills 中文指南与教程，全文 1.2w 字。Claude Skills 的价值还是被大大低估了。 | 价值主张→安装→创建→预测 | 高 |
| 6 | Claude Skills｜将 Agent 变为领域专家 | https://mp.weixin.qq.com/s/bwFGcomH6BfkBzhFMiiH1g | 一句话：基于文件系统的可复用知识包，向 Agent 注入流程化、确定性的内部知识（SOP）。 | 定义→结构→与MCP关系→openskills | 高 |
| 7 | Claude Skills使用教程：一起读 skill-creator 文档 | https://mp.weixin.qq.com/s/J_pi2C2uqnI9akNISKAinw | skill-creator 是创建 skills 的 skills。学习方法很简单：精读原文文档。 | 读官方→概念→元数据质量→步骤 | 高 |
| 8 | Agent Skills：把你的 Claude Code升级为定制工作平台 | https://mp.weixin.qq.com/s/oX3ADDWuKVct0-kAPUnXmw | Claude 近期推出 Agent Skill，能让 AI 更好完成特定任务。Skill 是可重复使用的资源包。 | 结构→目录→创建→plugin安装 | 高 |
| 9 | [小山译] Claude Skill 编写与实战指南 | https://mp.weixin.qq.com/s/yBpO9uH3sRJmv5zRw05Srg | 对做 MCP 集成的开发者来说，Skill 是额外一层，把原始工具访问变成可靠、可复用的工作流。 | 受众分层→类别→creator→发布 | 高 |
| 10 | 7.2k Stars！OpenSkills开源，无缝接入所有编程智能体 | https://mp.weixin.qq.com/s/FcZmn-U9qif-hF3qpDK9vQ | OpenSkills 把 Anthropic Skills 赋能给所有可读 AGENTS.md 的智能体，堪称 SKILL.md 通用安装器。 | 痛点→机制→安装命令→通用目录 | 高 |
| 11 | 在Cursor/TRAE中用Skills做精美PPT（OpenSkills实操） | https://mp.weixin.qq.com/s/_1TeNQBOiPoRShNki7Aw-g | 过去 Skills 是 ClaudeCode 和 Codex 专属；OpenSkills 让任意编程工具都支持。手把手：install → sync → AGENTS.md。 | 安装三步→规则映射→实战PPT | 高 |
| 12 | Claude Code 真正的玩法：.claude文件夹使用指南 | https://mp.weixin.qq.com/s/r9s63xZgXhwGCx0g_O0zmA | Claude Code 每次启动第一个读取的就是 CLAUDE.md。它直接加载进系统提示，全程生效。 | CLAUDE.md→rules→hooks→skills→五步上手 | 高 |
| 13 | Claude Code 最佳实践（官方向） | https://mp.weixin.qq.com/s/_xqHmQsLSquA5qoXe8JQWw | CLAUDE.md 是特殊文件，开始对话时自动拉入上下文——记录常用命令、风格、测试、仓库规范。 | 写CLAUDE.md→多位置→调优→工作流 | 高 |
| 14 | Claude Code 实战指南：终端编程与 MCP | https://mp.weixin.qq.com/s/kBavEWqUQGqW2a1LcYSb8w | Claude Code 运行在终端，直接与开发环境集成。推荐 /init 生成 CLAUDE.md，并演示 context7/SSE MCP 安装。 | 前言→init→权限→MCP命令→工作流 | 中 |
| 15 | Claude 高级技巧：CLAUDE.md、MCP、命令、Skills & Hooks | https://mp.weixin.qq.com/s/8yq_Q8JbnDPRWsltVgc9lw | 按企业/全局/项目/本地四级加载 CLAUDE.md；Hooks 做确定性控制，Skills 做指导。 | 层级配置→安全门控→Hooks表→Skills | 高 |
| 16 | 在Claude Code中配置MCP工具 | https://mp.weixin.qq.com/s/B5MnRFTgGPxLA5ypyoeUtg | 两种方法：`claude mcp add` CLI，或直接改 `.mcp.json` / `~/.claude.json`。项目作用域更推荐，避免空耗上下文。 | CLI→配置文件→作用域→/mcp验证 | 高 |
| 17 | Claude Code 必备！最常用 MCP 工具合集（附安装命令） | https://mp.weixin.qq.com/s/HIr2RLtb0iMho194_O8ZsQ | MCP 像 USB-C：stdio / SSE / HTTP 三种连接。附 Playwright、GitHub、Sentry、Context7 等 `claude mcp add` 命令。 | 协议→管理命令→高频清单 | 高 |
| 18 | Gemini 3没等来，Claude Code插件系统悄悄上线 | https://mp.weixin.qq.com/s/PTd_GI76LBWRpBS1QsElFA | 看起来不起眼但很实用——插件系统。一行 `/plugin marketplace add` 把命令、subagent、MCP、Hooks 打包分享。 | 发布钩子→四件套→市场→用例 | 高 |
| 19 | Claude Code上线插件系统，AI编程模式再次升级 | https://mp.weixin.qq.com/s/9GrYLOnjQ5YmPZTAvQbEsA | 以前手动配置的斜杠命令、代理、MCP、Hooks 不再本地吃灰，而是可共享、工程化的工具包。 | 革命性→CLI→marketplace.json→社区 | 高 |
| 20 | 扒完Claude Code源码，我看到了Agentic Harness设计的尽头 | https://mp.weixin.qq.com/s/O1bJL9obrNjMwzbx-Wm3aQ | Claude Code 整套系统，就是套在 Claude 外面的那个 Harness。模型不是壁垒，Harness 才是。 | 总论→调度链→Skill/Plugin/Hook/MCP→结论 | 高 |
| 21 | Claude Code源码泄漏：记忆系统秘密（对比OpenClaw） | https://mp.weixin.qq.com/s/Tlld8VxsZbhiI1Jd1vppiA | 从 512000 行里扒出五层记忆：CLAUDE.md 族、Auto memory、Session memory、Agent memory、Team memory——和 OpenClaw 完全不同。 | 五层拆解→检索机制→产品哲学对比 | 高 |
| 22 | Claude Code大泄露：当今最顶Harness开源了 | https://mp.weixin.qq.com/s/dkENShji0_SjufXwOnQIZw | 别光 Clone。上下文硬上限、三级压缩、后台子agent记笔记、Hook 可改写入参——Harness 从固定产品变成可定制底座。 | 存储层→容器层→Hook→启示 | 高 |
| 23 | 让Claude自己分析源码：build-code-agent教程 | https://mp.weixin.qq.com/s/srPQIA4m7c0sqdXND6V2qA | 源码泄露后有人让 Claude 自己分析自己，写出 8 章 Code Agent 构建教程。核心：Loop + 工具 + Skills + 压缩 + 子代理 + 权限。 | 钩子→架构一句→章节地图→可迁移性 | 高 |
| 24 | Claude Code记忆系统源码拆解（三层） | https://mp.weixin.qq.com/s/ldp-p2-dMJifjsd_dmmqQg | 泄露含 1906 个 TS 文件、51.2 万行。记忆不是全塞进窗口：MEMORY.md 热数据 + 话题文件按需 + 历史 Grep。 | 泄露范围→三层记忆→召回提示词 | 高 |
| 25 | 从“结对编程”到“管理AI团队”：Async-Agent Coding | https://mp.weixin.qq.com/s/viSqcNDgWmkuqQumcmyY4g | Claude 有 CLAUDE.md / MCP / 指令；Codex 早期几乎只剩 AGENTS.md。把任务管理写进 AGENTS.md 实测「并没啥效果」——元编程对象变成了 agent 本身。 | 对比两工具→任务派发→瓶颈→元编程 | 高 |
| 26 | 告别Claude代码降智！Codex深度配置与避坑（附AGENTS.md） | https://mp.weixin.qq.com/s/Wd_HEmmu-XDJSnOTUg5niw | Windows 按官方配 mcp_servers 通常会失败：Program not found / timeout。正确做法：npx 完整路径 + SYSTEMROOT env。附完整工具优先级 AGENTS.md。 | 痛点→Windows MCP→AGENTS全文 | 高 |
| 27 | Codex 0.46.0 终极指南（MCP/AGENTS.md） | https://mp.weixin.qq.com/s/1zTOM5lo0jZZ0JBAAg868g | 三秒钟安装；MCP 支持 STDIO 与 Streamable HTTP；AGENTS.md 三级合并；调试看 `~/.codex/log/codex-tui.log`。 | 安装→MCP→AGENTS→斜杠命令→配置 | 高 |
| 28 | Codex CLI初体验（mcp add/list） | https://mp.weixin.qq.com/s/zXSO25j79cv3ng47d-axLA | `codex mcp add context7 npx -y @upstash/context7-mcp`；配置只支持用户全局；mcp 早期只支持 stdio。 | 安装→mcp命令→限制清单 | 中 |
| 29 | Codex CLI MCP 配置实战（config.toml） | https://mp.weixin.qq.com/s/cC4ceBEaMRy0G_OYqKFz8g | Codex 用 TOML 的 mcp_servers，不像 Claude/Cursor 的 JSON。启动时报错即失败，无专门验证命令。 | 配置示例→验证技巧→调用演示 | 中 |
| 30 | Day35：Codex＋Chrome-DevTools MCP 配置全流程 | https://mp.weixin.qq.com/s/U0xMBtibHhH7HQ8G5Tuj5A | 解雇每月 50 刀的 AI 员工改用 Codex；Chrome MCP 怎么都启不动——需独立调试浏览器 + Node 升到 22。 | 动机→报错→两步踩坑→搞定 | 高 |
| 31 | 刚刚，Cursor分享了他们使用Agent Coding的最佳实践 | https://mp.weixin.qq.com/s/a1ceXnsadLcF0qP_ejfeWw | Rules 静态、Skills 动态；示例用 hooks.json + grind.ts 做「测不过就继续」的长循环。 | Rules→Skills→Hook循环→工作流建议 | 高 |
| 32 | Cursor Agent Skills：静态目录动态读正文 | https://mp.weixin.qq.com/s/AkYXiS5O0I6tbWyAzVN17A | Skill 是可移植知识包；启动只暴露名称描述，正文按需 grep——渐进式披露，与 Claude Code / Manus 同构。 | 策略→表结构→洞见→批注 | 高 |
| 33 | Cursor 配置 MCP Server 指南 | https://mp.weixin.qq.com/s/b_k-8nIGqyBqfYWRVwJ6yA | 推荐项目级 `.cursor/mcp.json`，全局会拖垮所有项目质量。绿点 Enabled + 提示词点名工具。 | 全局vs项目→JSON样例→验证排障 | 高 |
| 34 | 【Cursor】Cursor中MCP的接入和使用 | https://mp.weixin.qq.com/s/iokC2fEB5X6Jnmga2-8JBA | MCP 必须在 Composer Agent 模式；Chat 模式不可用。超时就先在终端跑通再回 Cursor 刷新。 | 设置路径→filesystem/sqlite→限制 | 中 |
| 35 | 模型不是主角，Harness才是 | https://mp.weixin.qq.com/s/sVGeofV9uTgvhgR44q8pNA | 这层壳有正式名字：Harness。AGENTS.md 当目录别当百科；硬约束优于软提示；工具要从 15 砍到 2。 | 命名→演进→实践清单 | 高 |
| 36 | 来自字节跳动TRAE的Harness Engineering指南 | https://mp.weixin.qq.com/s/xBNtHjseMomMA-aOQyOrJg | Harness 不是玩具，是生产化框架：REPL 容器 + 边界控制 + 工具路由 + 确定性反馈。 | REPL四段→沙箱层级→预算熔断→审计 | 高 |
| 37 | Harness Engineering：优化模型运行的环境与机制 | https://mp.weixin.qq.com/s/MzB8-B00GVdJy22j42CRgg | Harness 不是更好的 Prompt，也不是更强模型，而是模型外一切基础设施。四维解构 + Token 转化流水线。 | 定义→四维度→安全门控 | 高 |
| 38 | 一文读懂Harness Engineering（14篇工程文综） | https://mp.weixin.qq.com/s/DE0hc3Mz6zwoSCgL0k4SgQ | 从上下文工程到六层记忆、Coordinator Mode、Hooks 插槽——寻找那个让 AI 不再离经叛道的壳。 | 综读→记忆→协调者→Hooks | 高 |
| 39 | 一文搞懂Hermes：Skills闭环自我进化 | https://mp.weixin.qq.com/s/yHva-zLaRTxe8b4HSUr86Q | 这不是科幻：经验提取→存储→检索→注入→验证→改进。源码级讲条件激活与渐进式披露。 | 闭环宣言→模块→frontmatter条件→披露 | 高 |
| 40 | Hermes vs OpenClaw：两条能力生长路径 | https://mp.weixin.qq.com/s/x3-BAaIpVTLgvNTv0yJvxw | 龙虾偏人类预设 Skill（确定性）；Hermes 偏经验涌现 Skill（进化式）。可 `hermes claw migrate` 迁移。 | 对比哲学→技能/记忆→组合用法 | 高 |
| 41 | 装好小龙虾之后：完整配置指南 | https://mp.weixin.qq.com/s/ubGKHibndjdp47g_G82rjQ | 给人设、装技能、连聊天工具、定时任务、教它记住你——`openclaw skills install` + `openclaw doctor`。 | 类比表→SOUL/USER→技能→检查清单 | 高 |
| 42 | 腾讯QQ机器人接入OpenClaw：官方养虾指南 | https://mp.weixin.qq.com/s/8i-JFwEyObGKy0JFSMjx_Q | clawhub search/install → openclaw skills list/check → 配 Key → gateway restart；自定义 skill 丢进 workspace/skills。 | 官方步骤→命令序列→自建skill | 高 |
| 43 | OpenClaw 实用技巧大全（多Agent/技能） | https://mp.weixin.qq.com/s/kbewg3kIsfcBN-jUIfzkpg | 1.2 万字：初始化到模型切换、斜杠命令、技能安装、多智能体、记忆、成本、安全。万能技巧：让 AI 帮你养 OpenClaw。 | 万能提示→技能安装→多Agent→避坑 | 高 |
| 44 | OpenClaw 常用 Skill 安装并打通知识库 | https://mp.weixin.qq.com/s/3SI1Lq9Cd28XN4JIg9wIJw | 安装前先 skill-vetter；再 agent-browser、self-improving-agent；用 Gitee + git skill 打通本地与云端沉淀。 | 安全审查→核心skill→Git沉淀 | 高 |
| 45 | Superpowers：软件开发工作流skills | https://mp.weixin.qq.com/s/guxGDr9j8B6Ud20inOthWg | 不是提示词增强，而是强制流程：brainstorm → write-plan → execute-plan。两行 plugin 命令安装。 | 定位→三命令→14skills→何时不用 | 高 |
| 46 | 告别Vibe Coding！Superpowers跨Claude/Codex/OpenCode | https://mp.weixin.qq.com/s/B8S-rJJTE2rR1IKl3e4ykw | 可组合 Skills 让代理像高级工程师。Claude 走 plugin；Codex/OpenCode 共享 skills-core，文内给完整安装脚本。 | 理念→技能表→三端安装脚本 | 高 |
| 47 | Superpowers 实践指南：按流程交付 | https://mp.weixin.qq.com/s/7e8t_xk6NjPu_s6wIylIIQ | 一句话：给 AI 加工程流程纪律。先澄清、先计划、先验证；与 openspec/speckit 是过程 vs 产物。 | 收益→核心技能→FAQ对照→落地 | 高 |
| 48 | Spec-Kit / OpenSpec / Superpowers 深度对比 | https://mp.weixin.qq.com/s/NeBSi-Q8zUWlWb0mL5BPOA | Spec-Kit 与 OpenSpec 竞争（二选一），Superpowers 互补。先定「做什么」再管「怎么做」。 | 三工具→竞争互补表→组合方案 | 高 |

## 额外高相关（未挤进主表，可作扩充）

| 标题 | 链接 | 备注 |
|---|---|---|
| 烧掉十亿 Token 后的 Coding Agent 高阶玩法 | https://mp.weixin.qq.com/s/B4ROBwqvPG0ZOwoFqjQMCA | CLI优先于MCP、工具选型、/clear 纪律 | 高 |
| 【万字】带你实现一个Agent：Tools→MCP→Skills | https://mp.weixin.qq.com/s/LnJPEwMS17xwM9-r2XKESQ | 自研注入 skill.md 的最小实现 | 高 |
| 动手开发一个 MCP Server（Pixabay） | https://mp.weixin.qq.com/s/EedUriN_nMWIoZgJAZP6dQ | 作者发布 npm + Cursor 配置 | 高 |
| 手搓 MCP-server 本地到云部署 | https://mp.weixin.qq.com/s/UmG8nuqzS7Lna7kTDemWNQ | stdio→SSE 云部署实战 | 高 |
| 从 ReAct 到 Ralph Loop | https://mp.weixin.qq.com/s/zMnIrokMvdNI6gW-33-6xQ | Stop Hook 持续迭代范式 | 高 |
| 用 Agent Skills 修线上 Bug / skills CLI | https://mp.weixin.qq.com/s/CEBTFlnHdWc56XCGxId1eQ | `npx skills add` 清单（偏故事壳，内核有命令） | 中 |
| CC-Switch 配置切换神器 | https://mp.weixin.qq.com/s/xuwAwfZZiYzKhnPVrvitug | 多端同步 Claude/Codex/Gemini + MCP/Skills | 中 |
| GitHub 十大必装 Claude Skill | https://mp.weixin.qq.com/s/-SKx4qXz3O-TnQkWSYI5Nw | `npx skills add` 推荐清单 | 中 |
| 换掉小龙虾！Hermes 四万星（偏热度） | https://mp.weixin.qq.com/s/7tOTJ1RWs0qbKkx_9vEoNg | 有 Harness 解耦点，整体偏星数文 | 中偏滤 |
| OpenClaw 从新手到中级完整教程 | https://mp.weixin.qq.com/s/iJEoSySawpRz-y7ryMXyzg | 命令多，部分 API 可能已漂移 | 中 |

## 写作方法蒸馏（粗看共性）

1. **钩子类型**：工具背叛（Cursor 太贵）/ 官方新特性抢先 / 源码考古 / Windows 踩坑 / 类比命名（Harness=壳、整合包、USB-C）。
2. **结构高频**：痛点一句 → 分层对照表（MCP vs Skill / Rules vs Skills）→ 可复制命令块 → 作用域（全局/项目）→ 验证命令 → 边界（何时别用）。
3. **可信信号**：完整命令、报错原文、配置文件路径、作者本人工具/AGENTS.md 全文、源码函数名。
4. **应滤信号**：只有「狂揽 X 星」「赋能闭环」、无命令无目录无踩坑。
