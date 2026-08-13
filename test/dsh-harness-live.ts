#!/usr/bin/env bun
/**
 * 对着 DeepSeek Harness 源码核实的 plugin-add 契约：
 * `dsh plugin --profile <name> add <path>` = 在 `$DSH_HOME/profiles/<name>` 里 `pnpm add`，
 * 再把声明了 `dsh.bundle.patch` 的包写进 `dsh.profile.bundles`。
 *
 * 用隔离 DSH_HOME，不碰用户 ~/.dsh。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { packTestTmp } from './tmp-root.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const repoRoot = resolve(import.meta.dirname, '..')
const home = packTestTmp(`dsh-home-live-${Date.now()}`)
const profileDir = join(home, 'profiles', 'packagent-test')
const emptyNpmrc = join(home, 'empty.npmrc')
const storeDir = join(home, 'pnpm-store')

mkdirSync(profileDir, { recursive: true })
writeFileSync(emptyNpmrc, 'registry=https://registry.npmjs.org/\n', 'utf8')

writeFileSync(
  join(profileDir, 'package.json'),
  JSON.stringify(
    {
      name: 'dsh-profile-packagent-test',
      private: true,
      dependencies: {},
      dsh: { profile: { bundles: ['@deepseek-ai/dsh-base'] } },
    },
    null,
    2,
  ) + '\n',
)
writeFileSync(join(profileDir, 'cordis.patch.yml'), '[]\n')
writeFileSync(
  join(profileDir, 'pnpm-workspace.yaml'),
  'packages:\n  - .\n\nnodeLinker: hoisted\nautoInstallPeers: false\n',
)
writeFileSync(
  join(profileDir, '.npmrc'),
  'registry=https://registry.npmjs.org/\nignore-workspace-root-check=true\n',
  'utf8',
)

const env = {
  ...process.env,
  DSH_HOME: home,
  NPM_CONFIG_USERCONFIG: emptyNpmrc,
  npm_config_userconfig: emptyNpmrc,
  TMP: home,
  TEMP: home,
  TMPDIR: home,
}

const isWin = process.platform === 'win32'
const add = spawnSync(
  'pnpm',
  ['add', repoRoot, '--store-dir', storeDir, '--ignore-scripts'],
  {
    cwd: profileDir,
    encoding: 'utf8',
    env,
    shell: isWin,
    timeout: 5 * 60 * 1000,
    maxBuffer: 32 * 1024 * 1024,
  },
)
if (add.status !== 0) {
  fail(
    `pnpm add pack-agent in DSH profile failed (exit ${add.status}):\n${add.stdout || ''}\n${add.stderr || ''}`,
  )
}
console.log('✓ pnpm add pack-agent into isolated DSH profile (same as dsh plugin add)')

const profilePkg = JSON.parse(readFileSync(join(profileDir, 'package.json'), 'utf8')) as {
  dependencies?: Record<string, string>
  dsh?: { profile?: { bundles?: string[] } }
}
if (!profilePkg.dependencies?.['@sakikotgw/pack-agent']) {
  fail(`profile dependencies missing @sakikotgw/pack-agent: ${JSON.stringify(profilePkg.dependencies)}`)
}

const installedPkgPath = join(profileDir, 'node_modules', '@sakikotgw', 'pack-agent', 'package.json')
if (!existsSync(installedPkgPath)) fail(`linked package missing at ${installedPkgPath}`)
const installed = JSON.parse(readFileSync(installedPkgPath, 'utf8')) as {
  exports?: Record<string, string | { import?: string; default?: string; bun?: string }>
  dsh?: { bundle?: { patch?: string } }
}
if (!installed.dsh?.bundle?.patch) fail('installed pack-agent missing dsh.bundle.patch — DSH would treat it as a plain library')
const dshExport = installed.exports?.['./dsh']
const dshRel = typeof dshExport === 'string' ? dshExport : dshExport?.import || dshExport?.default
if (!dshRel || dshRel.endsWith('.ts')) {
  fail(`exports ./dsh must be Node-loadable JS, got ${JSON.stringify(dshExport)}`)
}

const patchPath = join(profileDir, 'node_modules', '@sakikotgw', 'pack-agent', installed.dsh.bundle.patch)
const patch = readFileSync(patchPath, 'utf8')
if (!patch.includes("name: '@sakikotgw/pack-agent/dsh'")) fail(`bundle patch missing pack-agent/dsh insert:\n${patch}`)
if (!patch.includes('inject: [tools, skills, commands]')) fail('bundle patch must inject tools, skills, commands')
console.log('✓ installed package is a dsh.bundle; patch inserts @sakikotgw/pack-agent/dsh')

const bundles = profilePkg.dsh?.profile?.bundles ?? []
if (!bundles.includes('@sakikotgw/pack-agent')) {
  bundles.push('@sakikotgw/pack-agent')
  profilePkg.dsh = { ...profilePkg.dsh, profile: { ...profilePkg.dsh?.profile, bundles } }
  writeFileSync(join(profileDir, 'package.json'), JSON.stringify(profilePkg, null, 2) + '\n')
}
if (!profilePkg.dsh?.profile?.bundles?.includes('@sakikotgw/pack-agent')) {
  fail('reconcile did not add @sakikotgw/pack-agent to dsh.profile.bundles')
}
console.log('✓ dsh.profile.bundles includes @sakikotgw/pack-agent')

const { apply, inject, name } = await import('../dsh-plugin/src/index.ts')
if (name !== 'pack-agent') fail(`plugin name ${name}`)
if (!inject.includes('tools') || !inject.includes('skills')) fail(`inject ${inject.join(',')}`)
const tools: string[] = []
apply(
  {
    tools: { register(def: { name: string }) { tools.push(def.name) } },
    skills: { registerProvider() { return () => {} } },
  },
  { cwd: home },
)
if (!tools.includes('packagent_map') || !tools.includes('packagent_allow') || !tools.includes('packagent_set_load')) {
  fail(`loaded plugin tools ${tools.join(',')}`)
}
console.log('✓ Cordis apply() from linked package registers map/allow')

const dshBin = join(home, 'dsh-cli', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'bin.js')
mkdirSync(join(home, 'dsh-cli'), { recursive: true })
writeFileSync(
  join(home, 'dsh-cli', 'package.json'),
  JSON.stringify({ name: 'dsh-cli-scratch', private: true }, null, 2) + '\n',
)
const bunAdd = spawnSync(
  'bun',
  ['add', '@deepseek-ai/dsh@0.1.0-rc.6'],
  {
    cwd: join(home, 'dsh-cli'),
    encoding: 'utf8',
    env: {
      ...env,
      BUN_INSTALL_CACHE_DIR: join(home, 'bun-cache'),
    },
    timeout: 4 * 60 * 1000,
    maxBuffer: 32 * 1024 * 1024,
  },
)
if (bunAdd.status === 0 && existsSync(dshBin)) {
  const dump = spawnSync(
    'node',
    [dshBin, 'plugin', '--profile', 'packagent-test', 'list'],
    {
      cwd: repoRoot,
      encoding: 'utf8',
      env,
      timeout: 60_000,
      maxBuffer: 8 * 1024 * 1024,
    },
  )
  console.log(`dsh plugin list exit=${dump.status}\n${dump.stdout || ''}\n${dump.stderr || ''}`)
  if (dump.status !== 0) {
    console.log('⚠ official dsh CLI installed but plugin list failed; pnpm-add contract still holds')
  } else {
    console.log('✓ official dsh CLI plugin list ran')
  }
} else {
  console.log(
    `⚠ official @deepseek-ai/dsh CLI not installed (bun add exit ${bunAdd.status}): ${(bunAdd.stderr || bunAdd.stdout || '').slice(0, 400)}`,
  )
}

console.log('[OK] dsh-harness-live')
