/**
 * 全局钥匙 library/credentials.yaml，0600。命名钥匙 library/credentials/<name>.yaml。
 * DSH 同构 REF: 字符串。钥匙不进 instance.json。
 */
import { chmodSync, copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import type { LauncherRoot } from './launcher.js'

export function globalCredentialsPath(root: LauncherRoot): string {
  return join(root.path, 'library', 'credentials.yaml')
}

export function namedCredentialsDir(root: LauncherRoot): string {
  return join(root.path, 'library', 'credentials')
}

export function namedCredentialsPath(root: LauncherRoot, name: string): string {
  if (name === 'global') return globalCredentialsPath(root)
  return join(namedCredentialsDir(root), `${name}.yaml`)
}

export function setGlobalCredentials(root: LauncherRoot, yaml: string): string {
  const path = globalCredentialsPath(root)
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, yaml)
  chmodSync(path, 0o600)
  return path
}

export function setNamedCredentials(root: LauncherRoot, name: string, yaml: string): string {
  if (name === 'global') return setGlobalCredentials(root, yaml)
  const path = namedCredentialsPath(root, name)
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, yaml)
  chmodSync(path, 0o600)
  return path
}

export function getGlobalCredentials(root: LauncherRoot): string | null {
  const path = globalCredentialsPath(root)
  if (!existsSync(path)) return null
  return readFileSync(path, 'utf8')
}

export function getNamedCredentials(root: LauncherRoot, name: string): string | null {
  const path = namedCredentialsPath(root, name)
  if (!existsSync(path)) return null
  return readFileSync(path, 'utf8')
}

export function listCredentialSets(root: LauncherRoot): string[] {
  const names: string[] = []
  if (existsSync(globalCredentialsPath(root))) names.push('global')
  const dir = namedCredentialsDir(root)
  if (existsSync(dir)) {
    for (const f of readdirSync(dir)) {
      if (!f.endsWith('.yaml')) continue
      const id = f.slice(0, -5)
      if (!names.includes(id)) names.push(id)
    }
  }
  return names.sort()
}

export function copyGlobalCredentialsToHome(root: LauncherRoot, home: string): void {
  copyNamedCredentialsToHome(root, home, 'global')
}

export function copyNamedCredentialsToHome(root: LauncherRoot, home: string, name: string): void {
  const src = namedCredentialsPath(root, name)
  if (!existsSync(src)) return
  const dest = join(home, '.credentials.yaml')
  mkdirSync(home, { recursive: true })
  copyFileSync(src, dest)
  chmodSync(dest, 0o600)
}
