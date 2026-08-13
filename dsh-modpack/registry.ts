import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { parse as yamlParse } from 'yaml'
import { packAgentRoot } from '../src/package-root.js'

export type UnitMapping = {
  emit: string
  plugin: string
  insertIdPrefix?: string
  note?: string
}

export type DshModpackRegistry = {
  version: number
  units: Record<string, UnitMapping>
}

export function loadRegistry(path = join(packAgentRoot(), 'dsh-modpack', 'registry.yaml')): DshModpackRegistry {
  const raw = yamlParse(readFileSync(path, 'utf8')) as DshModpackRegistry
  if (!raw?.units || typeof raw.units !== 'object') {
    throw new Error(`invalid dsh-modpack registry: ${path}`)
  }
  return raw
}
