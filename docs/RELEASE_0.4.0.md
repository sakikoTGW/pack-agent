# 0.4.0 就绪说明

## 北极星

跨 harness **迁移** agent 能力（见 [NORTH_STAR.md](./NORTH_STAR.md)）。  
CBL / Cos Collab 只是样例包，不是产品优先级。

## 本版已落地

Phase A

- 模块 `commands`（默认开）：`.cursor/commands` + `.claude/commands`
- `cos.collab` 方言 Kind 样例：`.collab/**` 布局 + portability 排除 FOUNDATION / environment-traps
- Cursor 移出 `PACK_APPLY_SKIP`，默认参与 install
- 主产物 `.pack.zip`；CLI 0.4.0

Phase B

- `packagent install <https://…>` 远程包
- Windows 推荐：`npm exec --yes --package=@sakikotgw/pack-agent -- packagent install …`
- 文档以 zip / 迁移为主路径

Phase C

- 生态位评估：[ECOSYSTEM.md](./ECOSYSTEM.md)（ccpkg / Claude plugin 互操作建议，不做第二商店）

DeepSeek Harness

- 整合包投影到 `.agent-pack/modpacks/<id>/`，目录必须带 `mods/`（skill / MCP / rule / command / hook / plugin + `mod.json`）
- `packagent dsh map` 把 Cursor / 老 `ccui-pack` 映到 DSH 包；`--from` 会收 MCP、rule、command、hook，不只 skill
- SQLite 注册表 + Rust `pack-index` 检索；`allow` / `deny` 是会话放行，不删文件
- 宿主只 `dsh plugin --profile web add @sakikotgw/pack-agent` 一次；禁止把每个整合包 `dsh plugin add` 焊进 Forge

## 发布 npm（需登录）

本机 `npm whoami` 未授权时跳过。发布者执行：

```bash
npm login
npm publish --access public
```

包名：`@sakikotgw/pack-agent@0.4.0`
