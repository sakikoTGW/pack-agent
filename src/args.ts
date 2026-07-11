/** 解析 CLI 列表参数（兼容 PowerShell：逗号会被拆成空格） */
import { parseModulesList, type PackModules } from './modules.js'
export function splitListArg(raw: string): string[] {
  return raw
    .split(/[,;\s]+/)
    .map(s => s.trim())
    .filter(Boolean)
}

/** 收集 `--flag a,b` 或重复 `--flag x --flag y` */
export function collectListArg(args: string[], flag: string): string[] | undefined {
  const out: string[] = []
  for (let i = 0; i < args.length; i++) {
    if (args[i] !== flag) continue
    const v = args[++i]
    if (v && !v.startsWith('-')) out.push(...splitListArg(v))
  }
  return out.length ? [...new Set(out)] : undefined
}

export function hasFlag(args: string[], ...flags: string[]): boolean {
  return flags.some(f => args.includes(f))
}

export function parseCaptureAs(args: string[]): 'skill' | 'experience' | undefined {
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--capture-as' || args[i] === '--deliver') {
      const v = args[++i]
      if (v === 'skill' || v === 'experience') return v
    }
  }
  if (hasFlag(args, '--harness', '--with-harness')) return 'skill'
  if (hasFlag(args, '--experience', '--with-experience')) return 'experience'
  return undefined
}

export function parseOnConflict(args: string[]): 'stop' | 'skip' | 'replace' | undefined {
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--on-conflict') {
      const v = args[++i]
      if (v === 'stop' || v === 'skip' || v === 'replace') return v
    }
  }
  return undefined
}

export function parseAgentArg(args: string[]): string | undefined {
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--agent' || args[i] === '-a') {
      const v = args[++i]
      if (v && !v.startsWith('-')) return v
    }
  }
  return undefined
}

export function parseAllowFullScan(args: string[]): boolean {
  return hasFlag(args, '--all', '--full-scan')
}

/** --modules skills,hooks,memory 或 --no-memory */
export function parseModulesArg(args: string[]): PackModules | undefined {
  const list = collectListArg(args, '--modules')
  if (list?.length) return parseModulesList(list)
  const out: Partial<PackModules> = {}
  const flags = ['hooks', 'subagents', 'memory', 'settings', 'transcripts', 'experiences', 'skills', 'rules', 'mcp'] as const
  for (const f of flags) {
    if (hasFlag(args, `--no-${f}`)) out[f] = false
    if (hasFlag(args, `--with-${f}`, `--${f}`)) out[f] = true
  }
  return Object.keys(out).length ? out : undefined
}
