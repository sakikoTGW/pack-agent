/** Mirrors pad/Core launch-script rules. JS twin of LaunchPolicy / CredentialsFile. */

export const DEEPSEEK_REF = 'DEEPSEEK_API_KEY'

/** True when a .cmd bakes the secret instead of loading `$DSH_HOME/.credentials.yaml`. */
export function launchScriptLeaksKey(text: string): boolean {
  for (const raw of String(text ?? '').split(/\r?\n/)) {
    const m = raw.match(/^\s*set\s+"DEEPSEEK_API_KEY=(.*)"\s*$/i)
    if (!m) continue
    const v = m[1]
    if (v.length === 0) continue
    if (v.startsWith('%') || v.startsWith('!')) continue
    return true
  }
  return false
}

export function isSecretEnvKey(key: string): boolean {
  return key === DEEPSEEK_REF
}

export function stripSecretEnv(text: string | null | undefined): string {
  const kept: string[] = []
  for (const raw of String(text ?? '').split(/\r?\n/)) {
    const line = raw.trim()
    if (line.length === 0 || line.startsWith('#')) {
      kept.push(raw)
      continue
    }
    const eq = line.indexOf('=')
    if (eq <= 0) {
      kept.push(raw)
      continue
    }
    if (isSecretEnvKey(line.slice(0, eq).trim())) continue
    kept.push(raw)
  }
  return kept.join('\n').replace(/\n+$/, '')
}

function norm(p: string): string {
  return p.replace(/[\\/]+$/, '').toLowerCase()
}

export function pathUnder(path: string, root: string): boolean {
  const a = norm(path)
  const b = norm(root)
  return a === b || a.startsWith(`${b}\\`)
}

export function cmdRelativeToRoot(absPath: string, launcherRoot: string): string | null {
  if (!pathUnder(absPath, launcherRoot)) return null
  const a = absPath.replace(/[\\/]+$/, '')
  const b = launcherRoot.replace(/[\\/]+$/, '')
  const rel = a.slice(b.length).replace(/^[\\/]+/, '').replace(/\//g, '\\')
  return `%PAD_ROOT%${rel}`
}

/** Owned instance scripts hang DSH_HOME / workspace off %PAD_INST%. Adopted homes stay absolute. */
export function cmdSetDshHome(adopted: boolean): string {
  return adopted ? 'set "DSH_HOME=' : 'set "DSH_HOME=%PAD_INST%home"'
}

export function cmdCdOwnedWorkspace(): string {
  return 'cd /d "%PAD_INST%workspace"'
}

export function cmdNode(absNode: string | null, launcherRoot: string): string {
  if (absNode) {
    const rel = cmdRelativeToRoot(absNode, launcherRoot)
    if (rel) return `"${rel}"`
  }
  return 'node'
}

/** runtime.json is PAD's own TrackRun. Ready means the Harness node pid exists. */
export function launchHarnessReady(harnessAlive: boolean): boolean {
  return harnessAlive
}

export function launchWaitGiveUp(elapsedMs: number, graceMs = 45_000): boolean {
  return elapsedMs >= graceMs
}

export function shouldSkipDuplicateLaunch(harnessAlive: boolean): boolean {
  return harnessAlive
}

export function isFrameworkDependentHost(hasPadDllBeside: boolean, hasHostDllBeside = false): boolean {
  return hasPadDllBeside || hasHostDllBeside
}

export function portableEphemeral(): string[] {
  return ['pad-cli.log', 'runtime.json', 'jobs.json']
}

export function junctionHealthy(targetExists: boolean, targetUnderPnpm: boolean): boolean {
  return targetExists && targetUnderPnpm
}

/** Windows MAX_PATH is 260 including the trailing NUL, so 259 is the last usable char. */
export const WIN_MAX_PATH_CHARS = 259

export function winPathTooLong(pathLength: number): boolean {
  return pathLength > WIN_MAX_PATH_CHARS
}

/** Nested copies inside `.pnpm/<long-id>/.../pkg/node_modules` must be moved into
 *  `.pnpm/<spec>@<ver>` and junctioned. A real directory is an orphan; a junction
 *  that already points at that store is done. */
export function shouldPromoteNestedDep(isReparse: boolean, alreadyStorePhys: boolean): boolean {
  if (alreadyStorePhys) return false
  return !isReparse
}

/** After a nested dep is moved into its own `.pnpm/<spec>@<ver>` store, Node
 *  realpath's the importer into that store. `@dsh-std/core` must sit beside it
 *  in the same isolated `node_modules`, same as pnpm's linker. */
export function isolatedImporterNeedsSiblingLink(
  importerRealpathInOwnStore: boolean,
  siblingPresentInIsolated: boolean,
): boolean {
  return importerRealpathInOwnStore && !siblingPresentInIsolated
}

/** First numeric major in an npm range (`^6.2.1` → `6`). `^8 || ^9` takes 9. */
export function depRangeMajor(range: string | null | undefined): string | null {
  if (!range) return null
  if (range.includes('||')) {
    let best = -1
    for (const part of range.split('||')) {
      const m = part.match(/\d+/)
      if (m) {
        const n = Number(m[0])
        if (Number.isFinite(n) && n > best) best = n
      }
    }
    return best < 0 ? null : String(best)
  }
  const m = range.trim().match(/\d+/)
  return m ? m[0] : null
}

export function pickPnpmPhys(
  storeFolderNames: string[],
  spec: string,
  preferVer: string | null,
  preferMajor: string | null = null,
): string | null {
  const encoded = spec.replaceAll('/', '+') + '@'
  const hits = storeFolderNames.filter((n) => n.toLowerCase().startsWith(encoded.toLowerCase()))
  const restOf = (n: string) => n.slice(encoded.length)
  const matchVer = (n: string) => {
    if (!preferVer) return false
    const rest = restOf(n)
    return rest === preferVer || rest.startsWith(preferVer + '_') || rest.startsWith(preferVer + '+')
  }
  if (preferVer) {
    const hit = hits.find(matchVer)
    return hit ?? null
  }
  if (preferMajor) {
    const majorHits = hits.filter((n) => {
      const rest = restOf(n)
      return (
        rest === preferMajor
        || rest.startsWith(preferMajor + '.')
        || rest.startsWith(preferMajor + '_')
        || rest.startsWith(preferMajor + '+')
      )
    })
    if (majorHits.length === 0) return null
    return [...majorHits].sort((a, b) => restOf(a).localeCompare(restOf(b), 'en'))[majorHits.length - 1]
  }
  if (hits.length === 1) return hits[0]
  return null
}
