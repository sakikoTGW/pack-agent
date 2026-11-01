# D85 · 插件详情改成独立窗口（插件市场版面）+ 标签只留关键分类

用户：「渲染成窗口呀，别这样」+「类别卡片只留几个最关键的，dsh/deepseek-harness/cordis 之类废话去掉」。

## 改了什么
1. **插件详情 = 独立窗口** `Views/PluginDetailWindow.xaml(.cs)`（700×600，Owner=PAD，ShowDialog）：
   顶部 56px 插件图标 + 名字 + spec + 版本/可更新徽标；正文是简介段落（不再是 markdown 字段列表），
   下面是过滤后的分类标签和 profile/npm latest 元信息；底部动作：更新 / 打开目录 / 卸载 / 关闭。
   原来塞在页面里的浮层（会跟侧栏错位）已删除。
2. **标签只留关键分类**：`PluginRow.Tags` 过滤噪音词（dsh、dsh-plugin、cordis、deepseek-harness、
   harness、plugin、ai、llm、agent…），最多保留 3 个；「可更新」仍是单独的醒目徽标，不混进标签。

## 验证
build 0 warning / 0 error；pad-window 绿。
说明：这台机器的 UI 自动化会把 PAD 主窗莫名最小化，详情窗口的截图没抓到；
代码路径是 ShowDialog 打开独立窗口，点行尾「详情」即可看到。
