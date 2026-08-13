# pack-agent-dsh

[English](README.md) | 中文

`@sakikotgw/pack-agent-dsh` 是 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 插件。

它把 `.pack.json` / `.pack.zip` 投影到 `.agent-pack/modpacks/`，按工作区白名单暴露 skill。宿主只装这一份插件。不要把每个投影目录再 `dsh plugin add`。

Claude Code、Codex、Cursor 用 [`@sakikotgw/pack-agent`](https://www.npmjs.com/package/@sakikotgw/pack-agent)。

## 安装

安装 [Node.js](https://nodejs.org/)，然后：

```sh
dsh plugin --profile web add @sakikotgw/pack-agent-dsh
```

pnpm 9 若报 `ERR_PNPM_ADDING_TO_ROOT`，加 `-w`。

插件仓库可添加 [`dsh-plugin`](https://github.com/topics/dsh-plugin) 话题，便于被发现。

## 使用

```sh
packagent dsh project path/to/foo.pack.json
packagent dsh allow <id>
```

`packagent` 来自 `@sakikotgw/pack-agent`。Cursor / 老包用 `packagent dsh map`。

DSH 里注册的工具：`packagent_project`、`packagent_map`、`packagent_search`、`packagent_allow`、`packagent_deny`、`packagent_list`、`packagent_set_save`、`packagent_set_load`、`packagent_set_list`。

白名单按项目目录一份。`set-save` / `set-load` 切换该目录下的命名预设。

## 从源码运行

```sh
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
bun run build:dsh
dsh plugin --profile web add ./dsh-plugin -w
```

对外安装用 npm 包。从 git 装不会跑本仓库的构建。

## 许可证

[MIT](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
