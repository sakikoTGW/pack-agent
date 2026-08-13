#!/usr/bin/env bun
/**
 * Windows junction / symlink skill dirs must be scanned.
 * Bun Dirent: isSymbolicLink=true, isDirectory=false for junctions.
 */
import { join } from 'node:path'
import { mkdtemp, mkdir, writeFile, symlink, rm } from 'node:fs/promises'
import { platform } from 'node:os'
import { scanRuntime, getAdapter } from '../src/adapters.js'
import { packTestTmp } from './tmp-root.js'

const root = await mkdtemp(packTestTmp('pack-junction-'))
const skillsRoot = join(root, '.cursor', 'skills')
const real = join(root, '_real', 'junction-skill')
await mkdir(real, { recursive: true })
await writeFile(
  join(real, 'SKILL.md'),
  '---\nname: junction-skill\ndescription: probe\n---\n# ok\n',
)
await mkdir(skillsRoot, { recursive: true })

const link = join(skillsRoot, 'junction-skill')
try {
  // Prefer junction on Windows; symlink elsewhere
  if (platform() === 'win32') {
    const { execFileSync } = await import('node:child_process')
    execFileSync('cmd', ['/c', 'mklink', '/J', link, real], { stdio: 'ignore' })
  } else {
    await symlink(real, link, 'dir')
  }
} catch (e) {
  console.error('✗ could not create junction/symlink', e)
  await rm(root, { recursive: true, force: true })
  process.exit(1)
}

const adapter = getAdapter('cursor')!
const scan = await scanRuntime(root, adapter)
const hit = scan.skills.some(s => s.name === 'junction-skill')
await rm(root, { recursive: true, force: true })

if (!hit) {
  console.error('✗ junction skill not scanned')
  process.exit(1)
}
console.log('✓ junction/symlink skill dirs are scanned')
