/**
 * PCL 式单文件分发产物：一个 exe 带全部东西。
 *
 * = 已发布的单文件 PAD exe
 * + 末尾追加 pack-runtime.zip（bun + packagent bundle + pack-index + 包根资源）
 * + 32 字节 footer（"PADPACKRT1" + 长度 + 偏移）
 *
 * PAD 首次用到整合包功能时自己把 payload 解压到 exe 旁的 .pack-runtime/，
 * 不需要安装器、不需要 node/bun、数据（.pack-launcher/.pack-runtime）都跟着 exe 走。
 *
 * 用法：
 *   bun run scripts/make-single-exe.ts [--host <单文件 exe>] [--out-dir dist/single]
 */
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { stagePackRuntime } from '../agent-pack-dsh/modpack/portable.js'

const REPO = resolve(import.meta.dirname, '..')
const TMP = process.env.AGENT_PACK_TMP || 'E:\\tmp\\pack-agent'
const SEVEN_ZIP = 'C:\\Program Files\\7-Zip\\7z.exe'
const MAGIC = 'PADPACKRT1'
const FOOTER = 40

function arg(name: string, fallback: string): string {
  const i = process.argv.indexOf(`--${name}`)
  return i >= 0 && process.argv[i + 1] ? resolve(process.argv[i + 1]) : fallback
}

const hostExe = arg('host', 'E:\\tmp\\pad-win-x64-onefile\\pack-agent-for DSH.exe')
const outDir = arg('out-dir', join(REPO, 'dist', 'single'))
const outExe = join(outDir, 'pack-agent-for DSH.exe')

if (!existsSync(hostExe)) throw new Error(`host exe not found: ${hostExe} (先 dotnet publish -p:PublishSingleFile=true)`)

const stage = join(TMP, 'single-runtime')
rmSync(stage, { recursive: true, force: true })
mkdirSync(stage, { recursive: true })
console.log(`[single] stage runtime → ${stage}`)
stagePackRuntime(stage, REPO)

const zipPath = join(TMP, 'pack-runtime.payload.zip')
if (existsSync(zipPath)) rmSync(zipPath, { force: true })
const seven = existsSync(SEVEN_ZIP) ? SEVEN_ZIP : null
const zr = seven
  ? spawnSync(seven, ['a', '-tzip', '-mx=5', zipPath, '.\\*'], { cwd: stage, encoding: 'utf8' })
  : spawnSync('tar', ['-a', '-cf', zipPath, '-C', stage, '.'], { encoding: 'utf8' })
if (zr.status !== 0 || !existsSync(zipPath)) throw new Error(`zip runtime failed: ${(zr.stdout || '') + (zr.stderr || '')}`)

const host = readFileSync(hostExe)
const payload = readFileSync(zipPath)
const hash = createHash('sha256').update(payload).digest().subarray(0, 8)
const footer = Buffer.alloc(FOOTER)
footer.write(MAGIC, 0, 'ascii')
footer.writeBigUInt64LE(BigInt(payload.length), MAGIC.length)
footer.writeBigUInt64LE(BigInt(host.length), MAGIC.length + 8)
hash.copy(footer, MAGIC.length + 16)

mkdirSync(dirname(outExe), { recursive: true })
writeFileSync(outExe, Buffer.concat([host, payload, footer]))

// 回读校验 footer
const back = readFileSync(outExe)
const tail = back.subarray(back.length - FOOTER)
if (tail.subarray(0, MAGIC.length).toString('ascii') !== MAGIC) throw new Error('footer magic 写坏')
const len = Number(tail.readBigUInt64LE(MAGIC.length))
const off = Number(tail.readBigUInt64LE(MAGIC.length + 8))
if (off + len + FOOTER !== back.length) throw new Error(`footer 不一致 off=${off} len=${len} size=${back.length}`)

const mb = (n: number) => Number((n / 1048576).toFixed(1))
console.log(
  JSON.stringify(
    {
      exe: outExe,
      mb: mb(statSync(outExe).size),
      hostMB: mb(host.length),
      runtimeZipMB: mb(payload.length),
      payloadOffset: off,
    },
    null,
    2,
  ),
)
