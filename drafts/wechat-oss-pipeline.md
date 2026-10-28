# 公众号：跟 GitHub 开源学的一键流水线

> 不自创「Terminal Brief」。版面与粘贴链路用现成开源；写作节奏学个人工具文 + 竞品 README。  
> 本机已跑通日期：2026-07-18

---

## 1. 学了哪些仓库（方法论来源）

| 仓库 | 学什么 |
|------|--------|
| [zjp1997720/wechat-styler](https://github.com/zjp1997720/wechat-styler) | **MD → 全内联 HTML**；主题 YAML；`validate.mjs` 脚本把关（禁止靠模型记红线） |
| [isjiamu/gzh-design-skill](https://github.com/isjiamu/gzh-design-skill) | Agent Skill 流程：选主题 → 读组件库 → 装配 → **校验 ERROR=0** → 预览页一键复制 |
| [limin112/wechat-publish-template](https://github.com/limin112/wechat-publish-template) | 个人技术号：MD 结构映射 block（cover/intro/numbered-heading/code/cta）；色板锁死；教学向单主题 |
| [geekjourneyx/md2wechat-skill](https://github.com/geekjourneyx/md2wechat-skill) | 完整 CLI：`inspect` / `preview` / `convert --draft` / discovery JSON（适合有账号凭证时） |
| [xiaohuailabs/xiaohu-wechat-format](https://github.com/xiaohuailabs/xiaohu-wechat-format) | Format→Cover→Publish；CJK 空格、外链脚注、画廊选题 |

**共同硬规则（所有仓库一致）：**

1. 公众号剥 `<style>` / `class` → **样式必须全 inline**  
2. 兼容性用 **脚本校验**，不靠「我记得」  
3. 交付物是 **浏览器打开 → 全选复制 → 粘编辑器**（或预览页「复制」按钮）  
4. 图片外链不可靠 → 占位或微信 CDN 重传  
5. Agent 侧：写好 MD，再调转换器；**别让模型手写一长段 HTML**

---

## 2. 本仓库采用的一键路径（已实测）

本地镜像（可删可再 clone）：

```text
drafts/_vendor/wechat-styler/skills/wechat-styler/
```

### 命令（一次跑完排版）

```bash
cd drafts/_vendor/wechat-styler/skills/wechat-styler
npm install   # 首次

node scripts/convert.mjs ^
  ../../../../wechat-agent-modpack.md ^
  --theme magazine-indigo ^
  --font-size 15 ^
  --line-height 1.75 ^
  --output ../../../../wechat-agent-modpack.wechat.html

node scripts/validate.mjs ../../../../wechat-agent-modpack.wechat.html
```

**2026-07-18 结果：** `✓ 公众号兼容校验通过`，产物  
`drafts/wechat-agent-modpack.wechat.html`

粘贴：用浏览器打开该 HTML → Ctrl+A → Ctrl+C → 公众号编辑器粘贴。

### 主题怎么选（跟 styler 自己的表）

| 题材 | 主题 |
|------|------|
| 技术研究 / 工具深潜（本篇） | `magazine-indigo` |
| 科技产品 / 教程 | `modern` |
| 个人随笔 | `minimal` |
| 默认品牌示例 | `zhijian`（别人的品牌，可当参考另做 YAML） |

以后要有自己的色板：复制 `themes/zhijian.yaml` 改色，**不要**再手写一套「Terminal Brief CSS 散文」。

---

## 3. 写作节奏（学开源 README + 个人工具文，不是学排版器）

排版器不管你写得好不好。正文节奏对齐：

1. **标题**：`我做了个 X：结果/一句话`（样本：我把微信 cli 开源了…；OpenSkills README：What → Quick Start）  
2. **开场 3～5 句**：场景痛点 → 别人解决到哪 → 我做第三种  
3. **最快试一把**：一条已核实的 `npx`（前置，对标 openskills / agents-anywhere Quick Start）  
4. **自己怎么用**：短命令块，不分六步说明书  
5. **一个真坑**：故事讲完，附一句操作后果（假成功 / skipped）  
6. **不做什么** + 链接  

实测笔记另存 `wechat-verify-notes.md`，**不要**进读者稿，也**不要**进排版 HTML 源。

---

## 4. Agent 以后只准走的闸门

```
① 写/改 Markdown 正文（读者稿）
② 命令在本机跑通（入口 npx / 关键路径）→ 笔记进 verify-notes
③ wechat-styler convert + validate（ERROR=0）
④ 交付：.md + .wechat.html + 粘贴三步说明
⑤ （可选）有微信凭证再上 md2wechat convert --draft
```

禁止：

- 模型凭记忆手搓大段公众号 HTML  
- 自创与开源冲突的「版面散文规范」当主路径  
- 把校验日志贴进正文  

---

## 5. 其它开源备选（同一方法论，换实现）

```bash
# Agent Skill 安装（任选其一）
npx skills add zjp1997720/wechat-styler -g -y
# 或：clone isjiamu/gzh-design-skill，按 SKILL.md 选主题+validate_gzh_html.py
# 或：npm i -g @geekjourneyx/md2wechat && md2wechat preview article.md
```

`drafts/_vendor/` 仅本地学习/转换用，勿当产品源码提交（见 `.gitignore`）。
