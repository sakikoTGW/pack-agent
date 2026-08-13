# pack-agent-dsh

English | [中文](README.zh.md)

`@sakikotgw/pack-agent-dsh` is a [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) plugin. It projects `.pack.json` / `.pack.zip` into `.agent-pack/modpacks/` and exposes skills from the project allow-list.

## Install

npm: [`@sakikotgw/pack-agent-dsh`](https://www.npmjs.com/package/@sakikotgw/pack-agent-dsh)

```sh
dsh plugin --profile web add @sakikotgw/pack-agent-dsh
```

pnpm 9: add `-w` on `ERR_PNPM_ADDING_TO_ROOT`.

CLI for project / map / allow is a second package: [`@sakikotgw/pack-agent`](https://www.npmjs.com/package/@sakikotgw/pack-agent).

## Use

```sh
packagent dsh project path/to/foo.pack.json
packagent dsh allow <id>
packagent dsh map path/to/from-cursor.pack.json
```

Same operations as DSH tools: `packagent_project`, `packagent_map`, `packagent_search`, `packagent_allow`, `packagent_deny`, `packagent_list`, `packagent_set_save`, `packagent_set_load`, `packagent_set_list`.

Allow-list is one per project directory. Projected packs stay under `.agent-pack/modpacks/`; they are not extra `dsh plugin add` targets.

## Run from source

```sh
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
bun run build:dsh
dsh plugin --profile web add ./dsh-plugin -w
```

## License

[MIT](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
