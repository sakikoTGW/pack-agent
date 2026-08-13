# 安装 pack-agent DSH 插件

宿主只装这一份管理器：

```sh
dsh plugin --profile web add @sakikotgw/pack-agent
dsh web
```

pnpm 9 若报 `ERR_PNPM_ADDING_TO_ROOT`：

```sh
dsh plugin --profile web add @sakikotgw/pack-agent -w
```

根包 `@sakikotgw/pack-agent` 的 `package.json` 含 `dsh.bundle.patch` → `dsh-plugin/cordis.root.yml`，会插入 `@sakikotgw/pack-agent/dsh`。

整合包走：

```sh
packagent dsh project path/to/foo.pack.json
packagent dsh allow pack-agent-modpack-foo
```

不要 `dsh plugin add` `.agent-pack/modpacks/` 里的某个包。
