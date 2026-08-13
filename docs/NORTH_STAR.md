# pack-agent 北极星

updated: 2026-07-30

## 一句话

**Agent Pack Language（APL）——一门有语法、类型、链接与多后端语义的 agent 能力语言，加处理器（`packagent`）校验、编译并跨 harness 投射。**

愿景详述见 [APL_VISION.md](./APL_VISION.md)；术语见 [GLOSSARY.md](./GLOSSARY.md)。

## 不是什么

- 不是 Cos Collab / CBL 产品——`cos.collab` 只是方言 Kind 样例包
- 不是「拷目录 + 口头约定」的安装脚本集合
- 不是某一 harness 的专属配置工具

## 是什么

像 LaTeX / Markdown / MATLAB：人写源（`Pack.toml` + Kind 规章），处理器 parse → typeck → IR → `.pack.zip`，再 codegen 到 Cursor / Claude Code / Codex / OpenClaw 等检测到的壳；可装可卸（ledger）。

旧 `ccui-pack/v0.2` 仅为遗留 IR，最终由 APL 源语言取代。

## Phase 顺序（据此实现）

1. **语言与处理器核心**：Glossary、Reference、parse/check/build/explain、稳定错误码与 diagnostics
2. **链接与 stdlib**：Kind / Unit / World / ABI 语义、stdlib Kind（`agent.skill` 等）、IR 规范与 `.pack.zip` 工件
3. **多 harness 投射**：codegen backends、install / eject、旧包升格（upgrade）
4. **生态与文档**：方言 Kind 样例（含 `cos.collab`）、Book、KINDS 目录、ccpkg / Claude plugin 互操作评估

各 Phase 以「语言 + 处理器可验收」为准，不以某一业务模块先落地为准。

## 关联文档

| 文档 | 职责 |
|------|------|
| [APL_VISION.md](./APL_VISION.md) | 终极目标、领域对标、成功画像、非目标 |
| [GLOSSARY.md](./GLOSSARY.md) | 术语定义 |
| [LANGUAGE.md](./LANGUAGE.md) | Reference（语法与语义真相） |
| [PACK_SPEC.md](./PACK_SPEC.md) | IR 规范（汇编层，待降为 IR 专述） |
