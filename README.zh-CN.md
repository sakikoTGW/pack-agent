# Agent Modpack

[![awesome · DSH plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

[English](README.md) | 中文

像 MC 整合包一样管理 DeepSeek Harness 配置。Agent Modpack（`packagent`）把 skills、rules、MCP 打成 `.pack.zip`，装进 Claude Code、Codex、Cursor 等 harness。卸装按账本撤。

需要 [Bun](https://bun.sh) ≥ 1.1。npm：[`@sakikotgw/pack-agent`](https://www.npmjs.com/package/@sakikotgw/pack-agent)。

DeepSeek Harness 用另一个包：[`@sakikotgw/pack-agent-dsh`](https://www.npmjs.com/package/@sakikotgw/pack-agent-dsh)。见 [dsh-plugin/README.zh.md](dsh-plugin/README.zh.md)。

## 安装

### 通过 npm

```sh
npm install @sakikotgw/pack-agent
packagent detect
```

只跑一次：

```sh
npm exec --yes --package=@sakikotgw/pack-agent -- packagent install foo.pack.zip
```

Windows 不要写成 `npx --package @sakikotgw/pack-agent -- pack-agent`，命令名是 `packagent`。

## 使用

```sh
packagent agents init
packagent export --agent <id>
packagent install .agent-pack/exports/<id>.pack.json --runtime claude-code
packagent eject --name <id>
```

`--runtime` 只装一家。不加则装到 `packagent detect` 里 `Will install to` 列出的 harness。默认只写当前项目。要写用户全局配置时加 `--global-config`。

## MCP

```json
{
  "mcpServers": {
    "agent-pack": {
      "command": "bun",
      "args": ["node_modules/@sakikotgw/pack-agent/mcp/server.ts"],
      "env": { "AGENT_PACK_CWD": "." }
    }
  }
}
```

## 支持的 harness

| id | skills | rules | MCP |
|----|--------|-------|-----|
| `claude-code` | `.claude/skills` | `CLAUDE.md` | `.mcp.json` |
| `codex` | `.agents/skills` | `AGENTS.md` | `.codex/config.toml` |
| `opencode` | `.opencode/skills` | `AGENTS.md` | `opencode.json` |
| `openclaw` | `skills.load.extraDirs` | `AGENTS.md` | `config/mcporter.json` |
| `hermes` | external_dirs | `AGENTS.md` | `~/.hermes/config.yaml` |
| `gemini-cli` | `.gemini/skills` | `GEMINI.md` | `.gemini/settings.json` |
| `windsurf` | `.windsurf/skills` | — | `.windsurf/mcp_config.json` |
| `github-copilot` | — | `.github/copilot-instructions.md` | `.vscode/mcp.json` |

## 从源码运行

```sh
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
```

## 许可证

[MIT](LICENSE)
