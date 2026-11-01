/**
 * 打发布用的「解压即玩」安装包（kit zip）。
 *
 * 产物：dist/pack-agent-kit-<version>-win-x64.zip
 * 内容：PAD 单文件 exe + .pack-launcher + pack-runtime（bun + bundle + pack-index）
 *      + 打开.bat / 安装.bat / 使用说明.txt + 示例整合包
 *
 * 用法：
 *   bun run scripts/make-kit.ts [--out <dir>] [--stage <dir>]
 *   bun run kit                       (package.json script)
 */
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'
import { HOST_EXE_NAME, initPortableKit } from '../agent-pack-dsh/modpack/portable.js'

const REPO = resolve(import.meta.dirname, '..')
const TMP = process.env.AGENT_PACK_TMP || 'E:\\tmp\\pack-agent'
const SEVEN_ZIP = 'C:\\Program Files\\7-Zip\\7z.exe'

function arg(name: string, fallback: string): string {
  const i = process.argv.indexOf(`--${name}`)
  return i >= 0 && process.argv[i + 1] ? resolve(process.argv[i + 1]) : fallback
}

const pkg = JSON.parse(readFileSync(join(REPO, 'package.json'), 'utf8')) as { version?: string }
const version = pkg.version || '0.0.0'
const outDir = arg('out', join(REPO, 'dist'))
const stage = arg('stage', join(TMP, 'kit-stage'))
const zipPath = join(outDir, `pack-agent-kit-${version}-win-x64.zip`)

const installBat = `@echo off
chcp 65001 >nul
set "HERE=%~dp0"
if not exist "%HERE%${HOST_EXE_NAME}" (
  echo [x] 缺 ${HOST_EXE_NAME}，压缩包可能没解压完整。
  pause
  exit /b 1
)
if not exist "%HERE%pack-runtime\\bun.exe" (
  echo [!] 没找到 pack-runtime，整合包的安装/启停可能不可用。
  echo     请重新完整解压，或把 pack-runtime 目录放到本文件旁边。
)
echo 启动 PAD …
start "" "%HERE%${HOST_EXE_NAME}"
`

const readme = `PAD（pack-agent for DSH）解压即玩包 v${version}

1. 把整个文件夹解压到任意位置（不要只解压 exe）。
2. 双击「安装.bat」或「打开.bat」启动。
3. 实例数据在 .pack-launcher 目录里，跟着文件夹走。
4. pack-runtime 已内置（bun + packagent + pack-index），
   目标机器不需要安装 node/bun，也不需要 clone 仓库。

整合包：把 .pack.zip 拖进 PAD 窗口，或在「管理 → 整合包」里装。
`

function ensureBatAndReadme(dir: string): void {
  writeFileSync(join(dir, '安装.bat'), installBat.replace(/\n/g, '\r\n'), 'utf8')
  writeFileSync(join(dir, '使用说明.txt'), readme.replace(/\n/g, '\r\n'), 'utf8')
}

function zipDir(from: string, outFile: string): void {
  if (existsSync(outFile)) rmSync(outFile, { force: true })
  const seven = existsSync(SEVEN_ZIP) ? SEVEN_ZIP : null
  const r = seven
    ? spawnSync(seven, ['a', '-tzip', '-mx=5', outFile, '.\\*'], { cwd: from, encoding: 'utf8' })
    : spawnSync('tar', ['-a', '-cf', outFile, '-C', from, '.'], { encoding: 'utf8' })
  if (r.status !== 0 || !existsSync(outFile)) {
    throw new Error(`zip failed: ${(r.stdout || '') + (r.stderr || '') || r.status}`)
  }
}

console.log(`[kit] stage → ${stage}`)
rmSync(stage, { recursive: true, force: true })
mkdirSync(stage, { recursive: true })
const kit = initPortableKit(stage, { repo: REPO })
if (!existsSync(join(kit.kit, HOST_EXE_NAME))) throw new Error('kit missing host exe')
// 发布包不带开发机的仓库指针：目标机靠内置 pack-runtime 运行。
rmSync(join(kit.kit, '.pack-agent-repo'), { force: true })
if (!existsSync(join(kit.kit, 'pack-runtime', 'bun.exe'))) throw new Error('kit missing pack-runtime')
ensureBatAndReadme(kit.kit)

mkdirSync(dirname(zipPath), { recursive: true })
console.log(`[kit] zip → ${zipPath}`)
zipDir(kit.kit, zipPath)
const bytes = statSync(zipPath).size
console.log(
  JSON.stringify(
    { zip: zipPath, bytes, mb: Number((bytes / 1048576).toFixed(1)), version, stage: kit.kit },
    null,
    2,
  ),
)
