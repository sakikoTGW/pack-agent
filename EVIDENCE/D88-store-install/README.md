# D88 · 插件商店安装端到端验证（隔离实例，真实 npm）

用户问：现在能不能用插件商店给实例装插件、怎么操作、丝滑不丝滑。

## 结论：能装，已真实验证
隔离数据根 `E:\tmp\pad-roots\lead` 里新建实例 `verify`（0.1.0-rc.7，dsh-tui），
跑 UI 安装按钮同一条路径的 CLI 入口：

```
pad cli plugin add verify dsh-tui dsh-cost-meter
dsh plugin --profile dsh-tui add dsh-cost-meter
dsh: initialized profile dsh-tui at ...\instances\verify\home\profiles\dsh-tui
Progress: resolved 3, reused 0, downloaded 3, added 3, done in 12.5s using pnpm v11.7.0
[ "@deepseek-ai/dsh-base", "dsh-cost-meter" ]
```

- `profiles/dsh-tui/package.json` 的 `dsh.profile.bundles` 出现 `dsh-cost-meter`；
- `profiles/dsh-tui/node_modules/dsh-cost-meter/package.json` 存在；
- `01-installed-plugin.png`：装完后版本设置 → 插件页列出 `dsh-cost-meter`（真图标/版本/简介/更新徽标）。

## UI 操作步骤（DownloadView → 详情页安装）
1. 下载 → 插件货架 → 搜名字；
2. **先在顶部「装到」选目标实例**（没选会跳去版本选择页）；
3. 点一行进详情页 → 选版本 chip → 点「安装」；
4. 等 toast「开始装 …」→ 完成后提示进没进 `dsh.profile.bundles`；
5. **重启该实例**（或下次启动）才生效；管理 → 实例插件列表能看到。

## 丝滑度：能跑，但有几处不丝滑（待修）
1. 商店页不选实例就点安装 → 直接把界面跳去「版本选择 / 版本设置」，很突兀；
2. 安装只有 spinner +「正在装 xx」，没有进度/日志（pnpm 输出被吞）；默认 `installTimeoutSec=20s` 对首次慢网偏紧；
3. 装完不提示「重启实例才生效」，也不自动刷新管理页插件列表；
4. 商店里不能选 profile（自动用实例 lastProfile），多 profile 实例会装错地方；
5. 列表行没有「安装 / 已装」状态按钮，必须先进详情；
6. 「可更新」判定用 `Latest != Version` 字符串不等；缓存里 latest 比已装旧时会误标可更新（应 semver 比大小）。

（本次验证只写隔离根，没有动正式实例数据。）
