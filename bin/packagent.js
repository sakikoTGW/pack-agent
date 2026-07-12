#!/usr/bin/env node
/**
 * npm 在 Windows 上生成的 .cmd/.ps1 shim 永远用 node 调用 bin 脚本，不看 shebang——
 * 而这个包的 CLI 源码是直接跑的 TypeScript（bun 原生执行，无编译步骤）。纯 node
 * 打不开 .ts 里 `import './x.js'`（实际指向 `x.ts`）这种 ESM 扩展名写法，会崩成一串
 * ERR_MODULE_NOT_FOUND 的原始 stack trace——不管是人还是 agent 拿到这条报错都摸不着头脑，
 * 跟"整合包一样简单"的目标背道而驰。这层做智能转发：有 bun 就转发过去，没有就给
 * 清楚的一句话指引，而不是让调用者自己去猜"为什么 node 跑不动"。
 *
 * 先探测再转发（两步）：在 Windows 上 `shell: true` 走 cmd.exe 解析 bun.cmd，
 * 但 bun 真不存在时是 cmd.exe 自己把 "不是内部或外部命令" 打到 stderr 再退出——
 * spawnSync 的 result.error 根本不会置位，无法用它判断"有没有装 bun"。
 * 所以先用不继承 stdio 的探测调用单独判断一次，探测通过了再跑真正继承 stdio 的那次。
 */
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
const target = join(here, '..', 'src', 'cli.ts')
const isWin = process.platform === 'win32'

function bunAvailable() {
  const probe = spawnSync('bun', ['--version'], { stdio: 'ignore', shell: isWin })
  return probe.status === 0
}

if (!bunAvailable()) {
  console.error(
    [
      '',
      'agent-pack CLI runs its TypeScript source directly via Bun (no build step) — Bun was not found on PATH.',
      '',
      'Install Bun, then re-run this command:',
      '  curl -fsSL https://bun.sh/install | bash        # macOS / Linux',
      '  powershell -c "irm bun.sh/install.ps1 | iex"     # Windows',
      '',
      'Docs: https://bun.sh',
    ].join('\n'),
  )
  process.exit(1)
}

const result = spawnSync('bun', [target, ...process.argv.slice(2)], {
  stdio: 'inherit',
  shell: isWin,
})

process.exit(result.status ?? 1)
