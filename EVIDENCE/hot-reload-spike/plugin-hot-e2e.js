import { writeFileSync, unlinkSync } from 'node:fs'

export const name = 'hot-e2e'

export function apply(ctx, config) {
  const file = (config && config.marker) || 'E:\\pack-agent\\.tmp\\lab-e2e\\hot-marker.txt'
  writeFileSync(file, new Date().toISOString())
  ctx.effect(() => () => { try { unlinkSync(file) } catch {} })
  console.log('[hot-e2e] apply called -> ' + file)
}

export default { name, apply }
