# pack-agent-dsh — pack-agent 长出的 DeepSeek 插件

在 pack-agent 上长出来的 Cordis 插件：给 DSH 注册投影/检索/允许工具，并以 SkillProvider 只暴露允许集。

## 安装

把**仓库根**加进 DSH 一次（根 `package.json` 声明了 `dsh.bundle`）：

```sh
dsh plugin --profile web add @sakikotgw/pack-agent
dsh web
```

pnpm 9 若报 `ERR_PNPM_ADDING_TO_ROOT`，加 `-w`：`dsh plugin --profile web add @sakikotgw/pack-agent -w`。

不要把 `.agent-pack/modpacks/<某个包>` 再 `plugin add`。换包 = 换允许集。

然后模型能调 `packagent_project` / `packagent_search` / `packagent_allow` / `packagent_deny` / `packagent_list`。
