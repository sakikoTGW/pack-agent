/**
 * 货架：读缓存的 awesome-dsh plugins.json。安装仍走对该实例 dsh plugin add。
 */
import { getMeta } from './meta-cache.js'
import { pluginAddToInstance } from './plugin-ops.js'
import type { LauncherRoot } from './launcher.js'

export type MarketPlugin = {
  name: string
  npm?: string
  category?: string
  url?: string
  owner?: string
  stars?: number
  install?: string
  description?: { zh?: string; en?: string } | string
}

function pluginsOf(root: LauncherRoot): MarketPlugin[] {
  const hit = getMeta<{ plugins?: MarketPlugin[] }>(root, 'plugins')
  const list = hit?.body?.plugins
  return Array.isArray(list) ? list : []
}

function blob(p: MarketPlugin): string {
  const desc = p.description
  const d = typeof desc === 'string' ? desc : `${desc?.zh || ''} ${desc?.en || ''}`
  return `${p.name} ${p.npm || ''} ${p.category || ''} ${d}`.toLowerCase()
}

export function marketList(root: LauncherRoot, opts?: { category?: string }): MarketPlugin[] {
  const all = pluginsOf(root)
  if (!opts?.category) return all
  return all.filter((p) => p.category === opts.category)
}

export function marketSearch(root: LauncherRoot, q: string): MarketPlugin[] {
  const needle = q.trim().toLowerCase()
  if (!needle) return marketList(root)
  return pluginsOf(root).filter((p) => blob(p).includes(needle))
}

export function marketInstall(root: LauncherRoot, instanceId: string, name: string) {
  const hit = pluginsOf(root).find((p) => p.name === name || p.npm === name)
  const spec = hit?.npm || name
  return pluginAddToInstance(root, instanceId, spec)
}
