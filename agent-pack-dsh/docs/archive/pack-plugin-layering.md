# 整合包 × 插件：两层怎么分账

> **ARCHIVED（2026-11-04）**：已落地/研究完成，仅存档，不再维护。活文档见 [docs/README.md](../README.md)。


date: 2026-11-04
status: **已落地 P1 + P2 + P3（2026-11-04，未 commit）**。实现索引见 §9，证据 `EVIDENCE/D105-pack-plugin-layering/`。决策见 [DECISIONS.md](../../DECISIONS.md) D105。
audience: 改 `agent-pack-dsh/modpack/*`（投影/import 链）与 `agent-pack-dsh/pad/*`（实例页/版本页）的人。
依据：本机 PCL 2.13.1.1 源码只用于对照（见 §8.4）；DSH 侧事实取自本机 0.2.1-rc.2 安装（见 §8.3）。

---

## 0. 一句话

**包 = 原子装配清单，插件 = 带归属的依赖。装/卸/更新前先算 diff，永不盲装盲卸；"停用"≠"卸载"。**

今天没有这层账本，所以两个方向都会"静默覆盖/单向残留"：包里带的插件实例里已有 → 再导入直接把版本改掉；导入后卸载 → 插件留在 profile 继续跑。本设计把这两处补上。

---

## 1. 两个轴的真值（先分清，再谈冲突）

| | 整合包 | 插件（组合包） |
|---|---|---|
| 存在哪 | 实例工作区 `.agent-pack/modpacks/<npmName>/`（投影目录）+ `catalog.sqlite` 的 allow 表 | `$DSH_HOME/profiles/<profile>/package.json` 的 dependencies + `dsh.profile.bundles` |
| 谁消费 | `@sakikotgw/pack-agent-dsh` 内容加载器，在会话里按 allow-set 暴露 skill / MCP / rule / command / hook | DSH 启动时按 `dsh.profile.bundles` 顺序加载（boot-time bundle） |
| 怎么装 | `project`（写投影目录 + 入索引）→ `allow`（DB 白名单） | `dsh plugin --profile <p> add <spec>`（转发 pnpm，写 dependencies + bundles） |
| 怎么停 | `deny`（只改 DB 白名单；文件不删） | **DSH 没有停用**：只有 add / remove / update |
| 怎么卸 | 删投影目录（当前 UI 只提供"停用"，真正卸只有删实例） | `dsh plugin remove`（pnpm 移除依赖 + reconcile 摘 bundles） |
| 冲突面 | —— | **唯一重叠：`pack.json` 的 `dsh.plugins[]`（组合包）**；import 链会把它 `dsh plugin add` 进实例 profile |

关键文件：

- 投影：`agent-pack-dsh/modpack/compile.ts`（`compilePackToDshBundle` 写 `mods/<id>/mod.json` + `cordis.patch.yml` + `catalog.json`）、`agent-pack-dsh/modpack/catalog.ts`（`projectPack` / `catalogAllow` / `catalogDeny`）。
- import 链：`agent-pack-dsh/modpack/registries/task-kinds.json` 的 `import` steps = sniff → resolve-version → ensure-version → create-instance → copy-overrides → project-allow → install-manager → add-plugins。实现：`agent-pack-dsh/modpack/import-pack.ts`（`addPlugins()` **无条件**遍历 `pack.dsh.plugins[]` 调 `dsh plugin add`）。
- PAD 侧插件操作：`pad/Core/Launcher.cs` `PluginOp()`（add/remove/update → `dsh plugin --profile`）、`pad/Views/InstanceView.xaml.cs` 插件页三个按钮。
- PAD 侧包操作：`pad/Core/Packs.cs`（`Project` / `Import` / `ImportPack` / `Allow` / `Deny`）；版本页「新建实例」= `Packs.ImportPack`（完整链，D104 起）；实例页「装整合包…」= `Packs.Project`（**只投影**）。

### 1.1 今天两个入口语义不一致（现状事实）

| 入口 | 投影 | 装管理器 | `dsh plugin add` 包的组合包 | 引用 |
|---|---|---|---|---|
| 版本选择页 · 整合包行「新建实例」 | ✅ | ✅ | ✅ | `VersionsView.InstallPackAsync` → `Packs.ImportPack` → TS `launcher import` |
| 实例页 ·「装整合包…」 | ✅ | ❌ | ❌ | `InstanceView.Import_Click` → `Packs.Project`（只投影） |
| 拖包 / 导入整合包按钮 / sidecar | ✅ | ✅ | ✅ | 同上 import 链 |

所以"装到已有实例"今天**只装半**：内容可见，包里的组合包一个都没装。这条已经让"包/插件"两轴无法简单对齐，必须先统一入口（R8）。

---

## 2. 现状行为（逐场景，代码可查）

### 2.1 包里的插件，实例里已经有了

- `add-plugins` 步骤**没有前置检查**：不看 profile 里已有什么、不比较版本、不写任何记录。
- DSH 侧 `dsh plugin add` 的真实行为（`@deepseek-ai/dsh-plugin-manager` 的 `reconcile()`）：
  - bundles 列表**按名字去重**（`if (!bundles.includes(name)) bundles.push(name)`），不会出现两行；
  - 已不在 dependencies 的旧 bundle 会被摘掉；
  - 但 **pnpm 会按这次给的 spec 重新解析**：裸名 → 装 latest；钉版本 → 改成那个版本。
- 结论：**再导入会静默改版本**。如果这个插件是手工装的、另一个包装的、或是你上次自己升过级的，谁都不知道它被改了；包也没有"我装了什么版本"的记录。

### 2.2 导入后再卸

| 卸载动作 | 包（投影/白名单） | 插件（profile） | 结果 |
|---|---|---|---|
| 包「停用」（`deny`） | 内容不可见 | 不动，继续随启动加载 | **停用不彻底**：包停了，插件还在生效 |
| 删包投影（当前无 UI 入口） | 目录没了 | 不动 | 同上，且账本连"它曾要求哪些插件"都没有 |
| 插件页 remove | 不动，仍显示启用 | 卸掉 | **包残缺**：内容少一块、无提示；若被别的包/手工需要，一起坏 |
| 插件页 update | 不动 | 改版本 | 包不知道，可能不兼容 |
| 删实例 | 全删 | 全删（home 删除） | 唯一"卸干净"的路径 |
| 再导入同一包 | 覆盖同目录投影 | 每个 spec 再 add 一遍 | **复活**被 remove 的插件 / 把版本拉回包要的版本 |

### 2.3 其它已知坑

- **管理器插件保护不全**：`plugin-ops.ts` 的 `pluginDisable` 对 `pack-agent` 有 PA110 警告；`pluginRemove` 没有任何守卫。卸掉管理器 → 所有包投影瞬间不可见，无提示。
- **假停用**：`pluginDisable` / `pluginEnable` 只写 `instance.json` 的 `plugins.disabled` + `<home>/pa-disabled.json`，`pluginList` 过滤展示；`Runner` 生成的启动脚本**不读**这份清单。DSH 本身也没有 disable（见 §8.3）。所以插件只有"真装/真卸"。
- **C# 侧会抹掉 TS 写的 instance.json 字段**：`pad/Core/Models.cs` 的 `Instance` 类没有 `packs` / `plugins` / `credentials` / `profile` 字段，也没有 `[JsonExtensionData]`；任何 C# `SaveInstance()`（改名/改钉/启动状态）都会把这些 TS 字段丢掉。→ **账本不要放 `instance.json`**（见 §4.2）。
- **同一包的两个版本**：投影目录名由包名 slug 决定（`npmNameForPack(name)`），同包不同版本会**互相覆盖**，账本不能只按名字记版本。
- **包内本地插件**（`dsh.plugins[].local = "plugins/<dir>"`，import 时 `file:` 装）：归属路径依赖 import 的临时解压目录派生的 pnpm 安装；两个包带同名本地插件时的冲突未实测。

---

## 3. 目标模型

### 3.1 七条规则

| # | 规则 | 说明 |
|---|---|---|
| R1 | **单一真相** | 包内容真相 = 投影目录 + allow 表；插件真相 = profile dependencies + bundles。账本只记"关系/归属"，不复制版本号与 enabled 状态（读时以真相为准，账本只做对账提示）。 |
| R2 | **diff-first** | 任何「装包 / 再装包 / 卸包 / 停用包 / 插件 add / remove / update」先算 diff 并展示，再执行。禁止无提示的 add/remove。 |
| R3 | **包 = 原子装配清单** | 装 = 应用清单（投影 + manager + 按 diff 装插件）；再装 = 按 diff 修补（不是再盲 add）；停用 = 内容不可见 **且** 该包要求的插件移出 `dsh.profile.bundles`（保留 dependencies，可逆、不重下载）；卸载 = 删投影 + 按账本回滚"该包独占"的插件。 |
| R4 | **共享插件引用计数** | 一个插件被 ≥2 个已装包要求时：停用/卸载其中任一个都不动它；UI 注明"还被包 X / Y 需要"。账本维护反向索引 `plugin → [packIds]`。 |
| R5 | **手工插件不受包管** | 账本里只登记"由 import 添加"的插件（`origin: pack`）；用户手工 add 的（`origin: manual`）在卸包时一律不动。被包声明过、后来手工 remove 的，包标 `incomplete` 并给"按包修复"。 |
| R6 | **不静默升降级** | 已存在不同版本 → 冲突，让用户选：升到包版本 / 保留现有（包标 `incomplete`）/ 取消。包钉死版本时，升级/降级都算冲突，不能默认覆盖。 |
| R7 | **管理器保护** | 停用/移除 `@sakikotgw/pack-agent-dsh` 都要 PA110 拦一次（"停了所有整合包都不可见"），remove 路径现在缺这个守卫。 |
| R8 | **入口统一** | 实例页「装整合包…」必须与版本页「新建实例」同一条链（投影 + manager + 按 diff 装插件），唯一区别是要不要先建实例。 |
| R9 | **不可逆动作要写清"删什么"** | 删实例 = 投影 + 插件 + home 全删（已有二次确认）；删包投影 ≠ 停用，两个动作文案分开。 |

### 3.2 账本草案（独立文件，不放 `instance.json`）

`<launcher-root>/instances/<id>/pack-ledger.json`

```jsonc
{
  "schema": "pack-agent.pack-ledger/v1",
  "updated": "2026-11-04T00:00:00.000Z",
  "packs": {
    "Test-0.1.0": {                     // key = catalog row id（文件 stem，与 source.packId 同源）
      "title": "Test",
      "installedVersion": "0.1.0",      // 同一 pack id 只保留一个版本（Q3）；版本变化走 diff，不并排
      "path": "library/packs/Test-0.1.0.pack.zip",
      "installedAt": "2026-11-03T14:20:31.983Z",
      "content": { "dir": ".agent-pack/modpacks/pack-agent-modpack-test", "allow": true }, // 实例级
      "profiles": {                     // 插件部分按 profile 记（Q2）
        "web": {
          "state": "installed",         // 每个 profile 各算各的：installed | disabled | incomplete
          "plugins": [
            // spec 是包的声明；addedVersion 只记"当初我加的版本"，对账时以 profile 为准
            { "spec": "dsh-find-skill", "addedVersion": "0.2.1", "origin": "pack" },
            // 本地插件（Q6）：先复制到实例目录再 add，路径稳定、卸包可回滚
            {
              "spec": "file:E:/.../instances/x/home/local-plugins/Test-0.1.0/my-local-plugin",
              "origin": "pack",
              "localDir": "home/local-plugins/Test-0.1.0/my-local-plugin"
            }
          ]
        }
      }
    }
  },
  "owners": {                           // 反向索引：profile → 插件 → 要求它的包（Q2）
    "web": { "dsh-find-skill": ["Test-0.1.0"] }
  }
}
```

**为什么独立文件**：`Instance` C# 模型不认识 `packs` / `plugins` 等字段（§2.3），放 instance.json 会被下一次 C# 保存抹掉。独立文件由 PAD 与 TS 两端都当"只读/独立写"处理，互不覆盖。

**账本不是真相**：读的时候永远以（a）投影目录/allow 表、（b）profile `package.json` + `dsh.profile.bundles` 为准；账本只回答"这个插件是谁要的""这个包曾要求什么版本"。对账失败（手工改了文件）→ 标 `incomplete` 并提示"重新同步"，不自动改用户文件。

### 3.3 动作矩阵（实现按这张表写）

| 用户动作 | 内容投影 | manager | 该包要求的插件 | 账本 |
|---|---|---|---|---|
| 新建实例并装包（版本页） | 写入 | 装（没有才装） | diff：缺的 add；已存在同版本跳过；版本不同 → 冲突选择 | 新建条目 |
| 装包到已有实例（实例页，R8 统一后） | 写入 | 装（没有才装） | 同上 | 新建/更新条目 |
| 再次安装同一包 | 覆盖同目录 | 已装跳过 | 只补差异；被 remove 的按"修复"重装；版本不同 → 冲突 | 更新条目 |
| 停用包 | 保留，allow=false | 不动 | 从 `dsh.profile.bundles` 摘掉（dependencies 保留） | state=disabled |
| 重新启用包 | allow=true | 不动 | 加回 bundles（不重新下载） | state=installed |
| 卸载包 | 删投影目录 | 不动 | 只 remove `owners[plugin].length == 1` 的；共享的保留并提示 | 删条目 + 更新 owners |
| 插件 remove（插件页） | 不动 | 不动 | 若被启用包要求 → 二次确认 + 包 state=incomplete，给「按包修复」 | owners 更新 |
| 插件 update | 不动 | 不动 | 若被包钉版本 → 二次确认，可能 state=incomplete | 记 addedVersion 不追新 |
| 删实例 | 全删 | 全删 | 全删 | 删文件 |

> 动作按 **profile 粒度**执行（Q2）：投影 / allow 是实例级，插件行与 `owners` 都带 profile；同 pack id 的版本变化按"升级/降级"走 diff（Q3），不并排。

### 3.4 状态机（包在实例里的四种状态）

> 状态按 **(pack, profile)** 计（Q2）：投影/allow 是实例级，`installed|disabled|incomplete` 是 profile 级；多 profile 时包详情里逐 profile 列状态，包行只取"至少一个 profile 非未装"。

```
未安装 --装--> installed --停用--> disabled --启用--> installed
                   |  ^                                  |
          插件被卸/版本被改                            卸载（删投影+回滚独占插件）
                   v  |                                  v
               incomplete --按包修复--> installed      未安装
```

- `installed`：投影在、allow=true、包要求的插件都在且版本与包声明一致（或用户已确认过偏差）。
- `disabled`：投影在、allow=false、包要求的插件已从 bundles 摘掉（dependencies 还在）。
- `incomplete`：投影在、但插件缺失/版本偏差/被手工改过；**不自动修**，给明确按钮。
- 未安装：无账本条目或无投影。

---

## 4. 与 PCL 的对照（为什么 PCL 没这个问题）

- PCL「装整合包」= 在当前 `.minecraft` 下新建一个版本目录：读 manifest → 装 MC 版本/加载器 → 解压 overrides → 按清单下 mod（hash 校验）→ 完成。卸载 = **删整个版本目录**。
- 它**没有"往已有版本里再装一个包"这一层**，也没有"包"和"插件"两个独立可装卸的轴，因此不存在两套账本。
- 可借鉴的一条：**"装"的粒度是实例整体；"停用"不等于"卸载"**。PCL 的兼容性也只在安装期拦硬条件（缺 MC 版本/加载器不支持），mod 之间的冲突留给运行期。

PAD 因为 DSH 有「投影/白名单」+「profile 插件栈」两层，必然多出这一轴。结论不是砍掉哪一层，而是**把关系记下来，把动作变成 diff**。

---

## 5. 非目标（这轮明确不做）

1. **不把插件也变成投影**（需要 DSH 支持运行期加载插件；当前插件是 boot-time bundle）。这是终局方案，等 DSH 能力。
2. **不自动修复历史半成品实例**（例如旧 `pack-Test`）：只在 UI 标"上次没装完 / 不完整"，修复要人点。
3. **不做多包原子事务**：两个包同时装只做顺序应用，失败保留现有 `import-failed` 语义。
4. **不接管 pnpm lockfile / 不做依赖解析器**：版本选择仍交给 pnpm，PAD 只做冲突提示与允许通道。
5. **不自动重装用户手工改过的插件版本**。
6. **不改 DSH 插件栈**；不给 DSH 打补丁。

---

## 6. 分期落地

> 三段均已于 2026-11-04 落地（未 commit）：实现索引/证据见 §9；每段的最终行为以 §9 的代码与 `EVIDENCE/D105-pack-plugin-layering/` 为准。

### P1 · 提示层（不改数据，先消除误导） — ✅ 已落地

- 实例页插件行标注「来自整合包 X」（从已装包的 `dsh.plugins[]` 或投影 `catalog.json` 的 plugin mod 推断）。
- 实例页包行标注「这个包带 N 个插件，其中 M 个此刻不在实例里」。
- 插件页 remove/update 一个被已装包声明的插件：二次确认 + 文案说明后果（包会残缺），并给「稍后可修复」提示。
- 管理器 remove 前也走 PA110（与 disable 对齐）。
- 包行装态文案过渡（Q5）：现有 `installed`（来源）判定的药丸文案改成「已建实例：X」；P2 换成账本判定的「已装到 X」。
- 验收：不写任何数据；上述四个场景均出现明确提示；`pad-window.test.ts` 增加源码断言。

### P2 · 账本 + 停用联动（核心） — ✅ 已落地

- 新增 `pack-ledger.json`（§3.2），import 链与实例页装包时写入；读时对账 profile。
- **Q1 门槛**：先在 scratch 实例复测"摘 bundles 留 deps"不被 reconcile 加回（脚本见 §7），复测通过才动 `WriteBundles` 联动。
- 停用/启用包：联动 `dsh.profile.bundles`（复用 `Trial.cs` 的 `WriteBundles` / `PatchArgs` 机制：摘 bundle 行、留 dependencies）。
- 卸载包（新增入口）：删投影 + 按 owners 回滚独占插件 + 清理本包 `home/local-plugins/<packId>/`（Q6）。
- 本地插件（Q6）：import 时先复制到 `<instance>/home/local-plugins/<packId>/<name>/` 再以 `file:` 装；账本记 `localDir`。
- pinst（Q4）：导出清单加入账本文件；导入原样恢复，不重算；旧 pinst 空账本 + 「未登记」标记 + 只读扫描重建。
- 「按包修复」按钮：按账本重新 apply 缺失插件。
- 两个入口统一（R8）。
- 验收：场景 A/B 全部按 §3.3 矩阵行为；停用后 `dsh --dump-config --profile <p>` 不再出现该插件的 bundle；重新启用不触发下载；共享插件卸载提示正确；pinst 导出→导入后账本一致。

### P3 · 不兼容版本的允许通道 + 冲突体验 — ✅ 已落地

- 包安装时若 DSH 报 `incompatible`：展示具体插件与要求范围，给两个动作：
  1. 「接受风险安装」→ `dsh plugin --profile <p> allow-version <pkg@ver> --dsh-version <exact> --accept-risk`（DSH 官方通道，见 §8.3）；
  2. 「换内核重试 / 重新导出包」。
- 同版本冲突选择 UI（升到包版本 / 保留现有 / 取消）。
- 验收：Test-0.1.0 这类包不再"只有死路"；冲突选择在三类场景（手工/多包/再导入）都出现。

---

## 7. 已拍板（2026-11-04）

原来 §7 的 6 条待定问题已经逐条定案；P2 实装前只需要做 Q1 的 scratch 复测，不再改方向。

| # | 问题 | 结论 | 依据 / 备注 |
|---|---|---|---|
| Q1 | 停用包"摘 bundles 留 deps"会不会被 DSH 反过来加回去？ | **可以这么干，会保持。** P2 实装前在 scratch 实例（DSH 0.2.1-rc.2）复测一次作为门槛。 | `reconcile()` 只对**本次新增依赖**补 bundle（`if (beforeDeps.has(name)) continue;`），已有依赖不会被补回；boot 只按 `dsh.profile.bundles` 顺序 `compose`，不在列表里的依赖不会加载。风险：DSH 升级可能改 reconcile 语义 → 记入升级回归清单。 |
| Q2 | 账本按 instance 还是 instance × profile？ | **混合：投影按实例记，插件按 profile 记。** 账本文件每个实例一份，条目里 `profiles: { <profile>: { plugins: [...] } }`；owners 也按 profile 分。 | 投影目录在 workspace（实例级、profile 共享）；`dsh plugin` 与 plugin list 都带 `--profile`。把插件记成实例级，多 profile 时必然对不上。 |
| Q3 | 同一包两个版本能否并存？ | **不能并存：同 pack id = 同一个包，版本变化走"升级/降级"diff，不静默。** 账本记 `installedVersion`。 | 投影目录按包名 slug（`npmNameForPack`）、allow 表按 pack id、UI 一行一个包；并排两版本要同时改投影目录 id 与 allow 键，收益低、认知成本高。将来要并存时再改投影目录为 `<name>@<version>`。 |
| Q4 | `pinst.zip` 和账本什么关系？ | **pinst 是整实例快照：账本随实例走，导出带上、导入原样恢复，不重算、不"认领"包。** 旧 pinst 没有账本 → 空账本 + 实例页标「未登记」，只提供只读"扫描重建"。 | pinst 语义是"把整个实例搬走"，不是"装包"；重算账本会让恢复出来的实例和导出时不一致。P2 实装时确认 TS `exportInstance` 打包清单包含 `<instance>/pack-ledger.json`（不含则扩清单，不动格式）。 |
| Q5 | D104 的 `installed`（`source.packId`）和账本装态怎么分？ | **两个词，两个字段**：`source.packId` = **来源包**（这个实例由哪个包建出来，只做来源显示）；账本 = **已装包**（装进了这个实例，可多个）。包行绿药丸改由账本算；P1 过渡期沿用现有判定但文案改「已建实例：X」。`import-failed` 不算已装。 | D104 现在用 `installed`（来源）当装态，是因为当时没有账本；两者语义不同，混用会在"从包建的实例里又装了别的包"时出错。 |
| Q6 | 本地插件（`file:`）的归属与路径稳定性 | **先复制、再 add、可回滚**：import 时把包里的本地插件目录复制到 `<instance>/home/local-plugins/<packId>/<name>/`，`dsh plugin add file:<该绝对路径>`；账本记 `origin: pack, localDir`；卸包时删目录。 | 现在 `file:` 指向 import 的**临时解压目录**，临时目录在导入结束即删，路径天然脆弱；复制进实例后路径稳定、可归属、可回滚。两个包带同名本地插件 → 按普通依赖名冲突处理（保留一个 / 换名 / 取消）。 |

**Q1 的复测脚本（P2 门槛，写进 EVIDENCE）**：scratch 实例 → 装一个带 `dsh.bundle` 的插件 → 手工从 `dsh.profile.bundles` 摘掉该行（保留 dependencies）→ 跑一次无关的 `dsh plugin add`/`remove` → 断言该行没被自动加回；再跑 `dsh --dump-config --profile <p>` 断言该 bundle 层没出现；最后手工加回，断言 `--dump-config` 恢复。

---

## 8. 依据索引

### 8.1 PAD / pack-agent 代码

- `agent-pack-dsh/modpack/import-pack.ts`：`addPlugins()`（无条件 add）、`createInst()`（写 `source`）。
- `agent-pack-dsh/modpack/registries/task-kinds.json`：`import` 步骤表、`install-manager` 的 spec。
- `agent-pack-dsh/modpack/catalog.ts`：`projectPack` / `catalogAllow` / `catalogDeny`（deny 只写 DB）。
- `agent-pack-dsh/modpack/plugin-ops.ts`：`pluginAddToInstance`（PA002 / PA101 / PA105 / PA021）、`pluginRemove` / `pluginUpdate`、`pluginDisable` / `pluginEnable`（只写 `pa-disabled.json`）、`pluginList`（展示层过滤）。
- `agent-pack-dsh/pad/Core/Launcher.cs`：`PluginOp()`（真插件操作）、`Packs` 相关调用。
- `agent-pack-dsh/pad/Core/Packs.cs`：`Project` / `Import` / `ImportPack` / `Allow` / `Deny`。
- `agent-pack-dsh/pad/Views/InstanceView.xaml` / `.xaml.cs`：整合包页（启用/停用、装整合包…）与插件页。
- `agent-pack-dsh/pad/Views/VersionsView.xaml.cs`：`InstallPackAsync`（新建实例走完整链，D104）。
- `agent-pack-dsh/pad/Core/Trial.cs`：`WriteBundles` / `PatchArgs`（停用联动的现成机制）。
- `agent-pack-dsh/pad/Core/Models.cs`：`Instance` 缺 `packs`/`plugins`/`credentials`/`profile` 字段（账本必须独立文件，§2.3）。
- `agent-pack-dsh/pad/Core/Runner.cs`：生成启动脚本时不消费 `plugins.disabled`（假停用）。

### 8.2 已落地的相关决策

- D13：整合包投影走 bundle 层，不进 plugin 栈。
- D39 / D41：版本选择页只留 整合包目录 / DSH 版本 / 容器；容器去收编。
- D81：插件归插件，整合包归整合包（术语）。
- **D104**：容器绿灯只认 PAD 账本；整合包「新建实例」走完整 import 链；装态药丸；兼容门如实报。
- **D105**：本设计（包=原子装配清单、账本、diff 先行、停用≠卸载；设计先行，未落码）。

### 8.3 DSH 0.2.1-rc.2 事实（本机实测）

- `dsh plugin --help`：只把剩余参数转发给 profile 目录里的 pnpm（add / remove / why），**没有 disable**。
- `node_modules/@deepseek-ai/dsh/lib/plugin-*.js`：`runPlugin` 把命令交给 `@deepseek-ai/dsh-plugin-manager/operations`；不兼容包会列 `incompatible` 并提示 `dsh plugin --profile <p> allow-version <pkg@version> --dsh-version <exact> --accept-risk`。
- `@deepseek-ai/dsh-plugin-manager` 的 `reconcile()`：bundles 按名字去重；不在 dependencies 的 bundle 会被摘掉；**只有本次新增的依赖**才会被补进 bundles（`if (beforeDeps.has(name)) continue`），已有依赖不会因为还留在 dependencies 就被加回。boot 只按 `dsh.profile.bundles` 顺序 compose。→ ①重装 add 不产生重复行，但 spec 会被 pnpm 重新解析；②"摘 bundles 留 deps"的停用方式可以保持（Q1 的依据）。

### 8.4 PCL 2.13.1.1 对照

- `Modules/Minecraft/ModModpack.vb`：整合包类型识别（CurseForge/HMCL/MMC/MCBBS/Modrinth/带启动器）、manifest 取 MC 版本+加载器、装加载器、解压 overrides、缺件弹窗继续、不兼容加载器可跳过。
- `Modules/Minecraft/ModWatcher.vb` + `FormMain.xaml(.vb)`：运行态只认 PCL 自己启动的 watcher；`HasRunningMinecraft` 驱动顶栏「关闭 Minecraft」。
- `Modules/Minecraft/ModMinecraft.vb` `McInstance.ToListItem`：版本行只有名字 + Info（版本/加载器/错误），没有运行灯。

---

## 9. 落地实现索引（2026-11-04）

### 9.1 代码地图

| 设计点 | 落地位置 |
|---|---|
| P1 包↔插件只读事实（投影 catalog → 插件归属、profile 实装/缺失） | 新 `pad/Core/PackPluginFacts.cs`；实例页包行 `PluginSummaryText`、插件行 `PackOriginText`（`Views/InstanceView.xaml(.cs)`、`Core/PluginList.cs`）；remove/update 确认 + 管理器 PA110 |
| P2 账本（schema/原子写/owners 引用计数/读时对账） | 新 `pad/Core/PackLedger.cs`（独立 `instances/<id>/pack-ledger.json`，不进 instance.json）；`EffectiveState/GapsFor/MissingFor/VersionConflicts` 都以 profile 真相为准，账本只记关系 |
| P2 装/再装/停用/卸载/修复 API | `pad/Core/Packs.cs`：`Import`/`ImportPack` 成功写账本；`ApplyPackToInstance`（diff：新增/保留/冲突不静默）、`SetPackPluginsActive`（内容 allow/deny + 摘/加 `dsh.profile.bundles`，deps 保留不重下载）、`UninstallPack`（owners==1 回滚 + 删投影/本地插件 + 删账本条目）、`RepairPack`；结果类型 `PackApplyResult/PackToggleResult/PackUninstallResult/PackRepairResult` |
| P2 UI 接线 + R8 入口统一 | `Views/InstanceView.xaml(.cs)`：包行账本药丸（不完整/投影缺失/未登记）、修复/卸载按钮、「装整合包…」改走 `ApplyPackToInstance`；`Views/VersionsView.xaml.cs` 装态三段（账本「已装到」→ source「已建实例」→ 未安装/上次没装完） |
| P2 Q4 pinst | TS `modpack/pinst.ts`：导出把 `pack-ledger.json` 原样放进 zip 根、导入原样恢复；旧 pinst 无账本保持「未登记」，不补空账本 |
| P2 Q6 本地插件 | TS `modpack/import-pack.ts`：先复制到 `<instance>/home/local-plugins/<packId>/<name>/` 再 `file:` 装；`import-pack.test.ts` 断言 |
| P3 allow-version 正门 | 新 `pad/Core/PackCompat.cs`（解析 DSH 提示行/兜底 warning）；`pad/Core/Launcher.cs`：`AllowVersionArgs`/`AllowVersion`/`MarkInstanceStatus`（JsonNode 只改 status，保 TS 字段） |
| P3 三选一 UI | `Views/InstanceView.xaml.cs`（接受风险→allow-version→续装 / 换内核→Pin→续装 / 取消 + 冲突升/留(incomplete)/取消，单次重试上限）；`Views/VersionsView.xaml.cs` 对新建流程的遗留 `import-failed` 实例续装（不重复建实例） |
| M2 发现的最小修复 | 实例页过滤 catalog DB 残留行（投影目录没了且无账本 → 隐藏；老实例投影在仍显示「未登记」）；TS 侧 catalog prune 列为 follow-up |

### 9.2 测试与证据

| 门 | 结果 | 证据 |
|---|---|---|
| Q1（停用机制 DSH 复测） | PASS 15/15 | `EVIDENCE/D105-pack-plugin-layering/q1/`（含 `run-q1.ps1` 一键复跑、3 份 dump-config） |
| P1 M1（独立 verifier） | PASS | `.../m1/SUMMARY.txt`（build 0/0、5 bun、截图、14/14 源码断言） |
| P2 M2（独立 verifier） | PASS | `.../m2/SUMMARY.txt`（build 0/0、dotnet 103/103、ledger fields、停用=摘 bundle 留 dep+no re-download、共享插件卸载保留、Apply+版本页账本药丸、手工移除→incomplete→修复 105.5s） |
| P2 API 真 DSH E2E | 28/28 PASS | `.../p2-api/`（含修掉 allow/deny 用错 id 的 bug；幂等 diff） |
| P3 M3（独立 verifier） | 待回填（verifier 跑） | `.../m3/` |
| 单测 | `dotnet test` **114/114**（+47：PackLedger 26 / PackCompat 11 / 其余回归）；6 个 bun 套件全绿 | `dotnet test pad.tests`、`bun run modpack/{import-pack,test,pack-catalog,pad-window,pad-pack,pinst}.test.ts` |
| 构建 | `dotnet build pad -c Debug` 0 警告 0 错误 | 同上 |

### 9.3 边界与后续

- 设计边界仍按 §5：不做全投影（等 DSH 运行期插件加载）、不自动修复历史半成品、不做多包原子事务、不接管 pnpm lockfile。
- 已记录的 follow-up：① catalog DB 残留行的 TS 侧 prune（当前 UI 最小过滤）；② `UninstallPack` 逐插件 `dsh plugin remove`（N 次 pnpm，实测 100s+，可批量化）；③ Q1 依赖 `reconcile()` 语义，DSH 升级需回归；④ 可选插件每次 Apply 重试有日志噪音（P3 的 allow-version 已给出正门，后续可记豁免跳过）。
