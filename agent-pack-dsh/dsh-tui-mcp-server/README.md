stdio MCP：把 Cursor 接到正在跑的 DSH TUI 的 pad-gateway 上，走官方 `@deepseek-ai/dsh-host-apiproxy`（`session.list` / `session.prompt` / `session.history`）。

## 跑

环境变量二选一：

- `DSH_HOME`：该实例 home（里面有 `pad-gateway.json`）
- `PAD_GATEWAY_AD`：该广告文件的绝对路径

```
bun src/index.ts
```

Cursor：本仓 `.cursor/mcp.json` 里的 `dsh-tui`。启用后本会话才能 `CallDynamicTool`。

工具：`dsh_ping`、`dsh_session_list`、`dsh_session_create`、`dsh_session_prompt`、`dsh_session_history`、`dsh_wait_turn`、`dsh_session_cancel`、`dsh_session_models`、`dsh_credentials_describe`、`dsh_rpc`（白名单）。`credentials.set` 故意不暴露，避免钥匙进对话。

测试：`bun test`（`package.json` scripts.test）。
