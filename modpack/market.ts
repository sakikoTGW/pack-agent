/**
 * 货架：缓存的 awesome-dsh plugins.json + 钉死的 dsh-TUI 组合包目录。
 * 安装仍走对该实例 dsh plugin add。
 */
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
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

const TUI_MARKET = join(dirname(fileURLToPath(import.meta.url)), 'tui-market.json')

function pluginsOf(root: LauncherRoot): MarketPlugin[] {
  const hit = getMeta<{ plugins?: MarketPlugin[] }>(root, 'plugins')
  const list = hit?.body?.plugins
  return Array.isArray(list) ? list : []
}

function pinnedTui(): MarketPlugin[] {
  if (!existsSync(TUI_MARKET)) return []
  try {
    const body = JSON.parse(readFileSync(TUI_MARKET, 'utf8')) as { plugins?: MarketPlugin[] }
    return Array.isArray(body.plugins) ? body.plugins : []
  } catch {
    return []
  }
}

function merged(root: LauncherRoot): MarketPlugin[] {
  const map = new Map<string, MarketPlugin>()
  for (const p of pinnedTui()) map.set(p.npm || p.name, p)
  for (const p of pluginsOf(root)) map.set(p.npm || p.name, p)
  return [...map.values()]
}

function blob(p: MarketPlugin): string {
  const desc = p.description
  const d = typeof desc === 'string' ? desc : `${desc?.zh || ''} ${desc?.en || ''}`
  return `${p.name} ${p.npm || ''} ${p.category || ''} ${d}`.toLowerCase()
}

export function marketList(root: LauncherRoot, opts?: { category?: string }): MarketPlugin[] {
  const all = merged(root)
  if (!opts?.category) return all
  return all.filter((p) => p.category === opts.category)
}

export function marketSearch(root: LauncherRoot, q: string): MarketPlugin[] {
  const needle = q.trim().toLowerCase()
  const all = marketList(root)
  if (!needle) return all
  return all.filter((p) => blob(p).includes(needle))
}

export function marketInstall(root: LauncherRoot, instanceId: string, name: string) {
  const hit = merged(root).find((p) => p.name === name || p.npm === name)
  const spec = hit?.npm || name
  return pluginAddToInstance(root, instanceId, spec)
}
