# KINDS

stdlib Kind 规章位于 `src/kinds/*.kind.json`，供 `loadKind`、`checkPackToml`、`explain` 与 `getStdlibInstallTemplate` 使用。

**迁移状态：** install/scan 深层 wiring 仍部分走 legacy `scan-modules` / `install-modules`；Trait 全调度待续。见 [APL_VERIFY.md](./APL_VERIFY.md)。

## stdlib kind ids
- `agent.skill`：可移植的 skill 目录。
- `agent.rule`：可移植的规则目录。
- `agent.mcp`：可移植的 MCP 配置或服务目录。
- `agent.command`：可移植的命令目录。
- `agent.hook`：可移植的 hook 目录。
- `agent.loop`：可移植的循环工作流目录。
