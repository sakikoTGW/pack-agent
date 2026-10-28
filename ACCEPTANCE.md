# ACCEPTANCE

> 每条都必须可执行、可判定。跑不通就是没做完。
> 证据一律落在 `EVIDENCE/<id>/`，不许用自然语言代替。

## P0 — 端到端最小闭环

**P0-1 只读体检，数字必须对**
```powershell
pad cli inspect --json --out E:\tmp\pad-inspect.json
```
- 输出含 `tools`、`toolsTokens`、`contextTokens`、`compactions`
- 在本机当前状态下：`tools == 71`、`toolsTokens == 12755`（±0）
- 无可用会话时输出合法 JSON（各字段为 0 或 null），不得崩溃、不得抛栈
- `--out` 是唯一可靠的输出通道：WinExe 的 stdout 不可被重定向捕获

**P0-2 只读，不污染**
```powershell
pad cli inspect <home> --json --out E:\tmp\x.json
```
- 跑完前后，该 home 内所有文件的 `名称+长度+LastWriteTimeUtc` 集合哈希必须相等
- 校验目标要选空闲 home：正在跑的实例会自己写盘，不能作为证据

**P0-3 建瘦实例**
```powershell
pad project new demo --profile minimal
```
- 生成独立 `DSH_HOME`，与默认 home 无包含关系
- 该 profile 的工具目录里不含 `browser_*`、`ppt*`、`team_*`、`schedule_*`
- `pad inspect demo --json` 的 `tools` 显著小于 71（目标 ≤ 30）

**P0-4 拉起 / 收回一个节点（不判读）**
```powershell
pad cli node open <home> --surface web --bin <bin.js> --json
pad cli node list <home>
pad cli node close <home> --profile web
```
- open 后端口在听、账本有记录、窗口指向带 token 的 URL
- PAD 退出后实例仍存活（PAD 管窗口，DSH 管端点）
- close 后进程树消失、账本清空、端口释放
- **PAD 不执行任何断言，不对结果做任何判断**（见 DECISIONS.md D18）

**P0-5 一张卡片只显示事实**
- PAD 窗口"项目"页显示：项目名、在跑 / 已停、端口、启动时间
- **不显示"待验收 / 通过 / 失败"**——那是人的事，不是启动器的事（D18）

## 非目标（P0 明确不做）

- 多 harness 支持（PRODUCT 已否）
- 插件市场 / 下载器
- UI 重做（PCL 形状不动）
- **跑断言 / 判读结果 / 验收台**（D18：PAD 是启动器，不是测试运行器）
- 自动修 bug / 自动重试
- 云端同步

## 这份文件的用途

它约束的是 **PAD 这个项目自己的开发**，不是 PAD 的产品功能。
PAD 不会去读任何项目的 `ACCEPTANCE.md`，也不会替谁验收。
