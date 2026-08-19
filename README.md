# Agent Modpack

[![awesome · DSH plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

English | [中文](README.zh-CN.md)

Manage DeepSeek Harness config like a Minecraft modpack. Agent Modpack (`packagent`) packs skills, rules, and MCP into a `.pack.zip` and installs them into Claude Code, Codex, Cursor, and other harnesses. Uninstall uses the install ledger.

Requires [Bun](https://bun.sh) ≥ 1.1. npm: [`@sakikotgw/pack-agent`](https://www.npmjs.com/package/@sakikotgw/pack-agent).

agent-pack for DSH lives in [`agent-pack-dsh/`](agent-pack-dsh/README.md). Product design: [`agent-pack-dsh/docs/PRODUCT.md`](agent-pack-dsh/docs/PRODUCT.md). Plugin package: [`@sakikotgw/pack-agent-dsh`](https://www.npmjs.com/package/@sakikotgw/pack-agent-dsh). See [agent-pack-dsh/plugin/README.md](agent-pack-dsh/plugin/README.md).

## Install

### From npm

```sh
npm install @sakikotgw/pack-agent
packagent detect
```

One-off:

```sh
npx --yes -p @sakikotgw/pack-agent packagent install foo.pack.json
```

On Windows, `npx --package @sakikotgw/pack-agent -- pack-agent` fails. The bin name is `packagent`.

## Use

```sh
packagent agents init
packagent export --agent <id>
packagent install .agent-pack/exports/<id>.pack.json --runtime claude-code
packagent eject --name <id>
```

`--runtime` installs to one harness. Without it, install targets every harness listed under `Will install to` in `packagent detect`. Install writes the current project only. Pass `--global-config` to write user-global harness config.

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

## Supported harnesses

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

## Run from source

```sh
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
```

## License

[MIT](LICENSE)
