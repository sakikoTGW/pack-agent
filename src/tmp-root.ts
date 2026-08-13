/**
 * pack-agent 临时目录。Windows 且存在 E:\ 时默认 E:\tmp\pack-agent，
 * 禁止落到 C:\Users\...\Temp（2026-08-13 曾把 C 盘撑满 ENOSPC）。
 * 覆盖：AGENT_PACK_TMP。
 */
import { existsSync, mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

export const DEFAULT_WIN_PACK_TMP = 'E:\\tmp\\pack-agent'

let pinned = false

export function packTmpRoot(): string {
  const env = process.env.AGENT_PACK_TMP?.trim()
  let root: string
  if (env) {
    root = env
  } else if (process.platform === 'win32' && existsSync('E:\\')) {
    root = DEFAULT_WIN_PACK_TMP
  } else {
    return tmpdir()
  }
  mkdirSync(root, { recursive: true })
  pinProcessTemp(root)
  return root
}

/** 让本进程里 tar/bun 等子工具也走同一根，避免再写 C:\Users\...\Temp */
function pinProcessTemp(root: string): void {
  if (pinned) return
  process.env.TMP = root
  process.env.TEMP = root
  process.env.TMPDIR = root
  process.env.BUN_TMPDIR = root
  pinned = true
}

export function packTmpDir(prefix: string): string {
  const name = `${prefix.replace(/[/\\]+$/g, '')}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  const dir = join(packTmpRoot(), name)
  mkdirSync(dir, { recursive: true })
  return dir
}

if (process.platform === 'win32') {
  packTmpRoot()
}
