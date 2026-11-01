import { spawnSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { strict as assert } from 'node:assert'

const exe = process.env.PAD_EXE ?? join(import.meta.dirname, '../pad/bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
const root = mkdtempSync(join(tmpdir(), 'pad-headless-test-'))
const instance = 'demo', home = join(root, 'instances', instance, 'home'), workspace = join(root, 'workspace')
const fakeBin = join(root, 'versions', 'test', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'bin.js')
const key = 'fake-secret-123456'
for (const dir of [home, workspace, join(root, 'library'), join(root, 'instances', instance), join(fakeBin, '..')]) mkdirSync(dir, { recursive: true })
writeFileSync(join(root, 'instances', instance, 'instance.json'), JSON.stringify({ id: instance, name: 'demo', home, workspace: {kind:'shared',path:workspace}, dsh:{version:'test'} }))
writeFileSync(join(root, 'library', 'credentials.yaml'), `DEEPSEEK_API_KEY: "REF: ${key}"\n`)
writeFileSync(fakeBin, `let input=''; process.stdin.setEncoding('utf8'); process.stdin.on('data',s=>input+=s); process.stdin.on('end',()=>{ if(input==='timeout') return setTimeout(()=>{},10000); if(input==='fail'){console.error('failed '+process.env.DEEPSEEK_API_KEY);process.exitCode=9;return} const a=process.argv.slice(2),patch=require('fs').readFileSync(a[a.indexOf('--patch')+1],'utf8'); console.error('internal diagnostic'); console.log(JSON.stringify({input,keyValid:process.env.DEEPSEEK_API_KEY==='${key}',cwd:process.cwd(),args:a,patch})) })`)

function run(input: string, timeout = 5) {
  const inFile = join(root, 'input.txt'), outFile = join(root, 'output.json')
  writeFileSync(inFile, input)
  if (existsSync(outFile)) rmSync(outFile)
  const p = spawnSync(exe, ['cli','task','run',instance,'--input',inFile,'--out',outFile,'--timeout',String(timeout)], {env:{...process.env,PACK_LAUNCHER_ROOT:root},encoding:'utf8',timeout:15000})
  assert.ok(existsSync(outFile), `missing JSON result; process exit ${p.status}`)
  return {status:p.status, raw:readFileSync(outFile,'utf8'), result:JSON.parse(readFileSync(outFile,'utf8'))}
}
try {
  const ok = run('中文任务')
  assert.equal(ok.status,0); assert.equal(ok.result.status,'success'); assert.equal(ok.result.error,'')
  const data = JSON.parse(ok.result.output.trim())
  assert.equal(data.input,'中文任务'); assert.equal(data.keyValid,true); assert.equal(data.cwd,workspace)
  assert.ok(data.args.includes('headless'))
  assert.match(data.patch, /provider: deepseek-official/); assert.match(data.patch, /model: deepseek-flash/)
  const failed = run('fail'); assert.notEqual(failed.status,0); assert.equal(failed.result.status,'failed'); assert.equal(failed.result.exitCode,9)
  assert.ok(!failed.raw.includes(key),'credential leaked in JSON')
  const expired = run('timeout',1); assert.notEqual(expired.status,0); assert.equal(expired.result.status,'timeout')
  assert.ok(!readFileSync(join(root,'pad-cli.log'),'utf8').includes(key),'credential leaked in log')
  assert.ok(!readFileSync(join(root,'pad-cli.log'),'utf8').includes('中文任务'),'prompt leaked in log')
  writeFileSync(join(root,'library','credentials.yaml'),'')
  const missing = run('missing'); assert.notEqual(missing.status,0); assert.equal(missing.result.status,'failed')
  assert.match(missing.result.error,/not configured/)
  const samePath = join(root,'same.txt'); writeFileSync(samePath,'preserve me')
  const same = spawnSync(exe,['cli','task','run',instance,'--input',samePath,'--out',samePath],{env:{...process.env,PACK_LAUNCHER_ROOT:root},timeout:15000})
  assert.notEqual(same.status,0); assert.equal(readFileSync(samePath,'utf8'),'preserve me')
  console.log('PASS headless CLI: UTF-8, credential, workspace, failure, timeout, redaction')
} finally { rmSync(root,{recursive:true,force:true}) }
