# agent-pack for DSH · 设计稿

先读 [PRODUCT.md](PRODUCT.md)。

| 文件 | 内容 |
|---|---|
| [PRODUCT.md](PRODUCT.md) | 产品定义 |
| [modpack.md](modpack.md) | 整合包装包链 |
| [pack-plugin-layering.md](pack-plugin-layering.md) | 整合包 × 插件两层分账：包=原子装配清单、账本、diff 先行、停用≠卸载（**P1/P2/P3 已落地**，D105；实现索引见 §9） |
| [frontend-spec.md](frontend-spec.md) | **前端规范 v1**：文字纪律（能删就删/有用但吵就藏）、视觉优先、一次一件事、顺手与反馈、同类统一、Tokens/组件/动效 + PR 10 条检查清单（D109；配图手册 `frontend-spec/index.html`） |
| [proposals/install-concept/index.html](proposals/install-concept/index.html) | **「装整合包」概念原型**：版本页安装▾ / 实例页包库选择器 / 预检授权与安装报告（D108 已按此落地，三张 HTML+PNG） |
| [pack-v2-superset.md](pack-v2-superset.md) | pack v2：超集 + PackForge `.dspack` 双向兼容（适配器/市场/registry/compat 安装已落地，见 EVIDENCE/D101、D102） |
| [launcher-design.md](launcher-design.md) | 启动器功能 |
| [launcher-pcl-depth.md](launcher-pcl-depth.md) | 学 PCL 的功能深化：设置两层、窗口补全 CLI |
| [launcher-architecture.md](launcher-architecture.md) | 引擎 / 数据 / 命令 |
| [launcher-p0.md](launcher-p0.md) | P0 计划（已落地） |
| [session-jump.md](session-jump.md) | 会话跳转：surface 能力注册表 + planner；插件/gateway/jump-only carrier/PAD M1 已落地（浏览器 E2E 通 gateway 通道） |
| [launcher-behavior-2026-11.md](launcher-behavior-2026-11.md) | 2026-11 启动器行为链研究（24 个项目） |
| [launcher-inventory-2026-11.md](launcher-inventory-2026-11.md) | 第二轮：启动器生态全量清单（单列表去重 1,222 客户端 / 325 启动器；新增 1,207） |
| [launcher-top30-2026-11.md](launcher-top30-2026-11.md) | 第三轮：30 个重点启动器的 7 段行为链 + 截图（TUI/桌面/管理/移动/远程） |
| [launcher-landscape-2026-11.md](launcher-landscape-2026-11.md) | 2026-11 桌面端/桌面壳生态研究（已降级为邻接物） |
| [launcher-opportunities-2026-11.md](launcher-opportunities-2026-11.md) | 2026-11 可做事项报告：生态基线 × 代码缺口 × 工作项池 × 路线 |
| [lab.md](lab.md) | **PAD Lab 试验场方案**：隔离副本 × 试验 × 验证判据 × 晋升/回退（待拍板，P0 未落码） |
| [hot-reload-integration.md](hot-reload-integration.md) | **官方 HMR 集成方案**：默认冷、场景化热（lab/`--hot`）、读盘能力判定、V1–V4 验证与回滚（待拍板） |
