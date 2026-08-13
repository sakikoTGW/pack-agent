#!/usr/bin/env bun
/**
 * 编译 crates/pack-index（Windows 走 VS Build Tools vcvars64）。
 * 产物写到 E:\tmp\pack-agent\pack-index-target，禁止落到 C:\Users\...\Temp。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTmpRoot } from '../src/tmp-root.js'

export function packIndexTargetDir(): string {
  return process.env.CARGO_TARGET_DIR?.trim() || join(packTmpRoot(), 'pack-index-target')
}

export function packIndexBinPath(): string {
  if (process.env.PACK_INDEX_BIN?.trim()) return process.env.PACK_INDEX_BIN.trim()
  const name = process.platform === 'win32' ? 'pack-index.exe' : 'pack-index'
  return join(packIndexTargetDir(), 'release', name)
}

function findVcvars64(): string | undefined {
  const pf86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)'
  const vswhere = join(pf86, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe')
  if (!existsSync(vswhere)) return undefined
  const r = spawnSync(
    vswhere,
    [
      '-latest',
      '-products',
      '*',
      '-requires',
      'Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
      '-find',
      '**/vcvars64.bat',
    ],
    { encoding: 'utf8' },
  )
  return (r.stdout || '')
    .split(/\r?\n/)
    .map(s => s.trim())
    .find(s => s.toLowerCase().endsWith('vcvars64.bat'))
}

export function buildPackIndex(): string {
  const bin = packIndexBinPath()
  if (existsSync(bin) && !process.env.PACK_INDEX_REBUILD) return bin

  const repoRoot = join(import.meta.dirname, '..')
  const manifest = join(repoRoot, 'crates', 'pack-index', 'Cargo.toml')
  const target = packIndexTargetDir()
  mkdirSync(target, { recursive: true })
  const tmp = packTmpRoot()
  const env = {
    ...process.env,
    CARGO_TARGET_DIR: target,
    TMP: tmp,
    TEMP: tmp,
    TMPDIR: tmp,
  }
  const spawnOpts = {
    encoding: 'utf8' as const,
    env,
    cwd: repoRoot,
    maxBuffer: 64 * 1024 * 1024,
    timeout: 10 * 60 * 1000,
  }

  let r
  if (process.platform === 'win32') {
    const vc = findVcvars64()
    if (!vc) {
      throw new Error('未找到 MSVC vcvars64.bat，装 VS Build Tools 的 C++ 工作负载后再编 pack-index')
    }
    const bat = join(tmp, 'build-pack-index.bat')
    const body = [
      '@echo off',
      `call "${vc}"`,
      `if errorlevel 1 exit /b 1`,
      `cargo build --release --manifest-path "${manifest}" --target-dir "${target}"`,
      'exit /b %ERRORLEVEL%',
      '',
    ].join('\r\n')
    writeFileSync(bat, body, 'utf8')
    r = spawnSync('cmd.exe', ['/c', bat], spawnOpts)
  } else {
    r = spawnSync(
      'cargo',
      ['build', '--release', '--manifest-path', manifest, '--target-dir', target],
      spawnOpts,
    )
  }
  if (r.status !== 0) {
    throw new Error(`cargo build pack-index failed:\n${r.stderr || ''}\n${r.stdout || ''}`)
  }
  if (!existsSync(bin)) {
    throw new Error(`pack-index binary missing at ${bin}`)
  }
  return bin
}

export function ensurePackIndexBin(): string {
  const bin = packIndexBinPath()
  if (existsSync(bin) && !process.env.PACK_INDEX_REBUILD) return bin
  return buildPackIndex()
}

if (import.meta.main) {
  const bin = buildPackIndex()
  console.log(bin)
}
