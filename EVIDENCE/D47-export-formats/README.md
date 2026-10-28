# D47 · 2026-10-28 · 导出支持两种形式：清单包 / 便携包（并解决没上架插件）

用户：*"要是有的插件他无法 npm 下载没上商店呢？我说导出的时候应该支持选导出成哪种，
一种是表（但是没上架的插件搞个文件夹塞进去）。另一种是解压即玩，实例什么的都要进去，
要让他们打开压缩包里的 PAD 就能玩。当然你别取名成解压即玩。"*

## 导出形式（导出页底部新增「导出形式」两块，二选一）

### 清单包（`.pack.zip`）

- `pack.json` 照旧：`dsh.version` + `dsh.plugins[]`（至少一个插件）。
- **没上架 npm 的插件**：导出页「插件」区多了「本地插件…」按钮，选插件文件夹 →
  整个目录塞进 `plugins/<名字>/`，清单条目写成
  `{ "spec": "名字@版本", "required": true, "local": "plugins/<名字>" }`；
  接收方解压后可以对这个目录 `dsh plugin add`。
- 已在 npm 的插件仍然只写 spec（包小）。

### 便携包（`.zip`）——名字不叫"解压即玩"

- 复用 PAD 已有的 `Launcher.ExportPortable(dest, instanceId)`：PAD 单文件 exe +
  `.pack-launcher/`（版本库、选中实例、runtime/node、registries；凭据/日志/临时文件剥离）。
- 导出时把它打进一个 zip：解压后根目录就是 `pack-agent-for DSH.exe` + `.pack-launcher`，
  **打开压缩包里的 PAD 就能跑**。
- 前提：先在版本选择里选中一个实例（便携包带的是这个实例）。

## 为什么不是"加东西"

这两块不是新页面：它们挂在已有的「导出整合包」页底部，导出前二选一；本地插件也只多一个
「本地插件…」按钮（没上架才用）。主流程仍是 核心 / 容器 / 插件 / 导出。

## 验证

- `packmaker make` 用 localPlugins 实测：包内含 `plugins/my-local-plugin@1.0.0/...`，
  `pack.json` 条目带 `local` 指路。
- `pad-window` 新增断言：页面必须有「清单包」「便携包」、不能出现「解压即玩」、
  必须有 `AddLocal_Click` / `LocalPlugins` / `ExportPortable` / `ZipFile.CreateFromDirectory`，
  `packmaker/main.go` 必须支持 `LocalPlugins`；`pad-window` exit 0、`pad-pack` exit 0。
- `dotnet build` 0 warning / 0 error；PAD 单次重启（pid 见提交时进程）。
