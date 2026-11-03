import { writeFileSync } from 'node:fs'
export const name = 'hot-e2e'
export function apply(ctx, config) {
  const file = (config && config.marker) || 'E:\\pack-agent\\.tmp\\lab-e2e\\hot-marker.txt'
  const services = ['hmr', 'profileContext', 'appReady', 'loader', 'pluginPackages']
    .map((s) => `${s}=${ctx.get(s) !== undefined}`).join(' ')
  console.log('[hot-e2e] services: ' + services)
  writeFileSync(file, new Date().toISOString() + ' ' + services)
}
export default { name, apply }