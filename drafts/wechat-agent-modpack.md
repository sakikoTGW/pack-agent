# Agent Modpack：Agent 也有整合包了——一包带走一个角色

![封面](wechat-assets/cover-uika-sakiko-cover-ready.png)

换过 coding agent 的人都懂这一幕：你在 Claude Code 里调顺了 skills、rules 和 MCP，换到 Codex 却面对另一套目录。你拷过去一半，漏掉一半；想卸载时，已经不知道该删哪些文件。

也有人说：把文件夹丢给 agent，让它「装一下」不就行了？

**agent 确实装得动。但要装完整、装正确，并且还能卸干净，是另一回事。**

今天和大家分享我做完并开源的东西——**Agent Modpack**。

> **像装 MC 整合包一样，你可以用它安装自己的 agent。**  
> 你把一个角色打成一份 `.pack.json`，装进本机上的壳，再按账本执行 `eject`。

本工具面向已经在用 Cursor、Claude Code、Codex、Hermes 的人。你把要用的 skills、规则和 MCP 点名进包，导出可携带的文件；adapter 再写入各家目录。它只处理配置的打包与安装，不包含模型，也不涉及各家计费。

我最近已经把它开源了。老规矩，先上链接。

GitHub：https://github.com/sakikoTGW/pack-agent  
npm：`@sakikotgw/pack-agent`（需要 Bun ≥ 1.1）

你可以把一份包甩给任意 coding agent，并说一句「装一下这个」：

```bash
npx --yes -p @sakikotgw/pack-agent packagent install foo.pack.json
```

在 Windows 上请使用 `-p … packagent` 这种写法。旧写法 `--package … -- pack-agent` 会失败。

---

## 一、实现了什么

我先把能力说清楚。截图演示放fa在下一节。

### 1. 角色级打包，不是单页 skill

OpenSkills 一类工具解决的是 **skill 装载**。Agent Modpack 做的是 **整份角色快照**：

- 你在 `agents.yaml` 里点名要用的 skills、rules 和 MCP。  
- 你运行 `export --agent`，工具会打出一份 `.pack.json`。若漏写 `--agent`，命令会直接拒绝。  
- 包内的 bundle 会内嵌正文，换机器也能安装。  
- 你运行 `show`，可以在安装前查看清单，就像装整合包前先看 mod 列表。  

### 2. 装进本机壳，按账本卸干净

- 你运行 `detect`，可以看到本机有哪些 harness 在场。  
- 你运行 `install`，adapter 会把内容写入各家目录。若只想装一家，可以加 `--runtime`。  
- 安装成功后，工具会写入 **install-ledger**。你运行 `eject --name`，工具会按账本回滚。  
- 若遇到同名冲突，默认策略是 `--on-conflict stop`，工具不会偷偷覆盖。  
- 多包并存时，工具按包名记账。你卸其中一个，不会拆掉另一个。  

### 3. skill 与 MCP 同包；人敲 CLI，agent 也能装

- 一份 pack 可以同时投射 skills 与 MCP，这样「整角色」才站得住。  
- 你在壳里接上 pack-agent 的 MCP 后，AI 可以调用 `pack_install` 与 `pack_eject`。这些调用与 CLI 走同一套逻辑。  
- 你可以让 agent 自己装。你甩给它的应是一份 `.pack.json` 合同，而不是一堆散目录。  

### 4. 经验另路，不焊进 skill

skills、rules、MCP 适合写「相对稳定、可以落盘当合同」的东西。对话里用顺的脾气性格经验不是这一类：例如冲突时默认停还是覆盖、哪条路试通过——它会跟着会话往前漂。

如果把这类习惯蒸馏进 `SKILL.md`，落盘那一刻就被钉死；下一轮对话里脾气已经变了，磁盘上的 skill 却还假装没变。所以**故意不把经验焊进 skill 树**：skill 继续管稳定合同，经验单独开一条会变的路。

具体做法：基座文案可以进包（`.agent-pack/experiences/`），装上壳的 hook 之后，每一轮把最新内容写入 `live.json`，再注回下一轮上下文。经验继续活在「会变」的那一层，而不是伪装成又一份静态 skill。

另外，若还没录到各家壳真正发给模型的那一层（瓶口），包会标 **L1**：配置仍可搬，但不保证换壳、换模型后请求体字节级一致。

**功能一览：**

| 能力 | 入口 |
|------|------|
| 点名角色 | `agents init` / `agents.yaml` |
| 导出 / 预览 | `export --agent` / `show` |
| 检测 / 安装 / 卸载 | `detect` / `install` / `eject --name` |
| 同步 | `sync --agent` |
| 壳内自装自卸 | MCP：`pack_*` |
| 零全局安装试用 | `npx -p @sakikotgw/pack-agent packagent …` |

### 其实很简单，只需要 3 步

```bash
# 1. 安装（反复用）
npm install -g @sakikotgw/pack-agent

# 2. 点名并导出
packagent agents init
packagent export --agent <角色id>

# 3. 装进本机壳
packagent install .\.agent-pack\exports\<包名>.pack.json
```

若只想临时装一次，你可以直接用文首那条 `npx` 命令。

---

## 二、演示

下面的截图都是本机实录。

### 演示 1：点名、导出、show

我先在表里点名角色，再运行 `export --agent`：

![agents.yaml 点名](wechat-assets/real/proof-shots/cli-02b-agents-yaml.png)

![export 打出 .pack.json](wechat-assets/real/proof-shots/cli-04-export.png)

安装前，我再运行 `show`：

![show：装前看包内容](wechat-assets/real/proof-shots/cli-05-show.png)

### 演示 2：在 Codex 上安装、确认、卸载

我在空目录里实测。安装前，目录里几乎只有一份包：

![装前：只有 packer.pack.json](wechat-assets/real/proof-shots/explorer-01-before.png)

```bash
npx --yes -p @sakikotgw/pack-agent packagent install .\packer.pack.json --runtime codex
```

![装完：.agents/skills 里出现 agent-pack](wechat-assets/real/proof-shots/explorer-skills-after-install.png)

目录里出现文件，还不算装成功。我用 `codex exec` 当面提问，它能答出 agent-pack 的用途。

账本会记下工具写过哪些路径：

![ledger：安装账本](wechat-assets/real/proof-shots/explorer-ledger-after-install.png)

```bash
npx --yes -p @sakikotgw/pack-agent packagent eject --name packer
```

![卸完：技能目录空了](wechat-assets/real/proof-shots/explorer-skills-after-eject.png)

我再用同一句话去问，它已经答不上来了。

### 演示 3：在 Hermes 上同包投射 skill 与 MCP；由 AI 自己装卸

安装完成后，skills 列表和 MCP 列表里都出现了 agent-pack：

![skills 出现 agent-pack](wechat-assets/real/proof-shots/hermes-04-skills-after.png)

![mcp 出现 agent-pack](wechat-assets/real/proof-shots/hermes-05-mcp-after.png)

我在壳里接好 MCP 之后，让 AI 自己调用装卸工具：

![pack_install：AI 自己装](wechat-assets/real/proof-shots/mcp-23-install.png)

![pack_eject：AI 自己卸](wechat-assets/real/proof-shots/mcp-25-eject.png)

---

## 三、它怎么工作（架构与原理）

这一节再往下一层：一份包为什么能进入很多壳，卸载时又为什么找得回路径。

你可以先记住下面这三层心智模型，后面的表会更好读：

| 层 | 是什么 | 例子 |
|----|--------|------|
| **Harness** | 负责跑 loop 的壳 | Claude Code / Codex / Hermes |
| **Agent** | 你在壳上选定的角色 | `agents.yaml` 里点名的那一套 |
| **Prefunction** | 能打进 pack、可以搬走的部分 | skills / rules / MCP / 经验罐头 |

Prefunction **不会搬动模型权重**，它只影响壳拼出来的 fixed_input。

数据会按这条链路流动：

1. 你在 `agents.yaml` 里定义角色。  
2. 你运行 `export`，得到内嵌正文的 `.pack.json`。  
3. 你运行 `install`，adapter 写入各 harness 的装载口。  
4. 工具写入 install-ledger。  
5. 你运行 `eject --name`，工具按账本回滚。  

![架构总览](wechat-assets/real/proof-shots/arch-overview.png)

你也可以先查本机情况：

![packagent detect](wechat-assets/real/proof-shots/cli-01-detect.png)

这里有两件关键设计：

1. **统一度量衡**：`.pack.json` 加 bundle，让角色可以版本化、可以分享、可以 eject。  
2. **adapter**：各家目录、注入时机和 MCP 合并方式不同，由适配器负责投射。若缺少瓶口录制，包会标成 **L1**。  

---

## 四、对照与清单（密表区）

下面是对照表与清单。若前面已经够用，你可以直接跳过。

### 和拷目录 / OpenSkills 差在哪

| | 让 agent 拷目录 | OpenSkills 类 | Agent Modpack |
|--|----------------|---------------|---------------|
| **载体** | 散文件夹 | 多为 `SKILL.md` | 一份 `.pack.json` + bundle |
| **范围** | 看 agent 记性 | 装 skill | skills / rules / MCP 同包 |
| **卸载** | 通常不知删哪 | 往往无跨组件账本 | install-ledger + `eject --name` |
| **跨壳** | 路径靠口述 | 通常一家约定 | adapter 投射本机检测到的 harness |
| **何时用** | 临时试一把 | 只要 skill 装载 | 要搬整角色、能卸、能甩给别人 |

### 默认行为

| 行为 | 默认 | 想改就… |
|------|------|---------|
| 装到哪 | 本机 `detect` 到的多家 | `--runtime <id>` 只装一家 |
| Cursor | **跳过**（即便 detect 到） | `--runtime cursor` |
| 用户全局配置 | **不写** | `--global-config` |
| 同名冲突 | `stop` | `--on-conflict skip\|replace` |

### 保真度

| 层 | 包里有什么 | install 后 |
|----|------------|------------|
| **L1** | skills / rules / MCP | 写入各 harness 原生目录 |
| **L2** | prompt / tool schema / reminders 等 | 规则或经验罐头 + hook |
| **L3+** | 装配顺序 / loop | 写入 pack，尽力投射 |

换模型之后，行为会漂移。若缺少瓶口录制，包就标成 L1：配置可以搬，但不保证各家发给模型的请求体在字节级一致。

### 支持的 Harness（装载口摘要）

| id | skills | rules | MCP |
|----|--------|-------|-----|
| `claude-code` | `.claude/skills` | `CLAUDE.md` | `.mcp.json` |
| `codex` | `.agents/skills` | `AGENTS.md` | `.codex/config.toml` |
| `opencode` | `.opencode/skills` | `AGENTS.md` | `opencode.json` |
| `hermes` | external_dirs | `AGENTS.md` | `~/.hermes/config.yaml` |
| `openclaw` | extraDirs / staging | `AGENTS.md` | mcporter 等 |
| `gemini-cli` | `.gemini/skills` | `GEMINI.md` | settings |
| `windsurf` | `.windsurf/skills` | — | mcp_config |
| `github-copilot` | — | copilot-instructions | `.vscode/mcp.json` |

完整字段与边界见仓库里的 `docs/PACK_SPEC.md`。

---

## 五、给开发者

若你要接入 MCP、修改 adapter，或把 pack 嵌进自己的工作流，请看这一节。

### 常用 CLI

| 命令 | 作用 |
|------|------|
| `packagent detect` | 列出本机在场 harness 与 Will install to |
| `packagent agents init` | 生成 `agents.yaml` |
| `packagent export --agent <id>` | 导出角色包 |
| `packagent show <file>` | 安装前查看清单 |
| `packagent install <file>` | 安装 |
| `packagent eject --name <pack>` | 按账本卸载 |
| `packagent list` / `status` / `diff` | 已装列表 / lock 与 ledger / 对比 |
| `packagent sync --agent <id>` | 先 export，再 install |

CLI 别名还有 `pack-agent` 与 `agent-pack`。运行时需要 **Bun ≥ 1.1**。

### MCP 工具（壳内让 AI 调用）

| 工具 | 作用 |
|------|------|
| `pack_detect` | 检测 harness |
| `pack_scan` / `pack_export` / `pack_show` | 扫描 / 导出 / 预览 |
| `pack_install` / `pack_sync` / `pack_eject` | 安装 / 同步 / 卸载 |
| `pack_list` / `pack_status` / `pack_select` | 列表 / 状态 / 选择 |

### 项目侧目录

```
.agent-pack/
  agents.yaml
  exports/*.pack.json
  applied/<pack>-ledger.json
  lock.json
  experiences/
```

Schema 为 `ccui-pack/v0.2`。开发说明见 `docs/DEVELOPERS.zh-CN.md`，规范见 `docs/PACK_SPEC.md`。

欢迎提交 Issue 与 PR：https://github.com/sakikoTGW/pack-agent

![这是封面](wechat-assets/cover-uika-sakiko-cover-ready.png)

---

转载时请注明项目地址。文中截图均为本机实录。

GitHub：https://github.com/sakikoTGW/pack-agent  
npm：`@sakikotgw/pack-agent`
