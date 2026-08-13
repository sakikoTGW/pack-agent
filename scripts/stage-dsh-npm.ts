#!/usr/bin/env bun
/**
 * 把 DSH 精简包摊到 E:\tmp\pack-agent\npm-pack-agent-dsh。
 * npm files 不能指到 dsh-plugin 的上一级，所以发布前必须 stage。
 */
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { packAgentRoot } from '../src/package-root.js'
import { packTmpRoot } from '../src/tmp-root.js'

export function stageDshNpmDest(): string {
  return join(packTmpRoot(), 'npm-pack-agent-dsh')
}

function copyRequired(from: string, to: string): void {
  if (!existsSync(from)) throw new Error(`stage missing ${from}`)
  mkdirSync(dirname(to), { recursive: true })
  cpSync(from, to, { recursive: true })
}

export function stageDshNpm(): string {
  const root = packAgentRoot()
  const pluginDir = join(root, 'dsh-plugin')
  const lib = join(pluginDir, 'lib', 'index.js')
  if (!existsSync(lib)) {
    throw new Error(`missing ${lib}; run bun run build:dsh first`)
  }

  const dest = stageDshNpmDest()
  rmSync(dest, { recursive: true, force: true })
  mkdirSync(dest, { recursive: true })

  const pluginPkg = JSON.parse(readFileSync(join(pluginDir, 'package.json'), 'utf8')) as {
    name?: string
    version?: string
    description?: string
    keywords?: string[]
    dsh?: unknown
  }
  const rootPkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as {
    author?: string
    bugs?: unknown
    repository?: unknown
  }

  copyRequired(join(pluginDir, 'lib'), join(dest, 'lib'))
  copyRequired(join(pluginDir, 'skills'), join(dest, 'skills'))
  copyRequired(join(pluginDir, 'cordis.patch.yml'), join(dest, 'cordis.patch.yml'))
  copyRequired(join(pluginDir, 'README.md'), join(dest, 'README.md'))
  copyRequired(join(pluginDir, 'README.zh.md'), join(dest, 'README.zh.md'))
  copyRequired(join(pluginDir, 'INSTALL.md'), join(dest, 'INSTALL.md'))
  copyRequired(join(root, 'LICENSE'), join(dest, 'LICENSE'))
  copyRequired(join(root, 'dsh-modpack', 'registry.yaml'), join(dest, 'dsh-modpack', 'registry.yaml'))
  copyRequired(join(root, 'crates', 'pack-index', 'Cargo.toml'), join(dest, 'crates', 'pack-index', 'Cargo.toml'))
  copyRequired(join(root, 'crates', 'pack-index', 'Cargo.lock'), join(dest, 'crates', 'pack-index', 'Cargo.lock'))
  copyRequired(join(root, 'crates', 'pack-index', 'src'), join(dest, 'crates', 'pack-index', 'src'))

  const stagedPkg = {
    name: '@sakikotgw/pack-agent-dsh',
    version: pluginPkg.version,
    description: pluginPkg.description,
    type: 'module',
    main: './lib/index.js',
    exports: { '.': './lib/index.js' },
    files: [
      'lib',
      'skills',
      'cordis.patch.yml',
      'dsh-modpack',
      'crates',
      'README.md',
      'README.zh.md',
      'INSTALL.md',
      'LICENSE',
    ],
    license: 'MIT',
    author: rootPkg.author,
    homepage: 'https://github.com/sakikoTGW/pack-agent/blob/main/dsh-plugin/README.md',
    bugs: rootPkg.bugs,
    repository: rootPkg.repository,
    keywords: pluginPkg.keywords,
    dsh: pluginPkg.dsh,
    engines: { node: '>=20.11.0' },
    publishConfig: { access: 'public' },
  }
  writeFileSync(join(dest, 'package.json'), `${JSON.stringify(stagedPkg, null, 2)}\n`)
  return dest
}

if (import.meta.main) {
  console.log(stageDshNpm())
}
