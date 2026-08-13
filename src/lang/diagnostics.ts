export type DiagCode =
  | 'E-EDITION-UNKNOWN'
  | 'E-KIND-NOT-FOUND'
  | 'E-LAYOUT-MISSING'
  | 'E-SCHEMA-INVALID'
  | 'E-RESOLVE-FAILED'
  | 'E-ABI-UNSATISFIED'
  | 'E-ABI-CONFLICT'
  | 'E-PORTABILITY-LEAK'
  | 'E-PROCESSOR-CAP'

export type DiagParams = Record<string, unknown> | undefined

export type Diag = {
  code: DiagCode
  message: string
  params?: Record<string, unknown>
}

function formatValue(value: unknown): string {
  if (typeof value === 'string') return JSON.stringify(value)
  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
    return String(value)
  }
  if (value === null) return 'null'
  if (value === undefined) return 'undefined'
  if (Array.isArray(value)) return `[${value.map(formatValue).join(', ')}]`
  if (value instanceof Error) return value.message
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

function formatParams(params?: Record<string, unknown>): string {
  if (!params || Object.keys(params).length === 0) return ''
  const parts = Object.entries(params).map(([key, value]) => `${key}=${formatValue(value)}`)
  return parts.join(', ')
}

export function formatDiag(diag: Diag): string {
  const paramsText = formatParams(diag.params)
  if (paramsText) return `${diag.code}: ${diag.message} (${paramsText})`
  return `${diag.code}: ${diag.message}`
}

function buildMessage(code: DiagCode, params?: Record<string, unknown>): string {
  switch (code) {
    case 'E-EDITION-UNKNOWN':
      return `unknown edition ${params?.edition ? formatValue(params.edition) : ''}`.trim()
    case 'E-KIND-NOT-FOUND':
      return `kind not found ${params?.kind ? formatValue(params.kind) : ''}`.trim()
    case 'E-LAYOUT-MISSING':
      return `layout missing ${params?.path ? formatValue(params.path) : ''}`.trim()
    case 'E-SCHEMA-INVALID':
      return `schema invalid ${params?.name ? formatValue(params.name) : ''}`.trim()
    case 'E-RESOLVE-FAILED':
      return 'resolve failed'
    case 'E-ABI-UNSATISFIED':
      return 'abi unsatisfied'
    case 'E-ABI-CONFLICT':
      return 'abi conflict'
    case 'E-PORTABILITY-LEAK':
      return 'portability leak'
    case 'E-PROCESSOR-CAP':
      return 'processor cap exceeded'
  }
}

export function diag(code: DiagCode, params?: DiagParams): Diag {
  const normalized = params ? { ...params } : undefined
  return {
    code,
    message: buildMessage(code, normalized),
    ...(normalized ? { params: normalized } : {}),
  }
}
