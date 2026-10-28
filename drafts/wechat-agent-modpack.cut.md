# 我做了个 Agent 整合包，开源了

最近一直在捣鼓 coding agent 的配置怎么搬——Cursor、Claude Code、Codex、Hermes，各认各的目录，拷来拷去很容易散。

今天和大家分享我做完并开源的东西——**Agent Modpack**。

**Agent Modpack** 是一款面向已经在用 Cursor / Claude Code / Codex 等工具的配置便携工具。你只需把「一个角色」（要用的 skills、规则、MCP）点名进 `agents.yaml`，即可导出一份 `.pack.json`，并在本机检测到的工具上完成安装、按账本卸载；换工具时由 adapter 改路径。搬走的是配置，不是模型，也不是账单。

最近在 GitHub 开源了，npm 也能直接跑。老规矩，先上链接。

GitHub：https://github.com/sakikoTGW/pack-agent  
npm：`@sakikotgw/pack-agent`（需要 Bun ≥ 1.1）

先看整条链长什么样：点名 → 打包 → 投射进各家目录。

![架构总览](wechat-assets/real/proof-shots/arch-overview.png)

本机一查，会装到哪几家，一眼能看见：

![packagent detect](wechat-assets/real/proof-shots/cli-01-detect.png)

## 产品功能亮点

### 1. 从点名到一份包：export 出来就是整份角色

角色不是口头禅。`agents init` 之后，在表里点名要用哪些 skill / rule / MCP，再 `export`：

![agents init](wechat-assets/real/proof-shots/cli-02a-agents-init.png)

![agents.yaml 点名](wechat-assets/real/proof-shots/cli-02b-agents-yaml.png)

![export 打出 .pack.json](wechat-assets/real/proof-shots/cli-04-export.png)

装之前还可以先 `show`——像装 MC 整合包前先看 mod 列表：

![show：装前看包内容](wechat-assets/real/proof-shots/cli-05-show.png)

**亮点总结：**

- `agents.yaml` 圈定边界，`export --agent` 只封一个角色  
- `.pack.json` 内嵌正文，拷走就能装  
- `show` 装前可查 skill / rule / MCP 清单  

### 2. 装进去，壳还认得出；再卸，干净

空目录实测。装前几乎只有一份包：

![装前：只有 packer.pack.json](wechat-assets/real/proof-shots/explorer-01-before.png)

```bash
npx --yes -p @sakikotgw/pack-agent packagent install .\packer.pack.json --runtime codex
```

![install 输出](wechat-assets/real/proof-shots/cli-06-install.png)

![装完：.agents/skills 里出现 agent-pack](wechat-assets/real/proof-shots/explorer-skills-after-install.png)

文件在了不算。本机用 `codex exec` 当面问，它答：

> 能看到，agent-pack 这个 skill 是用来把 skills、rules、MCP、harness prompts 等内容打包/导出/同步成可移植的 agent 包，也可以安装到不同的 Codex/Claude Code 环境里。

账本也记下了写过哪些路径：

![ledger：安装账本](wechat-assets/real/proof-shots/explorer-ledger-after-install.png)

然后卸：

```bash
npx --yes -p @sakikotgw/pack-agent packagent eject --name packer
```

![卸完：技能目录空了](wechat-assets/real/proof-shots/explorer-skills-after-eject.png)

再问同一句——忘了。实录在 `%TEMP%\packagent-codex-verify-20260719-040422`。

同名冲突默认停，不覆盖：

![冲突：默认 stop](wechat-assets/real/proof-shots/cli-09-conflict-stop.png)

**亮点我这里总结如下：**

- `npx -p @sakikotgw/pack-agent packagent …` 可直接跑  
- 装完目录在、模型侧也能报出用途  
- `eject --name` 按账本删；冲突默认 `--on-conflict stop`  

### 3. skill + MCP 同包走，还能让 agent 自己装卸

只拷一页 `SKILL.md` 不够。Hermes 上这份工具包：装前两边都空——

![Hermes skills 装前为空](wechat-assets/real/proof-shots/hermes-01-skills-before.png)

![Hermes mcp 装前无 agent-pack](wechat-assets/real/proof-shots/hermes-02-mcp-before.png)

```bash
npx --yes -p @sakikotgw/pack-agent packagent install .\packer-mcp.pack.json --runtime hermes --global-config
```

![install 输出](wechat-assets/real/proof-shots/hermes-03-install-tooling.png)

装完 skills 和 mcp 列表里都有了：

![skills 出现 agent-pack](wechat-assets/real/proof-shots/hermes-04-skills-after.png)

![mcp 出现 agent-pack](wechat-assets/real/proof-shots/hermes-05-mcp-after.png)

有了 skill + MCP，角色能自己调打包/安装/卸载。我让它把示例角色 `hello-pack` 再 export 一份、装回去——工具包和业务包并排：

![export hello-pack](wechat-assets/real/proof-shots/hermes-07-export.png)

![exports 目录](wechat-assets/real/proof-shots/hermes-08-exports-dir.png)

![skills：agent-pack + hello-pack](wechat-assets/real/proof-shots/hermes-10-skills-with-hello.png)

`eject --name hello-pack` 只卸业务包，工具包还在：

![eject 后 skills](wechat-assets/real/proof-shots/hermes-13-skills-after-eject.png)

壳里若已接上 pack-agent 的 MCP，也可以让 AI 自己 `pack_detect` / `pack_install` / `pack_eject`：

![pack_detect](wechat-assets/real/proof-shots/mcp-15-detect.png)

![pack_install：AI 自己装](wechat-assets/real/proof-shots/mcp-23-install.png)

![pack_eject：AI 自己卸](wechat-assets/real/proof-shots/mcp-25-eject.png)

**同时我做了如下核对：**

- MCP 与 skill 同包投射  
- 多包按包名记账，卸一个不拆另一个  
- 人可以敲 CLI，也可以让 agent 调 MCP 完成同一套动作  

### 4. 经验另走：会变的脾气，不焊进 skill

skills / 规则 / MCP 是文件，进包就能带走。  
对话里用顺的脾气（比如冲突时默认停还是盖）还会变——焊进 skill 就定死了，所以经验不进 skill 树：基座可进包，接上之后写入 `live.json`，下一轮注回。

![经验怎么走](wechat-assets/real/proof-shots/experience-flow.png)

同一会话两轮，注回去的内容跟着变：

![live inject · turn 1](wechat-assets/real/proof-shots/exp-live-inject-turn1.png)

![live inject · turn 2](wechat-assets/real/proof-shots/exp-live-inject-turn2.png)

![live.json](wechat-assets/real/proof-shots/exp-live-json-turn2.png)

**具体边界如下：**

- 经验和 skill 分路，避免把还在漂的习惯焊死  
- 还没录到各家壳真正发给模型的那一层时，包上会标 `L1`  
- 只想拷一个 `SKILL.md`：别装这套，偏重  

## 怎么上手

三步：

```bash
# 1. 安装
npm install -g @sakikotgw/pack-agent

# 2. 点名角色并导出
packagent agents init
packagent export --agent <角色id>

# 3. 安装到本机壳
packagent install .\.agent-pack\exports\<包名>.pack.json
```

先看本机能装哪几家：`packagent detect`。Cursor 要显式加 `--runtime cursor`。细则在仓库 `docs/`。

项目还在迭代，Issue 和 PR 都欢迎。

GitHub：https://github.com/sakikoTGW/pack-agent  
npm：`@sakikotgw/pack-agent`

打牌仓库：https://github.com/sakikoTGW/STS2_Skills  

转载注明项目地址。文中截图均为本机实录。
