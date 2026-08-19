# dsh-modpack — 投影 + 注册表

DeepSeek Harness 是 Forge。这个目录把 Pack 编成投影目录，**不** `dsh plugin add` 进宿主插件栈。

多包并存于 `.agent-pack/modpacks/<id>/`。每个整合包必须带 `mods/<mod-id>/`：skill、MCP、rule、command、hook、Cordis 插件各有 `mod.json` + 正文。SQLite 注册表由 Rust `pack-index` 维护。技能目录只看见工作区活白名单（`packs.enabled`）。命名预设用 `set-save` / `set-load`。

## 实际用法

```sh
# 宿主只装一次 pack-agent 管理器
dsh plugin --profile web add @sakikotgw/pack-agent-dsh
# pnpm 9: add -w if ERR_PNPM_ADDING_TO_ROOT

# 投影 + 建索引（默认不放行）
packagent dsh project path/to/foo.pack.json

# Cursor 导出的老包 → DSH 整合包（带 mods/）
packagent dsh map path/to/from-cursor.pack.json
# 只有 ref、正文还在 Cursor 项目里：
packagent dsh map path/to/from-cursor.pack.json --from E:\that-cursor-project
# 会收 .cursor/mcp.json、rules、commands、hooks.json，编进 mods/

# 检索（所有已投影包）
packagent dsh search ALPHATOKENSIG

# 工作区活白名单放行 / 停用（文件不删，不改已保存预设）
packagent dsh allow pack-agent-modpack-foo
packagent dsh deny pack-agent-modpack-foo
packagent dsh list
packagent dsh snapshot

# 命名整合白：保存当前活名单，或覆盖加载
packagent dsh set-save alpha-only
packagent dsh set-load alpha-only
packagent dsh set-list
```

只编译目录、不入索引：`packagent dsh compile` / `packagent dsh ours`。

检索实现：`agent-pack-dsh/pack-index`（SQLite + 倒排词表）。第一次调用会在本机 `cargo build --release`，产物写到 `E:\tmp\pack-agent\pack-index-target`。

## 规范化

映射表：`registry.yaml`。skill / mcp / hooks / persona / commands / preset / subagents / memory / settings / experiences 都有固定 emit。DSH 放开的任意插件走 `pack.dsh.plugins[]` 写进投影里的 `cordis.patch.yml`，供索引记录，不自动 insert 进宿主。

## 不做

不重做 DSH 的 loop / preset 宿主。不把抄 `.dsh/skills` 当成这条产品路径。不把每个整合包 `dsh plugin add` 焊进 Forge。
