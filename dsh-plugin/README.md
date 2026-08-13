# pack-agent-dsh

English | [中文](README.zh.md)

`@sakikotgw/pack-agent-dsh` is a [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) plugin.

It projects `.pack.json` / `.pack.zip` into `.agent-pack/modpacks/` and exposes an allow-list of skills. Install this plugin once. Do not `dsh plugin add` each projected pack.

Claude Code, Codex, and Cursor use [`@sakikotgw/pack-agent`](https://www.npmjs.com/package/@sakikotgw/pack-agent).

## Install

Install [Node.js](https://nodejs.org/), then:

```sh
dsh plugin --profile web add @sakikotgw/pack-agent-dsh
```

pnpm 9: add `-w` if you get `ERR_PNPM_ADDING_TO_ROOT`.

Add the [`dsh-plugin`](https://github.com/topics/dsh-plugin) topic to plugin repositories so they can be found.

## Use

```sh
packagent dsh project path/to/foo.pack.json
packagent dsh allow <id>
```

`packagent` is the CLI in `@sakikotgw/pack-agent`. Cursor / old packs: `packagent dsh map`.

Tools registered in DSH: `packagent_project`, `packagent_map`, `packagent_search`, `packagent_allow`, `packagent_deny`, `packagent_list`, `packagent_set_save`, `packagent_set_load`, `packagent_set_list`.

The allow-list is per project directory. `set-save` / `set-load` switch named presets for that directory.

## Run from source

```sh
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
bun run build:dsh
dsh plugin --profile web add ./dsh-plugin -w
```

Prefer the npm package. Git installs do not run this repo's build.

## License

[MIT](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
