# @sakikotgw/pad-gateway

PAD 的「管理侧门」：在 DSH host 里起一个 **loopback-only** 的 HTTP carrier，把官方 `@deepseek-ai/dsh-host-apiproxy` 和 PAD 自己的跳转通道挂在一个随机端口上，写 `<DSH_HOME>/pad-gateway.json`（0600）广播 `{schema,url,token,pid,profile}` 给 PAD。

## 端点

| 方法 | 路径 | 鉴权 | 说明 |
|---|---|---|---|
| GET | `/pad/ping` | Bearer | 健康检查：`{ok, pid, profile, schema}` |
| * | `/api/*` | Bearer | 代理到官方 apiproxy（PAD 的 live session/取消等能力） |
| POST | `/pad/jump` | Bearer | 会话跳转事件入队：`{sessionId}` → `{ok, seq}` |
| GET | `/pad/jump?since=N` | Bearer | 长轮询下一条事件（25s 超时回 204） |
| POST | `/pad/jump/ack` | Bearer | 客户端消费回执：`{seq, ok}` |

跳转通道的作用：PAD 把「打开哪条会话」推给页面内的 `@sakikotgw/pad-session-jump` 消费端。浏览器页面的 origin 是 `http://127.0.0.1:<port>`，官方桌面端渲染器的 origin 是 `dsh-app://app`；两者都在 CORS 白名单内，其它 origin 一律 403。

## 事件与队列语义

- 内存队列，最多 20 条、TTL 30s；seq 单调递增。
- 每条事件只被 `since` 推进过它的客户端消费一次（幂等边界由 seq 游标保证）。
- 队列在插件 dispose 时清空；gateway 退出时删除自己的广告文件（只删 pid+token 匹配的那份）。

## 环境变量

| 变量 | 默认 | 说明 |
|---|---|---|
| `DSH_HOME` | — | 必填；广告文件与日志落在这里 |
| `PAD_GATEWAY_JUMP_TTL_MS` | 30000 | 事件 TTL |
| `PAD_GATEWAY_JUMP_TIMEOUT_MS` | 25000 | 长轮询超时 |

## 测试

```powershell
node --test test/jump.test.mjs   # 7 pass：id/origin 校验、队列、HTTP、CORS、超时
```

## 兼容性

`engines.dsh` 目前写死 `>=0.1.0-rc.7 <0.2.0`，且依赖 `@deepseek-ai/dsh-host-apiproxy` 的旧版本范围。跑在 DSH `0.2.x` 实例上之前需要先验证挂载与 apiproxy 导出；跳转端点本身只依赖 node:http，与 apiproxy 无关。
