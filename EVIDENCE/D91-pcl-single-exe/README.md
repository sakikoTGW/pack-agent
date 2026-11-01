# D91 · 学 PCL：单文件 exe 分发（不再依赖 kit 目录）

日期：2026-11-02

## PCL 的做法（调研）

- 官网只给下载链接（爱发电 0 元获取），**没有安装器**：https://meloong.com/pcl
- 软件是**单文件绿色版**：约 5.98MB，解压即用，数据在 exe 旁边，运行依赖系统自带 .NET Framework 4.8
  （来源：[剑客网 PCL2 2.99 说明](https://www.go176.net/post-78.html)）
- 仓库 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 根目录有 最新正式版.zip（约 2.6MB）
  和 patches/Release *.patch（逐版增量补丁）——更新是"下载新包/补丁 → 替换自己"，不是走安装器。
- 结论：PCL 的"安装形态" = **一个 exe + 数据跟着 exe 走 + 自己更新自己**。

## PAD 照做

新增 Core/SelfBundle.cs + scripts/make-single-exe.ts：

- 单文件 PAD exe（126.7MB，自包含 .NET，不要求系统装 .NET）末尾追加：
  pack-runtime.zip（bun + packagent bundle + pack-index + 包根资源，52.1MB）+ 40 字节 footer
  （PADPACKRT1 + payload 长度 + 偏移 + payload sha256 前 8 字节）。
- 首次真正用到整合包功能时，PAD 自己把 payload 解压到 exe 旁 .pack-runtime/；
  footer 里的 hash 变了就重解压（同长度更新也不会复用旧运行时）。
- 用户只下载 **一个 exe**（178.9 MB），不需要安装器、不需要 node/bun、不需要目录结构；
  .pack-launcher / .pack-runtime 都跟着 exe 走，换机器整个文件夹拷走即可。

## 验证（单文件 exe，空目录，无仓库/无环境变量）

- 把 dist/single/pack-agent-for DSH.exe 放进空目录，cli pack list e2e3 → 自动解压 .pack-runtime/ 后正常列出；
- cli instance create e2e6 + cli pack project → 投影成功（完整安装链在单文件形态下可用）；
- 第二次调用复用已解压运行时（.payload 标记 = offset-length-hash）；
- pad-pack / pad-window / p2-p3 / pack-catalog 全绿。

## 与 PCL 的差距 / 下一步

- PCL 用 .NET Framework 4.8（系统自带）所以只有 6MB；PAD 自包含 .NET + 内置 bun，所以 179MB——
  换来的是目标机**零依赖**（连 .NET 都不用装）。
- 还差 PCL 式**自动更新通道**：发布 latest.json + zip/增量补丁，PAD 下载后下次启动替换自身（数据不动）。
