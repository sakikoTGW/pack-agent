# D84 · 整合包端到端验收（制作 → 安装 → 启停 → 导出 → 导入还原）

隔离方式（遵守 docs/PARALLEL.md）：`E:\tmp\pack-e2e\root`，versions 用 junction 共享，instances/library 独立；
全程 CLI，无窗口、不抢前台。

## 结果

| 步骤 | 结果 | 证据 |
|---|---|---|
| 1 建实例 | ✅ `pad cli instance create e2e 0.2.1-rc.2` | `01-instance-create.txt` |
| 2 制作 | ✅ `packmaker make --spec spec.json --out library/packs` → `e2e-pack-0.1.0.pack.zip`（1.1KB，含 pack.json + 本地插件 + pack-info skill） | `02-packmaker-make.txt` |
| 3 安装/投影 | ✅ `pad cli pack project e2e <zip>` → `.agent-pack/modpacks/pack-agent-modpack-e2e-pack`（catalog/cordis.patch/preset/mods/skills） | `04-pack-project.txt` |
| 4 启停（allow/deny） | ✅ `pack allow/deny e2e pack-agent-modpack-e2e-pack` → Enabled true/false，文件不删 | `05/06` |
| 5 导出 | ✅ `pad cli portable-export <dest> e2e`（200s，608MB/30653 文件；PAD exe + .pack-launcher + 实例 + 版本库） | 体积实测 |
| 6 还原 | ⚠️ **部分成功**：导出的 PAD 能读到自己根下的实例（home/workspace 已重映射），投影目录完整；但**整合包管理在便携包里断链** | `07/08` |

## 找到的断点（按严重度）

1. **便携包里整合包管理不可用**
   `导出后的 PAD cli pack list e2e` → `找不到 pack-agent 源码: 把仓库路径写进 PACK_AGENT_REPO，或在启动器根目录放 .pack-agent-repo`。
   原因：pack 的 list/project/allow 走 TS 侧（bun + 仓库），便携导出只带 exe + launcher，没带仓库；换机器就废。
   这是"生态"级问题：导出的整合包体系在别人机器上打不开。

2. **id 两套、错误提示不指路**
   目录 id 是 `e2e-pack` / `e2e-pack-0.1.0`，投影 id 是 `pack-agent-modpack-e2e-pack`；
   `pack allow e2e e2e-pack` 只报 `unknown pack id e2e-pack`，不告诉你去哪拿正确 id（`pack project` 的输出里才有）。

3. **`pack list --json` 的 `installed` 永远是空**
   装完/启用后仍是 `[]`，目录视图看不出"这个包装到哪些实例、是否启用"。

4. 小问题：
   - `packmaker` 对带 BOM 的 spec.json 直接报 `invalid character 'ï'`（Go json 不吃 BOM）；PAD 自己写 spec 不带 BOM，所以线上不受影响，但外部脚本容易踩。
   - 隔离根里 `pack list` 会把整盘（E:\）的包都扫出来（`source: drive: E:\`），列表噪声大。

## 结论
制作/安装/启停/导出（实例迁移）这条链是通的；**"导入还原后继续用整合包"不通**——
要么便携包带运行 pack 所需的最小运行时（bun + TS 产物），要么 PAD 提供原生 pack 读写（直接读 `.agent-pack/modpacks`），不依赖仓库。
