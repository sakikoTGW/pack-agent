/**
 * 注册表热更新。坏文件 PA017，映射不切到坏路径。snapshot 冻结开工表。
 */
import { existsSync, mkdirSync, watch, type FSWatcher } from 'node:fs'
import { resolve } from 'node:path'
import { loadRegistryStore, RegistryError, type RegistryStore } from './registry-store.js'

export type LiveRegistry = {
  store: RegistryStore
  lastError: () => RegistryError | undefined
  snapshot: () => RegistryStore
  close: () => void
}

export function openRegistryStore(opts: { builtinDir: string; userDir: string }): LiveRegistry {
  let current = loadRegistryStore(opts)
  let lastErr: RegistryError | undefined
  let timer: ReturnType<typeof setTimeout> | undefined
  const reload = (): void => {
    try {
      current = loadRegistryStore(opts)
      lastErr = undefined
    } catch (e) {
      if (e instanceof RegistryError) lastErr = e
      else throw e
    }
  }
  const onChange = (): void => {
    if (timer) clearTimeout(timer)
    timer = setTimeout(reload, 80)
  }
  const watchers: FSWatcher[] = []
  for (const dir of [resolve(opts.builtinDir), resolve(opts.userDir)]) {
    mkdirSync(dir, { recursive: true })
    if (!existsSync(dir)) continue
    try {
      watchers.push(watch(dir, { persistent: false, recursive: true }, onChange))
    } catch {
      /* watch unavailable */
    }
  }
  return {
    get store() {
      return current
    },
    lastError() {
      return lastErr
    },
    snapshot() {
      return current.snapshot()
    },
    close() {
      if (timer) clearTimeout(timer)
      for (const w of watchers) w.close()
    },
  }
}
