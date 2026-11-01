import { spawn, spawnSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { strict as assert } from 'node:assert'
const exe = process.env.PAD_EXE ?? join(import.meta.dirname, '../pad/bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
const root=mkdtempSync(join(tmpdir(),'pad-queue-test-')), home=join(root,'home'), workspace=join(root,'work'), lib=join(root,'library'), inst=join(root,'instances','demo')
const bin=join(root,'versions','test','node_modules','@deepseek-ai','dsh','lib','bin.js')
for(const dir of [home,workspace,lib,inst,join(bin,'..')]) mkdirSync(dir,{recursive:true})
writeFileSync(join(inst,'instance.json'),JSON.stringify({id:'demo',name:'demo',home,workspace:{kind:'shared',path:workspace},dsh:{version:'test'}}))
writeFileSync(join(lib,'credentials.yaml'),'DEEPSEEK_API_KEY: "REF: dummy-test-key"\n')
writeFileSync(bin,`let s='';process.stdin.setEncoding('utf8');process.stdin.on('data',v=>s+=v);process.stdin.on('end',()=>{setTimeout(()=>console.log('reply:'+s),s==='slow'?3000:0)})`)
const input=join(root,'prompt.txt'),out=join(root,'output.json');writeFileSync(input,'测试队列')
function cmd(...args:string[]) { const p=spawnSync(exe,['cli','queue',...args],{env:{...process.env,PACK_LAUNCHER_ROOT:root},timeout:30000,encoding:'utf8'}); return p.status }
function result(){assert.ok(existsSync(out),'queue command did not write output');return JSON.parse(readFileSync(out,'utf8'))}
try {
 assert.equal(cmd('submit','demo','--input',input,'--out',out),0);const receipt=result();assert.equal(receipt.status,'queued');assert.ok(receipt.id)
 assert.equal(cmd('status',receipt.id,'--out',out),0);assert.equal(result().status,'queued')
 assert.equal(cmd('worker','--once'),0)
 assert.equal(cmd('status',receipt.id,'--out',out),0);const complete=result();assert.equal(complete.status,'success');assert.equal(complete.result.output.trim(),'reply:测试队列')
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',receipt.id,'--out',out),0);assert.equal(result().attempts,1)
 assert.equal(cmd('submit','demo','--input',input,'--out',out),0);const id=result().id
 assert.equal(cmd('cancel',id,'--out',out),0);assert.equal(result().status,'cancelled');assert.equal(cmd('worker','--once'),0)
 assert.equal(cmd('status',id,'--out',out),0);assert.equal(result().status,'cancelled')
 assert.equal(cmd('list','--out',out),0);assert.equal(result().length,2)
 assert.equal(cmd('submit','demo','--input',input,'--out',out),0);const crashId=result().id
 const jobFile=join(root,'headless-queue',crashId,'job.json');const job=JSON.parse(readFileSync(jobFile,'utf8'));job.status='running';job.attempts=1;writeFileSync(jobFile,JSON.stringify(job))
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',crashId,'--out',out),0);assert.equal(result().status,'interrupted')
 assert.equal(cmd('retry',crashId,'--out',out),0);assert.equal(result().status,'queued');assert.equal(cmd('worker','--once'),0)
 assert.equal(cmd('status',crashId,'--out',out),0);assert.equal(result().status,'success');assert.equal(result().attempts,2)
 assert.equal(cmd('submit','demo','--input',input,'--out',out),0);const recoveredId=result().id
 const recoveredDir=join(root,'headless-queue',recoveredId),recoveredFile=join(recoveredDir,'job.json'),recovered=JSON.parse(readFileSync(recoveredFile,'utf8'))
 recovered.status='running';recovered.attempts=1;writeFileSync(recoveredFile,JSON.stringify(recovered));writeFileSync(join(recoveredDir,'result-1.json'),JSON.stringify({status:'success',exitCode:0,output:'finished before crash',error:''}))
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',recoveredId,'--out',out),0);assert.equal(result().status,'success');assert.equal(result().result.output,'finished before crash');assert.equal(result().attempts,1)
 writeFileSync(input,'slow');assert.equal(cmd('submit','demo','--input',input,'--out',out),0);const concurrentId=result().id
 const env={...process.env,PACK_LAUNCHER_ROOT:root};const worker1=spawn(exe,['cli','queue','worker','--once'],{env,stdio:'ignore'})
 await new Promise(r=>setTimeout(r,1500))
 const worker2=spawn(exe,['cli','queue','worker','--once'],{env,stdio:'ignore'})
 const exit=(p:typeof worker1)=>new Promise<number|null>(resolve=>p.on('exit',resolve))
 const exits=await Promise.all([exit(worker1),exit(worker2)])
 assert.ok(exits.includes(0));assert.ok(exits.includes(1),'second worker should not take the same queue')
 assert.equal(cmd('status',concurrentId,'--out',out),0);assert.equal(result().status,'success');assert.equal(result().attempts,1)
 console.log('PASS durable queue submit, dispatch, persistence and cancellation')
} finally {rmSync(root,{recursive:true,force:true})}
