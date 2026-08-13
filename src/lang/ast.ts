export type WorldAst = {
  exports?: string[]
  imports?: string[]
}

export type UnitAst = {
  name: string
  kind: string
  path: string
  title?: string
  exports?: string[]
  imports?: string[]
}

export type PackAst = {
  edition: string
  name: string
  version?: string
  units: UnitAst[]
  world?: WorldAst
}
