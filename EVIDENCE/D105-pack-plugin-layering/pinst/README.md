# Q4：pinst 与账本（导出带上、导入原样恢复、不重算）

date: 2026-11-04 · owner: p2-core（task-5 追加 Q4）· 状态：**PASS**
设计依据：`agent-pack-dsh/docs/pack-plugin-layering.md` §7 Q4。
改动：`agent-pack-dsh/modpack/pinst.ts`（`exportInstance` / `restorePinstHome`）、
`agent-pack-dsh/modpack/import-pack.ts`（restore-pinst 传实例目录）、`pinst.test.ts` 断言。

## 结论

1. **导出**：`exportInstance` 把 `<launcher-root>/instances/<id>/pack-ledger.json`（存在时）原样
   copy 到 pinst zip 根（与 `instance.json` / `manifest.json` / `home/` / `workspace/.agent-pack/` 同级）。
   不改 pinst 格式、不重算账本、不往 `manifest.stripped` 加字段。
2. **导入**：`restorePinstHome(..., destInstanceDir)` 把 zip 根的 `pack-ledger.json` 原样写回
   `instances/<新 id>/pack-ledger.json`。位置是**实例目录**，不是 `home/`（C# `PackLedger` 的
   `FilePath` 也在这里）。内容逐字节一致（测试用整文件字符串比较）。
3. **旧 pinst（P2 之前导出、没带账本）**：导入照常成功，但**不生成空账本文件** —— 保持
   「未登记」语义：`PackLedger.Load` 对缺失文件返回空账本且 `Exists=false`，UI 据此标「未登记」并给
   只读扫描重建。若在这里补一个空 `pack-ledger.json`，会把“没登记”伪装成“登记过但没包”，
   与 §7 Q4 相反，所以明确不补。
4. **不重算 / 不认领**：TS import-pinst 链没有 `add-plugins` / 写账本步骤；C# `Packs.Import`
   对 pinst 也会在 `RecordImportLedger` 里因 `PackManifest.TryRead(pinst.zip) == null` 直接跳过
   （日志“账本：读不出包清单（pinst 或旧格式），这次不登记”）。所以两条入口都不会重算账本。

## 实测（`pinst-run.log`）

```
✓ export strips credentials, no logs, ships pack-ledger.json as-is
✓ import new id, port auto, no credentials, restores pack-ledger.json verbatim
✓ legacy pinst without ledger imports fine, keeps 未登记 (no empty ledger file)
✓ bad pinst PA009
✓ pinst
```

断言点：
- 导出前先在 alpha 实例写一份带 `packs`/`owners` 的 `pack-ledger.json`；导出解包后断言 zip 根
  有该文件且**与原文完全相同**。
- `importPack` 导入后断言 `instances/<新 id>/pack-ledger.json` 存在、与原文逐字节相同，且
  `home/pack-ledger.json` 不存在（位置正确）。
- 新建一个没有账本的 gamma 实例导出旧式 pinst → 导入后断言**没有** `pack-ledger.json`（未登记），
  且 home 内容照常恢复。

复跑：`bun run agent-pack-dsh/modpack/pinst.test.ts`

## 文件

| 文件 | 内容 |
|---|---|
| `pinst-run.log` | `bun run agent-pack-dsh/modpack/pinst.test.ts` 完整输出（exit 0） |
