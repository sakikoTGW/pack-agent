---
name: pack-agent-dsh
description: >-
  pack-agent DeepSeek Harness plugin. Project .pack.json/.pack.zip into
  .agent-pack/modpacks with mods/, search/allow/deny via SQLite catalog.
  Use when the user wants 装整合包, packagent, or migrate a Cursor pack into DSH.
---

# pack-agent on DeepSeek Harness

Host installs this manager once:

```
dsh plugin --profile web add @sakikotgw/pack-agent-dsh
```

Do not `dsh plugin add` a projected pack under `.agent-pack/modpacks/`.

## Tools

- `packagent_detect` — which harnesses exist in the project
- `packagent_compile` — Pack → projected directory (no catalog)
- `packagent_project` — project into `.agent-pack/modpacks` and index
- `packagent_map` — Cursor/ccui-pack → DSH modpack (`mods/` + catalog); `from` hydrates refs
- `packagent_search` — search projected packs
- `packagent_allow` / `packagent_deny` — workspace live whitelist (files stay on disk; does not rewrite saved presets)
- `packagent_set_save` / `packagent_set_load` / `packagent_set_list` — named whitelist presets for this workspace
- `packagent_list` — list projected packs

## Slash commands

- `/packagent-detect`
- `/packagent-compile <pack.json|pack.zip>`
- `/packagent-project <pack.json|pack.zip>`
- `/packagent-map <pack.json|pack.zip>`
- `/packagent-search <query>`
- `/packagent-allow <pack-id>`
- `/packagent-set-save <name>`
- `/packagent-set-load <name>`
- `/packagent-set-list`

After project/map, allow the pack id if this workspace should see it:

```
packagent dsh allow <id>
```

Save / load a named whitelist for the whole workspace (every agent in this directory shares it):

```
packagent dsh set-save <name>
packagent dsh set-load <name>
```
