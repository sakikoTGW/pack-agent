import { DEFAULT_DSH_VERSION } from './dsh-version.js'

export type EmptyState = { title: string; hint: string; action: string }

export type ListEnvelope<T> = { items: T[]; emptyState?: EmptyState }

const EMPTY: Record<string, EmptyState> = {
  'version.list': {
    title: '无可用版本',
    hint: '先安装一个 DSH 发行号',
    action: `packagent dsh launcher version install ${DEFAULT_DSH_VERSION}`,
  },
  'instance.list': {
    title: '还没有实例',
    hint: 'create 或 import',
    action: 'packagent dsh launcher instance create <name>',
  },
  'plugin.list': {
    title: '该 profile 还没有组合包',
    hint: '对该实例 plugin add',
    action: 'packagent dsh launcher plugin add <id> <spec>',
  },
  'session.list': {
    title: '还没有 session',
    hint: 'run 之后会按工作区路径出现',
    action: 'packagent dsh launcher run <id>',
  },
  'job.list': {
    title: '没有任务',
    hint: '装版本 / 导入 / 克隆会生成任务',
    action: 'packagent dsh launcher job list',
  },
  'pack.list': {
    title: '该工作区还没有投影包',
    hint: 'project 后再 allow',
    action: 'packagent dsh launcher pack project <id> <pack>',
  },
  'market.list': {
    title: '货架是空的',
    hint: '先刷新 plugins.json 缓存',
    action: 'packagent dsh launcher meta refresh plugins',
  },
}

export function emptyStateFor(kind: string): EmptyState {
  return (
    EMPTY[kind] || {
      title: '没有条目',
      hint: '换一条命令或先创建',
      action: 'packagent dsh launcher help',
    }
  )
}

export function wrapList<T>(kind: string, items: T[]): ListEnvelope<T> {
  if (items.length === 0) return { items: [], emptyState: emptyStateFor(kind) }
  return { items }
}

export function listOrEmpty<T>(kind: string, items: T[]): T[] | ListEnvelope<T> {
  if (items.length === 0) return wrapList(kind, items)
  return items
}
