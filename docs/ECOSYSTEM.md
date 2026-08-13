# 生态位评估：pack-agent ↔ Claude Plugin / ccpkg / skillpack

updated: 2026-07-30  
北极星见 [NORTH_STAR.md](./NORTH_STAR.md)：跨 harness 迁移，不做单家优先。

## 各自解决什么

| 项目 | 解决什么 | 边界 |
|------|----------|------|
| **pack-agent** | 角色（agents.yaml）→ 便携 zip → 多 harness 投射 + ledger 可卸 | 安装器在 npm；pack 本身靠文件/URL |
| **Claude Plugin + Marketplace** | Claude Code 官方发现/安装；skills/commands/hooks/MCP | 绑 Claude 一家 |
| **ccpkg** | 跨工具归档规范（skills/agents/commands/hooks/MCP/LSP） | 规范草案；与 agents.yaml/eject 正交 |
| **skillpack** | skill 依赖锁、hash、签名、`.skl` | 偏 skill 生命周期，少 MCP/多 runtime |

## pack-agent 应占的位

- **不替代** Claude Marketplace（那是 Claude 官方分发）
- **不另起炉灶抢 ccpkg 规范主导权**；可做 **import/export 适配**
- **坚持差异化**：`agents.yaml` 角色边界 + 多 harness adapter + install-ledger eject

## 建议互操作（未实现，按优先级）

1. **`packagent export --format ccpkg`**（或 `to-ccpkg`）：把 `.pack.zip` 映射为 `.ccpkg` 布局（manifest + skills + commands + mcp）
2. **`packagent import foo.ccpkg`**：读入后转 PackDoc 再投射
3. **`packagent export --format claude-plugin`**：生成 `.claude-plugin/plugin.json` 目录，便于进 Claude marketplace（单家导出，迁移入口仍是 pack zip）
4. **轻量 index**：GitHub Release + `packs/index.yaml`（name/url/version/hash），`packagent install @owner/pack-name` 解析——商店可后置

## L2 capture

瓶口录制/经验罐头保留为可选深度；主路径仍是 L1 可迁移文件（skills/rules/commands/mcp/collab）。与业界「文件夹 skill」一致，不把 L2 当阻塞项。

## 结论

短期：把 **内容层（commands + dialect kinds）+ Cursor 默认可装 + zip/URL 安装** 做硬。  
中期：ccpkg/Claude plugin **适配层**，不做第二个商店。  
长期：可选 pack index；签名校验可借鉴 skillpack，非 MVP。
