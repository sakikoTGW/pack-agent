# Agent Modpack — Developer Guide

**English** | **[中文](DEVELOPERS.zh-CN.md)**

npm: `@sakikotgw/pack-agent` · CLI: `packagent` · Schema: v0.2  
Full pack spec → [PACK_SPEC.md](PACK_SPEC.md)

---

## 5-minute setup

```bash
npm install @sakikotgw/pack-agent   # requires Bun ≥ 1.1.0
```

**CLI**

```bash
packagent agents init
packagent export --agent my-agent
packagent install .agent-pack/exports/my-agent.pack.json --runtime claude-code
```

**Programmatic (Bun / Node)**

```ts
import {
  exportPackFromProject,
  installPackFile,
} from '@sakikotgw/pack-agent'

const cwd = process.cwd()
const { outPath } = await exportPackFromProject(cwd, { agent: 'my-agent' })
const report = await installPackFile(cwd, outPath, { runtime: 'codex' })
```

**MCP**

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

Tools: `pack_detect` · `pack_scan` · `pack_export` · `pack_install` · `pack_sync` · `pack_select` · `pack_eject` · `pack_status` · `pack_diff` · `pack_experience_offset`

---

## Package entry points

| Path | Purpose |
|------|---------|
| `@sakikotgw/pack-agent` | Programmatic API (`src/index.ts`) |
| `packagent` | CLI (`bin/packagent.js` → `src/cli.ts`) |
| `packagent-mcp` | MCP stdio server |
| `mcp/server.ts` | MCP impl; `createAgentPackMcpServer()` for embedding |

Alias bins: `agent-pack` / `agent-pack-mcp`.

---

## CLI commands

| Command | Action |
|---------|--------|
| `packagent detect` | Detect harnesses + list adapters |
| `packagent agents init \| list` | Init / list `.agent-pack/agents.yaml` |
| `packagent export --agent <id>` | Write pack only |
| `packagent pack …` | Selective pack; `--install` to apply |
| `packagent install <file>` | Install existing `.pack.json` |
| `packagent sync …` | Export + install |
| `packagent eject [--name]` | Uninstall via ledger |
| `packagent status` | Lock / ledger / experiences |
| `packagent diff <a> <b>` | Diff packs or locks |

### Common flags

| Flag | Commands | Meaning |
|------|----------|---------|
| `--agent`, `-a <id>` | export / pack / sync | Pack one agent from `agents.yaml` |
| `--all` | export / sync | Full-project scan (legacy) |
| `--runtime`, `-t <id>` | install / sync / export | Single harness target |
| `--from`, `-f <path>` | sync | Install only, skip export |
| `--name`, `-n` | export / pack / sync | Pack file name |
| `--skills`, `--rules`, `--mcp` | pack | Comma-separated selection |
| `--manifest`, `-m` | pack | Path to `select.json` |
| `--install`, `-i` | pack | Install after pack |
| `--capture-as skill\|experience` | export / install | L2 delivery (default `experience`) |
| `--harness` | pack | Same as `--capture-as skill` + merge capture |
| `--on-conflict stop\|skip\|replace` | install / sync | Conflict policy (default `stop`) |
| `--no-bootstrap` | export / install | Skip agent-pack bootstrap skill |
| `--modules hooks,memory,…` | export / install | Optional modules |
| `--force` | eject | Force-remove conflicting skill dirs |

---

## MCP tools

All tools return JSON (`structuredContent` + text). Default `cwd`: `AGENT_PACK_CWD` or process cwd.

### Shared parameters

`cwd` · `runtime` · `agent` · `all` · `capture_as` · `no_bootstrap` · `modules` · `state_dir` · `on_conflict`

See [DEVELOPERS.zh-CN.md](DEVELOPERS.zh-CN.md) for per-tool request/response examples (same schemas).

| Tool | Purpose |
|------|---------|
| `pack_detect` | Harness detection + adapter table |
| `pack_scan` | L1 scan (skills / rules / MCP) |
| `pack_export` | Write `.pack.json`; `dry_run` supported |
| `pack_install` | Install pack → `InstallReport` |
| `pack_sync` | Export + install or `from=` install-only |
| `pack_select` | Partial pack + optional `install: true` |
| `pack_eject` | Ledger-based uninstall |
| `pack_status` | Lock, ledger, experiences index |
| `pack_diff` | Compare two packs or locks |
| `pack_experience_offset` | Tune installed experience jar |

---

## Programmatic API

Import from `@sakikotgw/pack-agent` (TypeScript, run with Bun).

| Function | Role |
|----------|------|
| `exportPackFromProject` / `buildPackFromProject` | Export / build in memory |
| `installPackFile` / `installPack` | Install pack |
| `syncPack` | Export + install pipeline |
| `detectRuntimes` / `scanRuntime` / `getAdapter` | Harness detection & scan |
| `loadAgentsRegistry` / `resolveAgentForExport` | `agents.yaml` |
| `ejectPack` / `packStatus` | Uninstall / status |
| `embedPortableFiles` / `materializePortableBundle` | Bundle I/O |
| `createAgentPackMcpServer` | Embed MCP server |

Types: `PackDoc` · `InstallReport` · `InstallOpts` · `CaptureDeliver` · `ConflictPolicy` · `AgentProfile`

---

## Config quick ref

**`.agent-pack/agents.yaml`** — schema `agent-pack/agents/v1`  
Fields: `author` · `description` · `runtime` · `skills` · `rules` · `mcp` · `captureAs` · `modules`

**`.pack.json`** — schema v0.2 (see [PACK_SPEC.md](PACK_SPEC.md) for the `schema` field value)  
Key fields: `agent` · `knowledge` · `tools.mcp` · `experiences` · `harness` · `bundle.files` · `meta.fidelity`

**Harness ids**: `claude-code` · `codex` · `opencode` · `openclaw` · `hermes` · `gemini-cli` · `windsurf` · `github-copilot` · `astrbot`  
Skipped on default install: `cursor` · `generic-agents`

**State dir** (default `.agent-pack/`): `exports/` · `applied/` · `lock.json` · `experiences/` · `capture/`

**Conflicts**: `stop` | `skip` | `replace` — throws `PackConflictError` on `stop`

---

## Source map

| File | Content |
|------|---------|
| [PACK_SPEC.md](PACK_SPEC.md) | Full schema |
| [../src/index.ts](../src/index.ts) | Public exports |
| [../mcp/server.ts](../mcp/server.ts) | MCP tool registration |
| [../src/adapters.ts](../src/adapters.ts) | Harness adapters |

Issues: https://github.com/sakikoTGW/pack-agent/issues
