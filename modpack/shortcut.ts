/**
 * 直启快捷方式：写 library/shortcuts/<id>.bat，内容是 launcher run --detach。
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { getInstance, type LauncherRoot } from './launcher.js'

export function writeShortcut(root: LauncherRoot, id: string): string {
  getInstance(root, id)
  const dir = join(root.path, 'library', 'shortcuts')
  mkdirSync(dir, { recursive: true })
  const path = join(dir, `${id}.bat`)
  const body = [
    '@echo off',
    `set PACK_LAUNCHER_ROOT=${root.path}`,
    `packagent dsh launcher --root "%PACK_LAUNCHER_ROOT%" run ${id} --detach`,
    '',
  ].join('\r\n')
  writeFileSync(path, body)
  return path
}
