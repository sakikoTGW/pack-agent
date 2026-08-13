---
name: pack-agent-dsh
description: >-
  pack-agent DeepSeek Harness plugin. Project .pack.json/.pack.zip into
  .agent-pack/modpacks with mods/, search/allow/deny via SQLite catalog.
  Use when the user wants 装整合包, packagent, or migrate a Cursor pack into DSH.
---

# pack-agent on DeepSeek Harness

You are running inside DeepSeek Harness with the pack-agent manager plugin.

Host installs this manager **once**:

```
dsh plugin --profile web add <pack-agent package or repo root>
```

Do not `dsh plugin add` a projected pack under `.agent-pack/modpacks/`.

## Tools

- `packagent_detect` — which harnesses exist in the project
- `packagent_compile` — Pack → projected `dsh.bundle` directory (no catalog)
- `packagent_project` — project into `.agent-pack/modpacks` and index
- `packagent_map` — Cursor/ccui-pack → DSH modpack (`mods/` + catalog); `from` hydrates refs
- `packagent_search` — search projected packs
- `packagent_allow` / `packagent_deny` — session allow-list (files stay on disk)
- `packagent_list` — list projected packs

## Slash commands

- `/packagent-detect`
- `/packagent-compile <pack.json|pack.zip>`
- `/packagent-project <pack.json|pack.zip>`
- `/packagent-map <pack.json|pack.zip>`
- `/packagent-search <query>`
- `/packagent-allow <pack-id>`

After project/map, tell the user to allow the pack id if they want the session to see it:

```
packagent dsh allow <id>
```
