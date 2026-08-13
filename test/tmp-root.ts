import { join } from 'node:path'
import { packTmpRoot } from '../src/tmp-root.js'

/** 测试与 CLI 同一根：Windows 默认 E:\tmp\pack-agent */
export const PACK_TEST_TMP = packTmpRoot()

export function ensurePackTestTmp(): string {
  return packTmpRoot()
}

export function packTestTmp(prefix: string): string {
  return join(ensurePackTestTmp(), prefix)
}
