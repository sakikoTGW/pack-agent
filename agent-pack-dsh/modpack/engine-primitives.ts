/**
 * 引擎原语 / 嗅探 handler。doctor 与 import 共用。
 */
export const ENGINE_PRIMITIVES = [
  'sniff',
  'resolve-version',
  'ensure-version',
  'create-instance',
  'copy-overrides',
  'project-allow',
  'install-manager',
  'add-plugins',
  'read-pinst',
  'restore-pinst',
] as const

export const ENGINE_HANDLERS = ['import-pack', 'import-pinst'] as const

export type EnginePrimitive = (typeof ENGINE_PRIMITIVES)[number]
export type EngineHandler = (typeof ENGINE_HANDLERS)[number]
