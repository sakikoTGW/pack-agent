import { defaultKindRoots, loadKind } from './kind-load.js'

export const STDLIB_KIND_IDS: string[] = [
  'agent.skill',
  'agent.rule',
  'agent.mcp',
  'agent.command',
  'agent.hook',
  'agent.loop',
  'agent.plugin',
]

export function getStdlibInstallTemplate(kindId: string): string | undefined {
  const kind = loadKind(kindId, defaultKindRoots())
  return kind?.install?.default
}
