# Agent Modpack

**English** | **[中文](README.zh-CN.md)**

> **Install your agent like an MC modpack.**

**Agent Modpack** — **Export / import** your agent like an MC modpack: define a role, ship a `.pack.json`, run `packagent install` into Claude Code, Codex, and other harnesses.

[![npm version](https://img.shields.io/npm/v/@sakikotgw/pack-agent.svg)](https://www.npmjs.com/package/@sakikotgw/pack-agent)
[![license](https://img.shields.io/npm/l/@sakikotgw/pack-agent.svg)](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
[![bun](https://img.shields.io/badge/bun-%3E%3D1.1.0-black?logo=bun)](https://bun.sh)

Pack **one agent** (skills / rules / MCP) into a portable `.pack.json`, then install on harnesses **detected on this machine** (multiple by default; use `--runtime` for one target).

```bash
npm install @sakikotgw/pack-agent
packagent detect          # see which harnesses will receive the pack
packagent install foo.pack.json --runtime claude-code   # single target
```

> CLI: `packagent` · npm: `@sakikotgw/pack-agent` · schema: v0.2 · [Developer guide](docs/DEVELOPERS.md)

---

## Why

### Three layers: Harness · Agent · Prefunction

```
  user turn (input₁)
       │
       ▼
  ┌─ Harness ─────────────────────────────────────┐
  │  Claude Code / OpenClaw / Codex / Hermes …     │  ← loop, tools, request assembly
  │       │                                        │
  │       ├─ loads prefunctions from the pack      │  ← modpack captures this
  │       │     skills / rules / MCP / experiences │
  │       └─ harness-native assembly (per product) │  ← wire capture + adapters
  └────────────────────────────────────────────────┘
       │
       ▼
  fixed_input (input₂)  ──►  LLM  ──►  reply
       │
       ▼
  Harness runs tools, next turn…
```

**Harness** = runtime shell. Claude Code, OpenClaw, Codex, Hermes live here — like MC 1.20 / Forge: paths, inject timing, tool permissions, multi-turn loop.  
**Agent** = a **role** on one harness: which skills, rules, MCP — bound in `agents.yaml`.  
**Prefunction** = the **packable slice** of that agent: skills, rules, MCP, experience jars. They do not change model weights; they change how the harness builds **fixed_input**.

OpenClaw **runs** the agent; a skill **loads into** it and joins prompt assembly.

A modpack = one agent’s prefunction snapshot — `.pack.json` + bundle, MC modpack style.

Ship it: `packagent install`, API key, same harness + same model → same fixed_input, approximately.  
PCL: download modpack → import → play.

### Loading

Prefunctions go in the bundle; **how they mount on a harness** varies:

- Paths: `.claude/skills`, `.agents/skills`, `.cursor/rules/*.mdc`, …
- Injection: SessionStart, system-reminder, which turn pulls skill body
- Permissions: MCP allowlists, tool approval

One pack, many harnesses — adapters handle **where to write, when to inject, who may call tools**.

Two jobs:

1. **One schema** — `.pack.json` + bundle; versioned, ejectable prefunctions.
2. **Wire capture** — record the final request body (prompt, tool schema, assembly order); replay via experience / rules for fixed_input. Missing capture → L1.

### `packagent` = launcher

Detect harness → pick pack → install / eject. Adapters write mount points per harness.

### export / install

```
  prefunctions (skills/rules/MCP/jars)   (optional) wire capture
              └──────── export ──────────┘
                          │
                     .pack.json
                          │
              install ────┴───► adapter → per-harness dirs
```

---

## Common scenarios

| Pain | What we do |
|------|------------|
| Re-configure skills/MCP when switching machines or tools | **Portable bundle** — copy `.pack.json`, run `install` |
| Multiple agent roles in one repo | Boundaries in `.agent-pack/agents.yaml`; `export --agent` packs one |
| No clean uninstall after install | **install-ledger** + `packagent eject --name` |
| Share only some skills with teammates | `pack --skills` / `--manifest` selective export |

---

## Core concepts

```
  agents.yaml          export --agent        .pack.json          install
 ┌─────────────┐      ──────────────►   ┌──────────────┐    ──────────────►  Claude Code
 │ packer      │                        │ embedded      │                     Codex
 │ debugger    │                        │ bundle        │                     OpenClaw …
 └─────────────┘                        │ skills/mcp    │
       ▲                                └──────────────┘
       │ many agents per harness
       └── bare export without --agent → rejected (agent-required)
```

| Term | Meaning |
|------|---------|
| **Harness** | Runtime shell: Claude Code, OpenClaw, Codex… — loop, request assembly, tools |
| **Agent** | Role on one harness: skills + rules + MCP (`agents.yaml`) |
| **Prefunction** | Packable slice: skills, rules, MCP, experience jars |
| **Pack** | One agent’s prefunction snapshot; `export --agent` packs one role |
| **Bundle** | Embedded file contents — required for cross-machine install |

---

## Install

**Requires [Bun](https://bun.sh) ≥ 1.1.0** (CLI/MCP run TypeScript via Bun).

```bash
# project-local
npm install @sakikotgw/pack-agent

# global
npm install -g @sakikotgw/pack-agent

packagent --help
packagent detect
```

---

## Quick start

### 1. Define an agent

```bash
packagent agents init    # creates .agent-pack/agents.yaml
packagent agents list
```

```yaml
# .agent-pack/agents.yaml
schema: agent-pack/agents/v1

agents:
  my-agent:
    author: you
    description: Shown on the exported pack
    runtime: codex                    # primary harness for skill scan
    skills: [agent-pack, my-skill]
    rules: [AGENTS.md]
    mcp: [agent-pack]
    captureAs: experience           # skill | experience (default: experience)
```

### 2. Export (pack)

```bash
packagent export --agent my-agent
# → .agent-pack/exports/my-agent.pack.json
```

Alternatives:

```bash
packagent pack --skills brainstorming,verification-before-completion
packagent pack --manifest .agent-pack/select.json
packagent export --all              # legacy: full-project scan
```

### 3. Install

```bash
packagent detect
# Shows Detected / Will install to — only harnesses present on THIS machine

packagent install .agent-pack/exports/my-agent.pack.json

# Single harness (recommended when you know the target)
packagent install .agent-pack/exports/my-agent.pack.json --runtime claude-code
packagent install .agent-pack/exports/my-agent.pack.json --runtime codex

# One-shot export + install (also accepts --runtime)
packagent sync --agent my-agent --runtime codex
```

**Default behavior**: project the pack to **each detected harness** (skips `cursor`, `generic-agents`).  
If both Claude Code and Codex are present, skills land in `.claude/skills` **and** `.agents/skills`.  
To install to **one** harness only → pass **`--runtime`**.

### 4. Uninstall

```bash
packagent eject --name my-agent
```

---

## Claude Code bootstrap

Paste the prompt below into Claude Code, or configure MCP manually:

<details>
<summary><strong>Setup prompt for Claude (click to expand)</strong></summary>

```text
1. npm install @sakikotgw/pack-agent (Bun must be installed)
2. Add agent-pack MCP to .mcp.json (JSON below)
3. packagent agents init → scan project skills and help fill agents.yaml
4. packagent sync --agent <id> to bootstrap
When I say "pack agent X", use pack_export(agent=X) or packagent export --agent X
```

</details>

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

Full example: [mcp/config.example.json](mcp/config.example.json)

### MCP tools

| Tool | Purpose |
|------|---------|
| `pack_detect` | Detect present harnesses |
| `pack_scan` | Scan skills / rules / MCP |
| `pack_export` | Write `.pack.json` (supports `agent`) |
| `pack_install` | Install an existing pack |
| `pack_sync` | Export + install |
| `pack_select` | Selective pack |
| `pack_eject` | Uninstall via ledger |
| `pack_status` | Lock / ledger / experiences |

---

## CLI commands

| Command | Description |
|---------|-------------|
| `packagent agents list \| init` | List / init agent definitions |
| `packagent export --agent <id>` | Export one agent (**recommended**) |
| `packagent pack --skills …` | Selective pack |
| `packagent install <file>` | Install pack |
| `packagent sync --agent <id>` | Export + install |
| `packagent detect` | List detected harnesses |
| `packagent eject --name <pack>` | Uninstall |
| `packagent status` | Lock / ledger status |
| `packagent diff` | Diff packs or locks |

---

## Supported harnesses

| id | skills | rules | MCP config |
|----|--------|-------|------------|
| `claude-code` | `.claude/skills` | `CLAUDE.md` | `.mcp.json` |
| `codex` | `.agents/skills` | `AGENTS.md` | `.codex/config.toml` |
| `opencode` | `.opencode/skills` | `AGENTS.md` | `opencode.json` |
| `openclaw` | `.agents/skills` | `AGENTS.md` | `openclaw.json` |
| `hermes` | external_dirs | `AGENTS.md` | `~/.hermes/config.yaml` |
| `gemini-cli` | `.gemini/skills` | `GEMINI.md` | `.gemini/settings.json` |
| `windsurf` | `.windsurf/skills` | — | `.windsurf/mcp_config.json` |
| `github-copilot` | — | `.github/copilot-instructions.md` | `.vscode/mcp.json` |

By default, install targets harnesses listed under **`Will install to`** in `packagent detect`. Use **`--runtime <id>`** to install to a single harness.

---

## Pack format (`.pack.json`)

Schema: **v0.2** (see `schema` field in [PACK_SPEC.md](docs/PACK_SPEC.md)) · full spec → [docs/PACK_SPEC.md](docs/PACK_SPEC.md)

<details>
<summary><strong>Top-level fields (click to expand)</strong></summary>

| Field | Description |
|-------|-------------|
| `schema` | pack schema v0.2 |
| `name` / `version` / `author` / `description` | Pack identity |
| `agent` | `{ id, harness? }` — matches `agents.yaml` |
| `runtime` | Primary harness used at export |
| `knowledge.skills[]` / `rules[]` | L1 manifest + version + contentHash |
| `tools.mcp[]` | MCP server definitions |
| `experiences[]` | L2 experience jars (SessionStart injection) |
| `harness` / `assembly` / `model` | L2–L3 capture distill (optional) |
| `bundle.files[]` | **Portable core** — embedded skill/rule bodies |
| `resolution` | packContentHash, agentPackCli lock |
| `meta.fidelity` | `L1` \| `L2` \| … — honest fidelity label |

</details>

<details>
<summary><strong>Fidelity layers (click to expand)</strong></summary>

| Layer | Content | After install |
|-------|---------|---------------|
| **L1** | skills / rules / MCP | Native harness directories |
| **L2** | prompt / tool schema / reminders | `captureAs=skill` → rules; `experience` → jar + hook |
| **L3+** | assembly order / loop | Stored in pack, best-effort projection |

Model swap → behavior drifts. Packs label L1–L4; config travels.

</details>

<details>
<summary><strong>Project layout (click to expand)</strong></summary>

```
.agent-pack/
  agents.yaml          # agent definitions
  exports/*.pack.json  # export output
  applied/<pack>.json  # install manifest
  applied/<pack>-ledger.json
  lock.json
  experiences/
  pack.ignore
  project.yaml
```

</details>

<details>
<summary><strong>JSON skeleton (click to expand)</strong></summary>

```jsonc
{
  "schema": "ccui-pack/v0.2",
  "name": "my-agent",
  "version": "0.2.0",
  "author": "you",
  "agent": { "id": "my-agent", "harness": "codex" },
  "knowledge": {
    "skills": [{ "name": "agent-pack", "version": "0.2.0", "contentHash": "sha256:…" }],
    "rules": [{ "name": "AGENTS.md", "format": "agents-md" }]
  },
  "tools": { "mcp": [{ "name": "agent-pack", "type": "stdio", "command": "bun" }] },
  "bundle": {
    "portable": true,
    "files": [{ "path": "skills/agent-pack/SKILL.md", "content": "…" }]
  },
  "resolution": { "packContentHash": "sha256:…", "agentPackCli": "0.2.0" },
  "meta": { "fidelity": "L1" }
}
```

</details>

---

## Development

```bash
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
bun test
```

---

## Links

| | |
|---|---|
| **npm** | https://www.npmjs.com/package/@sakikotgw/pack-agent |
| **Issues** | https://github.com/sakikoTGW/pack-agent/issues |
| **Spec** | [docs/PACK_SPEC.md](docs/PACK_SPEC.md) |
| **Developers** | [docs/DEVELOPERS.md](docs/DEVELOPERS.md) · [中文](docs/DEVELOPERS.zh-CN.md) |

## License

[MIT](LICENSE)
