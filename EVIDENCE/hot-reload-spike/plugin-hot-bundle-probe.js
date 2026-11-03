import { writeFileSync, unlinkSync } from 'node:fs'
export const name = 'hot-bundle-probe'
export function apply(ctx) {
  const file = 'E:\\pack-agent\\.tmp\\lab-e2e\\bundle-marker.txt'
  writeFileSync(file, new Date().toISOString())
  ctx.effect(() => () => { try { unlinkSync(file) } catch {} })
  console.log('[hot-bundle-probe] mounted (with disposer)')
}
export default { name, apply }