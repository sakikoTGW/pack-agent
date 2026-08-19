# agent-pack for DSH

本仓 DSH 全部在这个文件夹。根目录 `packagent dsh` 转到这里。

```
agent-pack-dsh/
  modpack/      投影、白名单、启动器
  plugin/       实例内管理器 @sakikotgw/pack-agent-dsh
  pack-index/   SQLite 检索
  cordis.ts     通用 install 用的 overlay
  scripts/      编插件 / 编检索 / stage npm
  tests/
  docs/         产品设计（先读 docs/PRODUCT.md）
```

人路径：`.pack.zip` → 隔离实例 → `packagent dsh launcher run`。

设计稿：

- 产品：[docs/PRODUCT.md](docs/PRODUCT.md)
- 装包链：[docs/modpack.md](docs/modpack.md)
- 启动器功能：[docs/launcher-design.md](docs/launcher-design.md)
- 引擎：[docs/launcher-architecture.md](docs/launcher-architecture.md)

装管理器（开发）：

```sh
bun run build:dsh
dsh plugin --profile web add ./agent-pack-dsh/plugin -w
```
