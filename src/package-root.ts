/**
 * 定位本包根目录（package.json name = @sakikotgw/pack-agent）。
 * DSH 插件会打成 dsh-plugin/lib/index.js，import.meta.dirname 不再是源码目录。
 */
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

export const PACK_AGENT_PKG = '@sakikotgw/pack-agent'

function startDir(from?: string): string {
  if (from) return from
  if (typeof import.meta.dirname === 'string' && import.meta.dirname) return import.meta.dirname
  return dirname(fileURLToPath(import.meta.url))
}

export function packAgentRoot(start?: string): string {
  let dir = startDir(start)
  for (let i = 0; i < 16; i++) {
    const pkgPath = join(dir, 'package.json')
    if (existsSync(pkgPath)) {
      try {
        const name = (JSON.parse(readFileSync(pkgPath, 'utf8')) as { name?: string }).name
        if (name === PACK_AGENT_PKG) return dir
      } catch {
        // keep walking
      }
    }
    const parent = dirname(dir)
    if (parent === dir) break
    dir = parent
  }
  throw new Error(`pack-agent package root not found (name ${PACK_AGENT_PKG}) from ${startDir(start)}`)
}
