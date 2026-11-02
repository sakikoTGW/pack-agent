# @sakikotgw/pad-gateway-jump

PAD 的 **0.2.x 跳转专用 carrier**：在 DSH host 里起一个 loopback HTTP，只服务会话跳转通道，写与完整 `pad-gateway` 相同的 `<DSH_HOME>/pad-gateway.json` 广告，供 `@sakikotgw/pad-session-jump` 长轮询。

## 为什么单独一个包

完整 `@sakikotgw/pad-gateway` 会挂官方 apiproxy（`/api`），依赖 `@deepseek-ai/dsh-host-apiproxy` 并要求 `engines.dsh >=0.1.0-rc.7 <0.2.0`。但 **0.2.x 没有对应的 npm apiproxy**：装不上、bundle patch 里的 apiproxy 行也加载不了。本包只带跳转通道，`engines.dsh` 放宽到 `>=0.1.0-rc.7`，用于 0.2.x（也兼容 0.1.x，但 0.1.x 应用完整版 gateway 以保留 `/api`）。

## 端点

| 方法 | 路径 | 鉴权 | 说明 |
|---|---|---|---|
| GET | `/pad/ping` | Bearer | `{ok, pid, profile, schema, capabilities:{jump:true,api:false}}` |
| POST | `/pad/jump` | Bearer | 入队：`{sessionId}` → `{ok, seq}` |
| GET | `/pad/jump?since=N` | Bearer | 长轮询（25s 超时 204） |
| POST | `/pad/jump/ack` | Bearer | 消费回执 |
| * | `/api/*` | — | 固定 503：本包不代理 apiproxy |

CORS 只放行 loopback origin 与 `dsh-app://app`。队列语义（TTL 30s、容量 20、seq 单调）与完整版一致。

## 安装（0.2.x 实例）

```powershell
dsh plugin --profile web add file:E:\pack-agent\agent-pack-dsh\gateway-jump
```

装完重启 `dsh web`；`<DSH_HOME>/pad-gateway.json` 会出现，`capabilities.api=false`。

## 广告里的 `webUrl`（0.2 管理页的关键）

插件启动后通过 host 的 `webServer.getOrigin()` 学到 web 监听地址，把广告改写成：

```json
{
  "url": "http://127.0.0.1:<carrier-port>/api",
  "webUrl": "http://127.0.0.1:<web-port>",
  "apiStyle": "dsh-0.2",
  "capabilities": { "jump": true, "api": false }
}
```

- **跳转**仍然走 `url`（carrier `/pad/jump`）。
- **管理面**（会话树/取消/凭据状态）PAD 直接打 `webUrl` 的 `/api/<ns>/<method>`；0.2.x 的 `/api` 就是旧 `client-request` 协议，只是路径改成斜杠、payload 变成单参数 `args`（`session/list` 还要 `_request`，`session/cancel` 要 `request`）。
- `/api` 用 HttpOnly cookie 鉴权；PAD 不读浏览器 cookie，而是用 `<home>/.credentials.yaml` 里 `client-connection/browser-session` 的持久密钥**自签**同一个 `dsh-auth-<sha256(authority)>` cookie（见 PAD `GatewayAuth`）。

所以 0.2.x 的「管理」页也能列出会话，不再只靠 InstanceView。

## 测试与同步

```powershell
node --test test/jump.test.mjs        # 7 pass（与完整版共用实现）
node scripts/sync-from-gateway.mjs    # 从 ../gateway 同步 lib/jump.mjs 与测试
```

`lib/jump.mjs` / `test/jump.test.mjs` 是生成的副本，改实现请改 `../gateway` 后跑 sync。

## 与完整 gateway 的选择

| 实例 DSH | 用哪个 |
|---|---|
| `0.1.x` | `@sakikotgw/pad-gateway`（含 `/api` 代理 + 跳转） |
| `0.2.x` | `@sakikotgw/pad-gateway-jump`（跳转 + `webUrl` 管理面；不再声称 `/api` 不可用） |

PAD 侧按实例钉的 DSH 版本选择安装哪个包（`Cli.GatewaySpecFor`）。
