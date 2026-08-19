/**
 * schema 迁移：纯函数。缺 display 等字段时补上，不改用户已有值。
 */
import type { InstanceRecord } from './launcher.js'

export function migrateInstanceRecord(rec: InstanceRecord): { rec: InstanceRecord; changed: boolean; log: string[] } {
  const log: string[] = []
  let changed = false
  if (!rec.display) {
    rec.display = { info: '', logo: null, star: false, category: '' }
    log.push('fill display')
    changed = true
  }
  if (!rec.packs) {
    rec.packs = { allowSet: 'default' }
    log.push('fill packs.allowSet')
    changed = true
  }
  if (!rec.plugins) {
    rec.plugins = { disabled: [] }
    log.push('fill plugins.disabled')
    changed = true
  }
  return { rec, changed, log }
}
