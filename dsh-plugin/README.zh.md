# pack-agent-dsh

[![awesome · DSH plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

[English](README.md) | 中文

像 MC 整合包一样管理 DeepSeek Harness 配置。`@sakikotgw/pack-agent-dsh` 是 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 插件。把 `.pack.json` / `.pack.zip` 投影到 `.agent-pack/modpacks/`，按项目白名单暴露 skill。

## 安装

npm：[`@sakikotgw/pack-agent-dsh`](https://www.npmjs.com/package/@sakikotgw/pack-agent-dsh)

```sh
dsh plugin --profile web add @sakikotgw/pack-agent-dsh
```

pnpm 9 报 `ERR_PNPM_ADDING_TO_ROOT` 时加 `-w`。

投影 / map / allow 的命令行在另一个包：[`@sakikotgw/pack-agent`](https://www.npmjs.com/package/@sakikotgw/pack-agent)。

## 使用

```sh
packagent dsh project path/to/foo.pack.json
packagent dsh allow <id>
packagent dsh map path/to/from-cursor.pack.json
```

DSH 里对应工具：`packagent_project`、`packagent_map`、`packagent_search`、`packagent_allow`、`packagent_deny`、`packagent_list`、`packagent_set_save`、`packagent_set_load`、`packagent_set_list`。

白名单按项目目录一份。投影结果在 `.agent-pack/modpacks/`，不要对里面的目录再 `dsh plugin add`。

## 从源码运行

```sh
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
bun run build:dsh
dsh plugin --profile web add ./dsh-plugin -w
```

## 许可证

[MIT](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
