/**
 * 收编已有 DSH_HOME。不改里面的文件。同 home → PA020。外部改凭据 → PA106。
 */
import { createHash } from 'node:crypto'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { homedir } from 'node:os'
import { join, resolve } from 'node:path'
import {
  createInstance,
  getInstance,
  pa,
  writeInstance,
  writeLauncherAdoptedHome,
  type Diagnostic,
  type InstanceRecord,
  type LauncherRoot,
} from './launcher.js'

export function defaultDshHome(): string {
  return resolve(process.env.DSH_HOME?.trim() || join(homedir(), '.dsh'))
}

export function fingerprintCredentials(home: string): string {
  const cred = join(home, '.credentials.yaml')
  if (!existsSync(cred)) return ''
  return createHash('sha256').update(readFileSync(cred)).digest('hex')
}

export function adoptExistingHome(
  root: LauncherRoot,
  opts: { home?: string; name?: string; id?: string; version: string; profile?: string },
): InstanceRecord {
  const home = resolve(opts.home || defaultDshHome())
  if (!existsSync(home) || !statSync(home).isDirectory()) {
    throw pa('PA009', `adopt home does not exist: ${home}`, home)
  }
  const rec = createInstance(root, {
    name: opts.name || 'local-dsh',
    id: opts.id || 'local-dsh',
    version: opts.version,
    profile: opts.profile,
    home,
    adopted: true,
    credentialsKind: 'instance',
  })
  rec.adoptedFingerprint = { credHash: fingerprintCredentials(home) }
  writeInstance(root, rec)
  writeLauncherAdoptedHome(root, home)
  return rec
}

export function checkAdoptedHome(root: LauncherRoot, id: string): Diagnostic | undefined {
  const inst = getInstance(root, id)
  if (!inst.adopted) return undefined
  const home = resolve(inst.home)
  const now = fingerprintCredentials(home)
  const was = inst.adoptedFingerprint?.credHash || ''
  if (now === was) return undefined
  return {
    code: 'PA106',
    level: 'warning',
    message: 'adopted DSH_HOME was changed outside the launcher',
    location: `instance \`${id}\` / home`,
    help: ['re-adopt this home', `packagent dsh launcher instance adopt --home ${home}`],
  }
}
