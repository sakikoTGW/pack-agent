/**
 * 整合包带启动器：目录对齐 PCL 解压包
 * pcl.exe + .minecraft + 旁边的 zip
 * → pack-agent-for DSH.exe + .pack-launcher
 * 包根只放 exe 和文件夹，运行时 dll 打进单文件 exe。
 */
import { existsSync, mkdirSync, copyFileSync, writeFileSync, statSync, readdirSync, rmSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import { ensureRoot } from './launcher.js'
import { launchScriptLeaksKey } from './launch-script.ts'

export const HOST_EXE_NAME = 'pack-agent-for DSH.exe'

export type PortableKit = {
  kit: string
  launcherRoot: string
  openBat: string
  hostExe: string
  examplePack: string
  exampleZip: string
}

const HERE = dirname(fileURLToPath(import.meta.url))
const REPO_FROM_HERE = resolve(HERE, '../..')
const PAD_DIR = resolve(HERE, '../pad')
const PAD_PROJ = join(PAD_DIR, 'Pad.csproj')
const CACHE_DIR = resolve(HERE, '../portable/pad-dist')
const TMP = process.env.AGENT_PACK_TMP || 'E:\\tmp\\pack-agent'

export function openBatBody(_opts: { repo: string }): string {
  return [
    '@echo off',
    'chcp 65001 >nul',
    'set "HERE=%~dp0"',
    'set "PACK_LAUNCHER_ROOT=%HERE%.pack-launcher"',
    'if exist "%HERE%*.pack.zip" move /Y "%HERE%*.pack.zip" "%PACK_LAUNCHER_ROOT%\\" >nul',
    'if exist "%HERE%*.pinst.zip" move /Y "%HERE%*.pinst.zip" "%PACK_LAUNCHER_ROOT%\\" >nul',
    `if not exist "%HERE%${HOST_EXE_NAME}" (`,
    `  echo ${HOST_EXE_NAME} 不在同目录。`,
    '  pause',
    '  exit /b 1',
    ')',
    `start "" "%HERE%${HOST_EXE_NAME}"`,
    '',
  ].join('\r\n')
}

function writeExamplePack(dir: string): void {
  mkdirSync(join(dir, 'skills', 'hello'), { recursive: true })
  mkdirSync(join(dir, 'overrides'), { recursive: true })
  writeFileSync(
    join(dir, 'skills', 'hello', 'SKILL.md'),
    '---\nname: hello\ndescription: example skill, maps to MC mods/\n---\n# hello\n\npack-agent 例包。\n',
  )
  writeFileSync(join(dir, 'overrides', 'AGENTS.md'), '# example override\n')
  writeFileSync(
    join(dir, 'pack.json'),
    JSON.stringify(
      {
        schema: 'ccui-pack/v0.2',
        name: 'example',
        version: '0.1.0',
        dsh: {
          version: '0.1.0-rc.8',
          profile: 'dsh-tui',
          overrides: [{ from: 'overrides/AGENTS.md', to: 'AGENTS.md' }],
        },
        knowledge: { skills: [{ name: 'hello', source: 'bundled' }] },
        tools: { mcp: [] },
        meta: { fidelity: 'L1', source: 'portable-example' },
        bundle: {
          portable: true,
          files: [
            {
              path: 'skills/hello/SKILL.md',
              content:
                '---\nname: hello\ndescription: example skill, maps to MC mods/\n---\n# hello\n\npack-agent 例包。\n',
            },
          ],
        },
      },
      null,
      2,
    ) + '\n',
  )
}

function zipDir(from: string, zipPath: string): void {
  if (existsSync(zipPath)) rmSync(zipPath, { force: true })
  const r = spawnSync('tar', ['-a', '-cf', zipPath, '-C', from, '.'], { encoding: 'utf8' })
  if (r.status !== 0 || !existsSync(zipPath)) {
    throw new Error(`zip failed: ${(r.stdout || '') + (r.stderr || '') || r.status}`)
  }
}

const SEVEN_ZIP = 'C:\\Program Files\\7-Zip\\7z.exe'

function walkLaunchCmds(dir: string, acc: string[] = []): string[] {
  if (!existsSync(dir)) return acc
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === '.pnpm') continue
    const p = join(dir, name)
    let st
    try { st = statSync(p) } catch { continue }
    if (st.isDirectory()) walkLaunchCmds(p, acc)
    else if (/^launch-.*\.cmd$/i.test(name)) acc.push(p)
  }
  return acc
}

function walkCredentialFiles(dir: string, acc: string[] = []): string[] {
  if (!existsSync(dir)) return acc
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === '.pnpm') continue
    const p = join(dir, name)
    let st
    try { st = statSync(p) } catch { continue }
    if (st.isDirectory()) walkCredentialFiles(p, acc)
    else if (name === 'credentials.yaml' || name === '.credentials.yaml') acc.push(p)
  }
  return acc
}

/** Refuse to zip a kit whose launch scripts or yaml still hold DEEPSEEK_API_KEY. */
export function assertPlayKitSafe(kitDir: string): void {
  const kit = resolve(kitDir)
  for (const f of walkLaunchCmds(kit)) {
    if (launchScriptLeaksKey(readFileSync(f, 'utf8')))
      throw new Error(`play kit leaks DEEPSEEK_API_KEY in ${f}`)
  }
  const launcher = join(kit, '.pack-launcher')
  const scanRoots = [
    join(launcher, 'library'),
    join(launcher, 'instances'),
  ]
  for (const root of scanRoots) {
    for (const f of walkCredentialFiles(root)) {
      const t = readFileSync(f, 'utf8')
      if (/DEEPSEEK_API_KEY\s*:\s*\S+/.test(t) && !/DEEPSEEK_API_KEY\s*:\s*"REF:\s*"/.test(t))
        throw new Error(`play kit still has credentials.yaml: ${f}`)
    }
  }
}

const PLAY_ZIP_EXCLUDES = [
  '-xr!credentials.yaml',
  '-xr!.credentials.yaml',
  '-xr!pad-cli.log',
  '-xr!runtime.json',
  '-xr!jobs.json',
]

function sevenZipAdd(type: 'zip' | '7z', parent: string, folder: string, outPath: string): void {
  const r = spawnSync(
    SEVEN_ZIP,
    ['a', type === 'zip' ? '-tzip' : '-t7z', '-mx=1', '-mmt=on', '-ssw', '-snl', ...PLAY_ZIP_EXCLUDES, outPath, folder],
    { cwd: parent, encoding: 'utf8' },
  )
  if ((r.status !== 0 && r.status !== 1) || !existsSync(outPath)) {
    throw new Error(`7z ${type} failed: ${(r.stdout || '') + (r.stderr || '') || r.status}`)
  }
  if (r.status === 1)
    console.warn(`7z ${type} warnings (exit 1), archive kept: ${outPath}`)
}

/** Play kits are hundreds of thousands of tiny pnpm files. Windows tar zip deflates
 *  each one on a single thread. 7-Zip zip + threads finishes in minutes.
 *  File count over 65535 forces Zip64. Windows 压缩文件夹 then says the zip is 无效.
 *  Give people packPlay7z or a 7z.sfx exe, not Explorer-opened zip. */
export function packPlayZip(kitParent: string, folderName: string, zipPath: string): void {
  const parent = resolve(kitParent)
  assertPlayKitSafe(join(parent, folderName))
  if (existsSync(zipPath)) rmSync(zipPath, { force: true })
  const folder = folderName
  if (existsSync(SEVEN_ZIP)) {
    sevenZipAdd('zip', parent, folder, zipPath)
    return
  }
  const r = spawnSync(
    'tar',
    ['--format', 'zip', '--options', 'zip:compression-level=0', '-cf', zipPath, folder],
    { cwd: parent, encoding: 'utf8' },
  )
  if (r.status !== 0 || !existsSync(zipPath)) {
    throw new Error(`tar store-zip failed: ${(r.stdout || '') + (r.stderr || '') || r.status}`)
  }
}

/** 7z container. Windows Explorer will not open it; 7-Zip will. Same excludes as packPlayZip. */
export function packPlay7z(kitParent: string, folderName: string, sevenZPath: string): void {
  const parent = resolve(kitParent)
  assertPlayKitSafe(join(parent, folderName))
  if (existsSync(sevenZPath)) rmSync(sevenZPath, { force: true })
  if (!existsSync(SEVEN_ZIP)) throw new Error(`7-Zip missing: ${SEVEN_ZIP}`)
  sevenZipAdd('7z', parent, folderName, sevenZPath)
}

/** Prefix a .7z with 7z.sfx so double-click extracts without Explorer zip. */
export function attachSevenZipSfx(archive7z: string, exePath: string): void {
  const sfx = join(dirname(SEVEN_ZIP), '7z.sfx')
  if (!existsSync(sfx)) throw new Error(`7z.sfx missing: ${sfx}`)
  if (!existsSync(archive7z)) throw new Error(`7z archive missing: ${archive7z}`)
  const out = resolve(exePath)
  if (existsSync(out)) rmSync(out, { force: true })
  writeFileSync(out, Buffer.concat([readFileSync(sfx), readFileSync(archive7z)]))
}

function newestMtime(dir: string, exts: string[]): number {
  let max = 0
  const walk = (d: string): void => {
    if (!existsSync(d)) return
    for (const name of readdirSync(d)) {
      if (name === 'bin' || name === 'obj') continue
      const p = join(d, name)
      const st = statSync(p)
      if (st.isDirectory()) walk(p)
      else if (exts.some((e) => name.endsWith(e)) && st.mtimeMs > max) max = st.mtimeMs
    }
  }
  walk(dir)
  return max
}

/** PAD 是 WPF 原生窗，只有 dotnet publish 这一条路。解压包根目录只放 exe 和文件夹，运行时 dll 打进单文件。 */
export function compileHostExe(outFile: string): string {
  const dest = resolve(outFile)
  mkdirSync(dirname(dest), { recursive: true })
  if (!existsSync(PAD_PROJ)) throw new Error(`Pad.csproj not found: ${PAD_PROJ}`)
  mkdirSync(TMP, { recursive: true })
  const single = process.env.PAD_PUBLISH_SINGLE !== '0'
  const pubDir = join(TMP, single ? 'pad-win-x64-onefile' : 'pad-win-x64-fdd')
  mkdirSync(pubDir, { recursive: true })
  const args = [
    'publish',
    PAD_PROJ,
    '-c',
    'Release',
    '-r',
    'win-x64',
    '--self-contained',
    single ? 'true' : 'false',
    '-o',
    pubDir,
  ]
  if (single) {
    args.push(
      '-p:PublishSingleFile=true',
      '-p:IncludeNativeLibrariesForSelfExtract=true',
      '-p:DebugType=None',
      '-p:DebugSymbols=false',
    )
  }
  const r = spawnSync('dotnet', args, {
    encoding: 'utf8',
    env: {
      ...process.env,
      NUGET_PACKAGES: join(TMP, 'nuget'),
      DOTNET_CLI_HOME: join(TMP, 'dotnet-cli'),
      TMP,
      TEMP: TMP,
    },
  })
  const published = join(pubDir, HOST_EXE_NAME)
  if (r.status !== 0 || !existsSync(published)) {
    throw new Error(`dotnet publish failed: ${(r.stdout || '') + (r.stderr || '') || r.status}`)
  }
  copyFileSync(published, dest)
  return dest
}

export function initPortableKit(kitDir: string, opts: { repo?: string } = {}): PortableKit {
  const kit = resolve(kitDir)
  const repo = resolve(opts.repo || REPO_FROM_HERE)
  mkdirSync(kit, { recursive: true })
  const launcherRoot = join(kit, '.pack-launcher')
  ensureRoot(launcherRoot)
  writeFileSync(join(kit, '.pack-agent-repo'), repo + '\n')
  const openBat = join(kit, '打开.bat')
  writeFileSync(openBat, openBatBody({ repo }))
  const examplePack = join(kit, 'example-pack')
  writeExamplePack(examplePack)
  const exampleZip = join(kit, 'example.pack.zip')
  zipDir(examplePack, exampleZip)
  mkdirSync(CACHE_DIR, { recursive: true })
  const cacheExe = join(CACHE_DIR, HOST_EXE_NAME)
  const stale =
    !existsSync(cacheExe) || newestMtime(PAD_DIR, ['.cs', '.xaml', '.csproj']) > statSync(cacheExe).mtimeMs
  if (stale) compileHostExe(cacheExe)
  copyFileSync(cacheExe, join(kit, HOST_EXE_NAME))
  const hostExe = join(kit, HOST_EXE_NAME)
  return { kit, launcherRoot, openBat, hostExe, examplePack, exampleZip }
}
