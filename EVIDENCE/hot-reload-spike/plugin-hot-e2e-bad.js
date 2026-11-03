import { writeFileSync } from 'node:fs'
export const name = 'hot-e2e-bad'
export function apply() {
  writeFileSync('E:\\pack-agent\\.tmp\\lab-e2e\\bad-applied.txt', new Date().toISOString())
  throw new Error('E3 boom: apply failed on purpose')
}
export default { name, apply }