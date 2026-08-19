/**
 * 各实例 plugin/version 安装共用 pnpm store，node_modules 仍在各自目录。
 */
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import type { LauncherRoot } from './launcher.js'

export function pnpmStoreDir(root: LauncherRoot): string {
  const dir = join(root.path, 'library', 'pnpm-store')
  mkdirSync(dir, { recursive: true })
  return dir
}

export function pnpmStoreEnv(root: LauncherRoot): Record<string, string> {
  const store = pnpmStoreDir(root)
  return {
    npm_config_store_dir: store,
    PNPM_STORE_DIR: store,
  }
}
