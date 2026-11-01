/**
 * PAD 发布包：单文件 exe + latest.json + 说明，可直接扔到任何静态托管。
 *
 * 产物：dist/release/<version>/
 *   pack-agent-for DSH.exe      ← 固定名（feed 指向它；用户下载这一个）
 *   latest.json                 ← PAD 自动更新读的 feed
 *   notes.md                    ← 版本说明
 *   SHA256SUMS.txt
 *   README.txt                  ← 怎么上传/怎么填 feed
 *
 * 用法：
 *   bun run release -- --version 0.4.5 --base-url https://example.com/pad
 *   bun run release -- --version 0.4.5            # base-url 留空则写占位符，上传后再填
 *   bun run release -- --skip-publish             # 复用已发布的 host（本地快速迭代）
 */
import { createHash } from 'node:crypto'
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'

const REPO = resolve(import.meta.dirname, '..')
const TMP = process.env.AGENT_PACK_TMP || 'E:\\tmp\\pack-agent'
const HOST_EXE = 'pack-agent-for DSH.exe'

function arg(name: string, fallback: string): string {
  const i = process.argv.indexOf(`--${name}`)
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : fallback
}
function flag(name: string): boolean {
  return process.argv.includes(`--${name}`)
}

const pkg = JSON.parse(readFileSync(join(REPO, 'package.json'), 'utf8')) as { version?: string }
const version = arg('version', pkg.version || '0.0.0')
const baseUrl = arg('base-url', '').replace(/\/+$/, '')
const outRoot = resolve(arg('out', join(REPO, 'dist', 'release')))
const releaseDir = join(outRoot, version)
const hostDir = join(TMP, `pad-release-host-${version}`)
const singleDir = join(TMP, `pad-single-${version}`)

function run(cmd: string, args: string[], cwd = REPO): void {
  console.log(`[release] ${cmd} ${args.join(' ')}`)
  const r = spawnSync(cmd, args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] })
  if (r.status !== 0) throw new Error(`${cmd} failed (${r.status}): ${(r.stdout || '').slice(-2000)}`)
}

// 1) 发布 host（带上版本号，PAD 自报的版本就是它）
if (!flag('skip-publish')) {
  rmSync(hostDir, { recursive: true, force: true })
  mkdirSync(hostDir, { recursive: true })
  run('dotnet', [
    'publish', join(REPO, 'agent-pack-dsh', 'pad', 'Pad.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-o', hostDir,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false',
    `-p:Version=${version}`,
  ])
}
const host = join(hostDir, HOST_EXE)
if (!existsSync(host)) throw new Error(`host exe not found: ${host}`)

// 2) PAD 单文件（host + pack-runtime payload）
rmSync(singleDir, { recursive: true, force: true })
run('bun', ['run', join(REPO, 'scripts', 'make-single-exe.ts'), '--host', host, '--out-dir', singleDir])
const single = join(singleDir, HOST_EXE)
if (!existsSync(single)) throw new Error(`single exe not found: ${single}`)

// 3) 发布目录
rmSync(releaseDir, { recursive: true, force: true })
mkdirSync(releaseDir, { recursive: true })
const exeOut = join(releaseDir, HOST_EXE)
copyFileSync(single, exeOut)
const bytes = readFileSync(exeOut)
const sha256 = createHash('sha256').update(bytes).digest('hex')
const size = statSync(exeOut).size
const feedUrl = baseUrl ? `${baseUrl}/pack-agent-for%20DSH.exe` : 'https://REPLACE-ME/pack-agent-for%20DSH.exe'
const createdAt = new Date().toISOString()

const notes = `# PAD ${version}\n\n- 构建：${createdAt}\n- 文件：${HOST_EXE}（${(size / 1048576).toFixed(1)} MB，单文件，内置 pack-runtime）\n- sha256：${sha256}\n`
const latest = {
  schema: 'pack-agent.pad-update/v1',
  version,
  url: feedUrl,
  sha256,
  size,
  notes: `PAD ${version}`,
}
writeFileSync(join(releaseDir, 'latest.json'), JSON.stringify(latest, null, 2) + '\n')
writeFileSync(join(releaseDir, 'notes.md'), notes)
writeFileSync(join(releaseDir, 'SHA256SUMS.txt'), `${sha256}  ${HOST_EXE}\n`)
writeFileSync(
  join(releaseDir, 'README.txt'),
  [
    `PAD ${version} 发布包`,
    '',
    '上传：把本目录整个传到任意静态托管（GitHub Releases / 对象存储 / 内网 HTTP 都行）。',
    'feed 地址 = <你上传的目录>/latest.json（填到 PAD 设置的「更新源」，或发布时用 --base-url 写进 latest.json）。',
    baseUrl ? `本次 base-url：${baseUrl}` : '本次没给 --base-url：latest.json 里的 url 是占位符，上传后手动改成本机可访问地址。',
    '',
    '用户侧：下载 pack-agent-for DSH.exe 双击即可；新版由 PAD 自己检查/下载/下次启动替换。',
    '校验：SHA256SUMS.txt',
    '',
  ].join('\n'),
)

// 4) 自检：feed 里的 hash/size 与 exe 一致
const check = JSON.parse(readFileSync(join(releaseDir, 'latest.json'), 'utf8')) as {
  version: string; sha256: string; size: number
}
if (check.version !== version || check.sha256 !== sha256 || check.size !== size)
  throw new Error('latest.json 自检失败')

console.log(
  JSON.stringify(
    {
      releaseDir,
      version,
      exeMB: Number((size / 1048576).toFixed(1)),
      feed: join(releaseDir, 'latest.json'),
      feedUrl: baseUrl ? `${baseUrl}/latest.json` : '(fill base-url before upload)',
      sha256,
    },
    null,
    2,
  ),
)
