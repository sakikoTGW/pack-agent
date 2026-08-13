/**
 * 从 URL 或本地路径解析 pack 文件到本地绝对路径（URL 会下载到临时文件）。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import { packTmpRoot } from './tmp-root.js'

export function isRemotePackUrl(spec: string): boolean {
  return /^https?:\/\//i.test(spec.trim())
}

export async function resolvePackPath(spec: string): Promise<{ path: string; cleanup?: string }> {
  const s = spec.trim()
  if (!isRemotePackUrl(s)) {
    return { path: s }
  }
  const res = await fetch(s)
  if (!res.ok) {
    throw new Error(`download pack failed: HTTP ${res.status} ${s}`)
  }
  const buf = Buffer.from(await res.arrayBuffer())
  const urlPath = new URL(s).pathname
  const base = urlPath.split('/').pop() || 'download.pack.zip'
  const safe = base.replace(/[^\w.-]+/g, '_') || 'download.pack.zip'
  const dest = join(packTmpRoot(), `pack-agent-dl-${Date.now()}-${safe}`)
  await fs.writeFile(dest, buf)
  return { path: dest, cleanup: dest }
}
