# 放假拷配置拷到想打人

Claude Code 一套目录，Codex 又一套。skills、规则、MCP 分开放；卸的时候，已经不知道该删哪几个文件。

Hermes 更气人：`install` 绿灯，`config.yaml` 里也有 hook，下一轮对话罐头是空的。我对着本机 `agent/shell_hooks.py` 看过——`pre_llm_call` 只要 stdout 里的 `{"context":"..."}`。我们当时吐 Claude 那套形状，解析结果直接是 `None`。不是报错，是静默当没发生。

文件在了，壳可以不认。拷文件夹解决不了这件事。

于是做了 **Agent Modpack**：面向已经在用 Cursor、Claude Code、Codex、Hermes 的人。你把要用的 skills、规则和 MCP 点名成一个角色，导出一份 `.pack.json`，装进本机壳；安装时记下写过哪些路径，`eject` 只按这本账删。搬走的是配置，不是模型，也不是账单。像 MC 整合包那样整份带走。

装包用这个 CLI（源码在 [sakikoTGW/pack-agent](https://github.com/sakikoTGW/pack-agent)，npm `@sakikotgw/pack-agent`，要 Bun）：

```bash
npx --yes -p @sakikotgw/pack-agent packagent install foo.pack.json
```

我这台 Windows 上，旧文档那种 `npx --package … -- pack-agent` 会提示找不到命令。写成上面这样才行。

---

说跑过，就得真跑。

空目录里只丢一份 `packer.pack.json`，装进 Codex：

```bash
npx --yes -p @sakikotgw/pack-agent packagent install .\packer.pack.json --runtime codex
```

![装完：技能目录里出现 agent-pack](wechat-assets/real/proof-shots/explorer-skills-after-install.png)

装完我没急着庆祝。文件在了，我还是怕它装聋。当面问：看得到 skill「agent-pack」吗？它说能，用途也讲对了。

然后卸：

```bash
npx --yes -p @sakikotgw/pack-agent packagent eject --name packer
```

![卸完：技能目录空了](wechat-assets/real/proof-shots/explorer-skills-after-eject.png)

再问同一句。忘了。

就这样。装得上，认得出，卸得掉。

你要是也在几家壳之间搬同一套习惯，自己封一份再装：

```bash
npm install -g @sakikotgw/pack-agent
packagent agents init
packagent export --agent <角色id>
packagent install .\.agent-pack\exports\<包名>.pack.json
```

只想拷一个 `SKILL.md` 走人的，会嫌重——正常，别装这套。

打牌那篇还在：《一个连进阶1都无法通关的 STS2_Skill？》。先这样。
