/**
 * 定位本包根目录。CLI 包名 @sakikotgw/pack-agent；DSH 精简包名 @sakikotgw/pack-agent-dsh。
 * 插件打成 agent-pack-dsh/plugin/lib/index.js 后，import.meta.dirname 不再是源码目录。
 */
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

export const PACK_AGENT_PKG = '@sakikotgw/pack-agent'
export const PACK_AGENT_DSH_PKG = '@sakikotgw/pack-agent-dsh'
export const PACK_AGENT_PKGS = [PACK_AGENT_PKG, PACK_AGENT_DSH_PKG] as const

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
        if (name && (PACK_AGENT_PKGS as readonly string[]).includes(name)) return dir
      } catch {
        // keep walking
      }
    }
    const parent = dirname(dir)
    if (parent === dir) break
    dir = parent
  }
  throw new Error(`pack-agent package root not found (${PACK_AGENT_PKGS.join(' | ')}) from ${startDir(start)}`)
}

/**
 * 包根下的资源。npm `@sakikotgw/pack-agent-dsh` 与仓库根同布局；
 * 若从 git 里的 `agent-pack-dsh/plugin/` 当包根，则回退到仓库根。
 */
export function packAgentFile(...rel: string[]): string {
  const root = packAgentRoot()
  const candidates = [join(root, ...rel), join(root, '..', ...rel), join(root, '..', '..', ...rel)]
  for (const p of candidates) {
    if (existsSync(p)) return p
  }
  return candidates[0]
}
