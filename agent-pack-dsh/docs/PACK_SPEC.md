# 整合包规范（PACK_SPEC）

status: draft v3 · 2026-11-04 · 以后按这份执行

## 0. 一句话

整合包 = **一份可搬运的运行组成**：`pack.json` 声明「用什么跑」，`overrides/` 原样携带「文件长什么样」。

```
整合包 = manifest（内核 / 配置树 / bundle / agent / 工作区 / 兼容约束 / 完整性）
       + overrides（patch、第三方 bundle、preset patch、数据文件、可选工作区模板）
       + icon.png
```

## 1. 六个可替换轴

| 轴 | DSH 里的东西 | 可替换方式 | 可选 |
|---|---|---|---|
| kernel | `@deepseek-ai/dsh` 的版本 + 它自带的官方 bundle 清单 | inherit / pin / bundled | 是 |
| profile | `profiles/<name>`：`package.json` 的 `bundles` + `cordis.patch.yml` | 换整棵树或只换 patch | 是 |
| **bundle** | profile 的 `dsh.profile.bundles` 条目 | 换列表 + 装对应包 | 随 profile |
| agent | agent preset（`@deepseek-ai/dsh-agent-preset` 的声明行） | 选内置 preset，或带自定义 preset patch | 是 |
| DSH_HOME | 数据根（storages / skills / sessions） | inherit / snapshot（**凭据默认不带**） | 是 |
| workspace | 启动时的 cwd | bundled / copy / user | 是 |

**bundle 必须在 manifest 里单列**：它决定启动方式（`dsh-web-app` → 浏览器，`dsh-tui` → 终端，桌面壳 → exe）。

## 2. 文件结构

```
X.pack.zip
├─ pack.json                         声明（唯一入口）
├─ icon.png                          包图标
├─ kernel/                           kernel.mode = bundled 时才有
│   └─ <version>/                    只带 .pnpm 存储 + package.json；顶层 junction 安装时重建
├─ runtime/
│   └─ node/<version>/               runtime.node = bundled 时才有
└─ overrides/                        原样覆盖，不参与版本解析
   ├─ profiles/<name>/cordis.patch.yml
   ├─ profiles/<name>/node_modules/.pnpm/…  source: bundled 的第三方包体（只带 pnpm 存储，顶层链接安装时重建）
   ├─ presets/<id>.patch.yml         source: bundled 的自定义 agent preset
   ├─ home/…                         home.mode = snapshot 时的数据快照
   ├─ .agent-pack/…                  插件要读的数据
   └─ workspace/…                    workspace.mode = copy/bundled 时的初始模板
```

安装语义两条：

1. `pack.json` 的声明项走**解析安装**（内核、bundle、patch、兼容校验）；
2. `overrides/` 走**原样覆盖**，逐文件记账（`integrity.files`），卸载/升级据此撤回。

## 3. pack.json

机器可读 schema：[`schemas/pack-agent.pack.v3.schema.json`](schemas/pack-agent.pack.v3.schema.json)（JSON Schema 2020-12，`additionalProperties: false`，kernel/runtime/home/workspace/agent 用 `oneOf` + 条件必填）。填满所有字段的示例：[`schemas/example.pack.json`](schemas/example.pack.json)。

```jsonc
{
  "schema": "pack-agent.pack/v3",
  "name": "Test", "version": "0.1.0",
  "description": "…", "icon": "icon.png",
  "meta": { "author", "homepage", "license", "exportedAt", "portable", "fidelity", "source", "tags" },

  "kernel": {
    "mode": "bundled",                 // inherit | pin | bundled
    "version": "0.2.1-rc.2",
    "bundled": "kernel/", "sha256": "…", "files": 30636,
    "bundles": ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "…"]   // 这个内核自带的官方 bundle
  },
  "runtime": { "node": "inherit" },    // inherit | bundled
  "home": {
    "mode": "snapshot",                // inherit | snapshot
    "snapshot": "overrides/home/",
    "include": ["storages", "skills"], // 带哪些子目录
    "credentials": false               // 默认 false：不把 API Key 打进包
  },
  "workspace": { "mode": "copy", "template": "overrides/workspace/" },

  "profiles": [{
    "name": "web",
    "bundles": [
      { "spec": "@deepseek-ai/dsh-web-app", "source": "official", "required": true },
      { "spec": "@deepseek-harness-tui/dsh-tui@0.18.0", "source": "bundled", "required": false }
    ],
    "patch": "overrides/profiles/web/cordis.patch.yml",
    "agents": ["standard", "ptc"]
  }],

  "agents": [
    { "id": "standard", "source": "official", "profiles": ["web"] },
    { "id": "reviewer", "source": "bundled", "patch": "overrides/presets/reviewer.patch.yml" }
  ],

  "data": [{ "path": "overrides/.agent-pack/", "scope": "workspace" }],
  "requires": { "dsh": ">=0.2.1-rc.2 <0.3.0", "node": ">=22.19.0", "os": ["win32"], "arch": ["x64"] },

  "integrity": {
    "algorithm": "sha256",
    "root": "…",                       // 见 §6
    "files": [{ "path": "…", "sha256": "…", "bytes": 812 }]   // overrides 账本
  }
}
```

## 4. kernel 与官方 bundle

| 模式 | 说明 | 包体积 | 适用 |
|---|---|---|---|
| `inherit` | 用目标机已装的版本 | 最小 | 对方已有同版本 |
| `pin` | 钉版本，安装时下载/复用本机版本库 | 小 | 常规分发 |
| `bundled` | 内核随包 | 极大（0.2.1-rc.2 ≈ **486 MB / 30,636 文件**） | 离线/便携 |

`bundles[].source` 有三种：

- `official`：内核自带（必须在 `kernel.bundles` 里），**不打包**；
- `registry`：安装时从 npm 装（需要网络，旧包映射落这里）；
- `bundled`：随包带，放进 `overrides/profiles/<name>/node_modules/`（离线可用）。
`kernel.bundles` 是**这个内核版本自带的官方 bundle 清单**（`@deepseek-ai/dsh-base`、`dsh-web-app`、`dsh-headless`、`dsh-sdk-app`、`dsh-acp-app` …）。profile 里 `source: "official"` 的条目必须出现在这张表里，校验时直接拒；这样「哪些 bundle 由内核提供」不需要另开字段，也不会和 profile 的 bundle 列表打架。

**bundled 的打包规则**：

- pnpm 的 `node_modules` 顶层是 junction，ZIP 不保存 reparse point，直接压包会丢链接；
- bundled 内核**只带 `.pnpm` 存储和 `package.json`**，不带顶层链接；
- 安装时用 `PnpmHeal.Relink` 从 `.pnpm` 重建 junction；
- `sha256` 对 `kernel/` 目录清单做哈希，安装时校验。

`runtime.node`：`inherit` 用本机 Node；`bundled` 带 `runtime/node/<ver>/`（类比 MC 带 Java）。

## 5. workspace 三种模式

| mode | cwd 指向 | 类比 PCL | 合并规则 |
|---|---|---|---|
| `bundled` | `<pack>/workspace` | 强版本隔离 | 包内，无合并 |
| `copy`（默认） | 安装时复制到 `instances/<id>/workspace` | 包是只读模板 | **取并集**：模板里有、用户没有的补上；用户已有的不覆盖 |
| `user` | 用户选的项目目录 | 关隔离 | 不写入，只记录 |

**用户数据永不覆盖**（sessions、workspace 里的用户文件）。

## 6. 完整性

`integrity` 是必填：

- `files[]`：`overrides/` 下每个文件的 `path + sha256 (+ bytes)`，既是校验清单，也是卸载账本；
- `root`：对所有条目按 `path` 排序后拼接 `path\0sha256\n`，再取 sha256（不含 `pack.json` 自身，避免自指）；
- `signature`（可选）：对 `root` 的签名，留给后续分发渠道。

`integrity.check` 决定装完之后怎么对待被改过的包内文件（有人偷偷换过）：

| check | 行为 |
|---|---|
| `off` | 不校验（等价 PCL：文件是什么就是什么） |
| `report`（默认） | 安装后 / 启动前比对账本，发现被改的只**报告**（"3 个包内文件已被修改"），不阻断 |
| `strict` | 被改过的包内文件**拒绝启动**，并提示"恢复为包内版本"（从包或 .pack-new 取回） |

安装顺序：读 manifest → 校验 `requires` → **校验 `integrity.root` 与每个文件** → 装内核 → 装 bundle → 写 patch → preset → 数据 → workspace → 记实例账本 → 按 `check` 建立基线。

## 7. agent preset

DSH 的 preset 不是文件目录，而是 `@deepseek-ai/dsh-agent-preset` 的**声明行**（`config.id` + `config.plugins`），由 `dsh-agent-preset-registry` 登记。

官方内置 4 个（随 `dsh-web-app` / `dsh-tui` 的 `presets/*.patch.yml` 出货）：

| id | patch 文件 |
|---|---|
| `standard` | `presets/standard.patch.yml` |
| `ptc` | `presets/ptc.patch.yml` |
| `minimal` | `presets/minimal.patch.yml` |
| `cordis` | `presets/cordis.patch.yml` |

manifest 里：

- `source: "official"` → 只写 `id`，从内核/bundle 的 presets 里取；
- `source: "bundled"` → 带一份 `overrides/presets/<id>.patch.yml`，安装时并入 profile 的 patch 层或作为独立 preset 声明。

## 8. 旧格式映射（`ccui-pack/v0.2` → v3）

| 旧字段 | 新位置 |
|---|---|
| `dsh.version` | `kernel.version`（mode 默认 `pin`） |
| `dsh.profile` | `profiles[0].name` |
| `dsh.plugins[].spec` | `profiles[0].bundles[].spec`（`source` 按官方清单判定） |
| `dsh.plugins[].required` | `bundles[].required` |
| `modules.*`（skills/mcp/rules/commands…） | `data[]` + `overrides/` 实际文件 |
| `bundle.files` | `overrides/` + `integrity.files` |
| `bundle.portable` | `meta.portable` |
| `meta.fidelity` / `meta.author` | `meta` 同名 |
| `icon.png` | 根目录保持，`pack.json` 的 `icon` 指向它 |

## 9. 校验规则

- `schema` 必须是 `pack-agent.pack/v3`（读取器兼容 `ccui-pack/v0.2`）；
- `name` / `version` / `kernel` / `profiles` / `integrity` 必填；
- `kernel.mode=bundled` 时必须有 `sha256`；
- 每个 profile 的 `source: official` bundle 必须在 `kernel.bundles` 里；
- `bundles[].source` 只能是 `official` / `registry` / `bundled`；`agents[].source=bundled` 必须带 `patch`；
- `overrides/` 内路径必须是相对路径，禁止 `..` 逃逸（zip-slip）；
- `requires.dsh` 与实际内核不满足时，安装前报错，不落盘；
- 同一 `DSH_HOME` 下 profile 名唯一；同一 profile 同时只允许一个运行实例（PAD 按 `(home, profile)` 记账）。

## 10. 冲突处理（安装 / 卸载时问用户）

冲突判据只有一条：**磁盘上的文件 hash ≠ 账本里记的 hash**（= 用户改过，或者被人换过）。

### 安装 / 升级

不预设策略，**逐条问用户**。GUI 弹一张冲突清单（可滚动），每条给三个选择：

| 选择 | 动作 |
|---|---|
| **替换** | 把用户版本备份到 `.pack-backup/<旧版本>-<时间>/`，写入包内版本 |
| **保留** | 不动用户文件；包内版本写成 `<文件>.pack-new`，供之后对比采纳 |
| **跳过** | 既不动用户文件，也不写 `.pack-new`（这次完全不管这个文件） |

- 默认选中 **保留**（最安全）；
- 顶部有「全部应用」——把当前选择套到剩余所有冲突；
- 列表里每条显示：路径、包内版本时间/大小、你的修改时间、diff 摘要（文本文件）；
- 没有冲突的文件不打扰用户，直接按包内版本覆盖（用户没改过，覆盖无风险）。

非交互（CLI / 脚本）：

```
--on-conflict replace|keep|skip     # 默认 keep
--yes                               # 用默认值（keep），不提问
```

### 卸载

同理，对"用户改过的包内文件"逐个问：

| 选择 | 动作 |
|---|---|
| **删除** | 连同用户修改一起删掉（会再确认一次） |
| **保留** | 留在原地，账本里标记为 orphan（不再归包管） |

默认 **保留**。没改过的文件直接删。

### 账本

每次安装/升级/卸载都记：`packVersion`、每个文件的 `sha256`、以及这次冲突的 `decision`（replace/keep/skip）。这样：

- 下次升级能判断"上次我选了保留，用户后来又改没改"；
- 卸载时能准确区分"包的文件"和"用户的东西"。

## 11. 跨平台与导出

导出时**先选平台**（多选，默认当前平台），再选输出形态：

| 形态 | 产物 | manifest |
|---|---|---|
| 多平台单包 | 一个 zip | `kernel.mode=bundled` + `kernel.platforms{ "win32-x64": {...}, "darwin-arm64": {...} }` |
| 单平台批量 | 每平台一个 zip，命名 `<name>-<version>-<platform>.pack.zip` | `kernel.mode=bundled` + `kernel.platform` |

安装时：

- 单平台包：`kernel.platform` 必须等于当前平台，否则按 `kernel.onMismatch`：`refuse`（默认，拒绝）/ `pin`（回退下载对应平台）/ `any`（强行装并警告）；
- 多平台包：从 `kernel.platforms` 取当前平台那份，取不到同样按 `onMismatch`；
- `pin` / `inherit` 不受平台限制（内核按平台从 npm 取）。

Node 运行时同理：`runtime.node=bundled` 时带 `platform`；不匹配且允许继承 → 用系统 Node（版本需满足 `requires.node`），否则报错。

**pnpm 链接的平台差异**：Windows 是 junction（PAD 的 `PnpmHeal`），macOS/Linux 是 symlink。安装时的 relink 必须按平台走不同实现，不能把 Windows 那套搬到别的平台。

注意：PAD 本身是 Windows-only（`net9.0-windows` + WPF），所以现在只有 Windows 侧的导入/导出实现；包格式跨平台，但 macOS/Linux 的消费者要等对应的宿主实现。
