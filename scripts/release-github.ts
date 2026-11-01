/**
 * 一句话发版：bump 版本 → 构建单文件 exe → 提交推送 → GitHub Release（含 latest.json）。
 *
 * 用法：
 *   bun run release:github -- --bump patch --notes "这一版改了什么"
 *   bun run release:github -- --version 0.4.6 --notes-file notes.txt
 *   bun run release:github -- --bump patch --notes "…" --dry-run   # 只打印计划
 *
 * 前置：gh 已登录（token 有 repo 权限）；工作区改动会被一起提交（发布提交）。
 */
import { readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'

const REPO_DIR = resolve(import.meta.dirname, '..')
const GH_REPO = arg('repo', 'sakikoTGW/pack-agent')
const BASE_URL = `https://github.com/${GH_REPO}/releases/latest/download`

function arg(name: string, fallback: string): string {
  const i = process.argv.indexOf(`--${name}`)
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : fallback
}
function flag(name: string): boolean {
  return process.argv.includes(`--${name}`)
}
function run(cmd: string, args: string[], cwd = REPO_DIR, inherit = false): string {
  console.log(`\n$ ${cmd} ${args.join(' ')}`)
  const r = spawnSync(cmd, args, {
    cwd,
    encoding: 'utf8',
    stdio: inherit ? 'inherit' : ['ignore', 'pipe', 'pipe'],
  })
  if (r.status !== 0) throw new Error(`${cmd} failed (${r.status})\n${r.stdout || ''}\n${r.stderr || ''}`)
  return r.stdout || ''
}

const pkgPath = join(REPO_DIR, 'package.json')
const pkg = JSON.parse(readFileSync(pkgPath, 'utf8')) as { version: string }
const dry = flag('dry-run')

let version = arg('version', '')
if (!version) {
  const bump = arg('bump', '')
  if (bump === 'patch' || bump === 'minor' || bump === 'major') {
    const [a, b, c] = pkg.version.split('.').map(Number)
    version = bump === 'patch' ? `${a}.${b}.${c + 1}` : bump === 'minor' ? `${a}.${b + 1}.0` : `${a + 1}.0.0`
  }
}
if (!version) throw new Error('需要 --version x.y.z 或 --bump patch|minor|major')
if (!/^\d+\.\d+\.\d+$/.test(version)) throw new Error(`版本号格式不对：${version}`)

const tag = `v${version}`
const notesFile = arg('notes-file', '')
const notesInline = arg('notes', '')
if (!notesFile && !notesInline) throw new Error('需要 --notes "…" 或 --notes-file <file>')

const releaseDir = join(REPO_DIR, 'dist', 'release', version)
const exe = join(releaseDir, 'pack-agent-for-DSH.exe')
const feed = join(releaseDir, 'latest.json')
const sums = join(releaseDir, 'SHA256SUMS.txt')
const notesPath = notesFile ? resolve(notesFile) : join(releaseDir, 'notes.md')

console.log(JSON.stringify({ repo: GH_REPO, version, tag, baseUrl: BASE_URL, dry }, null, 2))
if (dry) {
  console.log('[dry-run] 会执行：build-release → 提交版本号 → push → gh release create')
  process.exit(0)
}

// 0) 已有同名 release 就不动
const existing = spawnSync('gh', ['release', 'view', tag, '--repo', GH_REPO], { encoding: 'utf8' })
if (existing.status === 0) throw new Error(`${GH_REPO} 已有 release ${tag}，换个版本号`)

// 1) 版本号写进 package.json（PAD 的 assembly 版本由 build-release 的 -p:Version 决定）
pkg.version = version
writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n')

// 2) 构建发布包（dotnet publish + 单文件 payload + latest.json 自检）
run('bun', ['run', join(REPO_DIR, 'scripts', 'build-release.ts'), '--version', version, '--base-url', BASE_URL])

// 3) 提交 + 推 main
run('git', ['add', 'package.json'])
run('git', ['-c', 'core.autocrlf=false', 'commit', '-m', `release: PAD v${version}`])
run('git', ['push', 'origin', 'main'])
run('git', ['tag', '-f', tag])
run('git', ['push', 'origin', tag, '--force'])

// 4) GitHub Release + 资产
const assets = [exe, feed, sums, ...(notesFile ? [notesPath] : [])]
run(
  'gh',
  ['release', 'create', tag, '--repo', GH_REPO, '--target', 'main', '--title', `PAD v${version}`,
    '--notes-file', notesPath, '--latest', ...assets],
  REPO_DIR,
  true,
)

// 5) 验证：线上 feed 的 version 必须等于这次版本
const url = `${BASE_URL}/latest.json`
const check = spawnSync('gh', ['api', `repos/${GH_REPO}/releases/latest`], { encoding: 'utf8' })
if (check.status !== 0) throw new Error(`release 验证失败：${check.stderr}`)
const latest = JSON.parse(check.stdout) as { tag_name: string }
if (latest.tag_name !== tag) throw new Error(`latest 指向 ${latest.tag_name}，不是 ${tag}`)

console.log(
  JSON.stringify(
    {
      ok: true,
      version,
      tag,
      release: `https://github.com/${GH_REPO}/releases/tag/${tag}`,
      download: `${BASE_URL}/pack-agent-for-DSH.exe`,
      feed: url,
    },
    null,
    2,
  ),
)
