# APL 愿景

updated: 2026-07-30

对外可引用的短文：pack-agent 要建成 Agent 界的「LaTeX / Markdown / MATLAB」——一门语言 + 一个处理器。

北极星与 Phase 见 [NORTH_STAR.md](./NORTH_STAR.md)；术语见 [GLOSSARY.md](./GLOSSARY.md)。

---

## 一句话

**写一次 agent 能力源，经处理器校验与链接，投射到任意 harness——像 Markdown→HTML、LaTeX→PDF、MATLAB→数值结果。**

---

## 领域对标

| 领域语言 | 人写什么 | 处理器做什么 | 产物 | Agent 界对应 |
|----------|----------|--------------|------|----------------|
| **Markdown** | 结构化纯文本 | 解析 → AST → 渲染 | HTML / 预览 | Pack 源 → 可读 explain + 可装树 |
| **LaTeX** | 排版语义源 | tex → DVI/PDF；宏包扩展 | PDF | Kind 方言 = 宏包；Edition = 引擎版本 |
| **MATLAB** | 矩阵/算法语言 | 解释/JIT；工具箱生态 | 数值/图 | loop / engineering 工具箱；stdlib = 基础工具箱 |
| **I❤️LA** | 可编译的「像黑板的」线性代数 | 一源多后端 | LaTeX + Python + MATLAB + C++ | **同一 Pack 源 → Cursor + Claude + Codex + …** |

今天的 agent 配置 = **手写汇编 + 口头约定**：拷目录、猜路径、别人不知道你的 `.collab` 是什么。

---

## 终极态

1. **作者**用 APL 声明 Kind / Unit / World / ABI（有专用语法，不是随便 JSON）
2. **`packagent check`** 像 rustc/tex：错就拒，并给稳定错误码
3. **`packagent build`** 产出 IR + `.pack.zip`（可分发工件）
4. **`packagent install`** 多 harness 投射；**`eject`** 可卸
5. **`packagent explain`** 任何人打开包都知道「是什么、依赖什么、导出什么」
6. **生态**：社区发布方言 Kind（engineering 方法、collab 规章、loop 契约），像 CTAN / MATLAB Toolbox / Markdown 扩展，而不是改 pack-agent 内核

---

## 成功画像（2030 心智，现在按此验收）

- **新手**：照 Book 写一个 skill+command 包，在两个 harness 装上
- **方法作者**：发布 `cos.collab` 类方言 Kind，他人 `use` 即可，无需 fork pack-agent
- **团队**：World 固定 exports/imports，CI 跑 `packagent check` 卡 ABI 回归
- **行业**：谈「agent 包」时默认有语言与处理器，散文件拷贝被视为过时

---

## 非目标（边界）

- **不取代** Cursor / Claude 内部真正跑 loop 的运行时——我们搬契约与配置
- **不做**中央商店——先语言 + 处理器；索引可后接
- **不把** Cos Collab / CBL 焊成北极星产品——仅为方言样例
- **不以**「先阉割功能、后补全」作为产品叙事——Phase 按语言处理器能力切片验收
