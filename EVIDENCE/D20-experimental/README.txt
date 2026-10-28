D20 实验性开关 — 实测 (2026-10-23 00:53)

开关：pad.json#other.experiments（字符串数组，空 = 全关）

[1] pad cli experimental list
    { "id": "layers", "description": "系统 → 层 → 模块：同一项目里按职能分工具面", "enabled": false }

[2] pad cli layer list <system>          (关着)
    error[PA096]: experimental feature is off: system & layer
     --> pad.json#other.experiments
      = help: pad cli experimental enable layers

[3] pad cli experimental enable layers
    pad.json -> "experiments": [ "layers" ]

[4] pad cli layer list <system>          (开着)
    empty    backend   ...   running 0
    empty    collect   ...   running 0
    profile  frontend  ...   running 0

[5] pad cli experimental disable layers
[6] pad cli layer list <system>          (又关回去)
    error[PA096] 同上

姿态与 DSH 对 @deepseek-ai/dsh-experimental-* 的处理一致：
随包发布，默认关，手动开，开了才出现在路径上。
