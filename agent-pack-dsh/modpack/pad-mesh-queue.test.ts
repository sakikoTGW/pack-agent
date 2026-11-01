import { spawn, spawnSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { strict as assert } from 'node:assert'
const exe=process.env.PAD_EXE ?? join(import.meta.dirname,'../pad/bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
const root=mkdtempSync(join(tmpdir(),'pad-mesh-queue-test-')),portFile=join(root,'port'),seenFile=join(root,'seen.json'),offlineFile=join(root,'offline'),input=join(root,'input.txt'),out=join(root,'out.json')
writeFileSync(input,'远端测试');mkdirSync(join(root,'library'),{recursive:true})
const server=spawn('node',['-e',`const http=require('http'),fs=require('fs'); const server=http.createServer((req,res)=>{if(req.url==='/mesh/v1/health'){res.setHeader('content-type','application/json');if(fs.existsSync(process.argv[3]))res.statusCode=503;res.end(JSON.stringify({app:'dsh-mesh'}));return}let s='';req.on('data',c=>s+=c);req.on('end',()=>{const body=JSON.parse(s);fs.writeFileSync(process.argv[2],JSON.stringify({path:req.url,token:req.headers['x-dsh-mesh-token'],body})); if(body.task==='disconnect'){req.socket.destroy();return} res.setHeader('content-type','application/json');res.end(JSON.stringify({ok:true,id:body.id,duplicate:false}))})});server.listen(0,'127.0.0.1',()=>fs.writeFileSync(process.argv[1],String(server.address().port)))`,portFile,seenFile,offlineFile],{stdio:'ignore'})
const sleep=(n:number)=>new Promise(r=>setTimeout(r,n))
try {
 for(let i=0;i<100&&!existsSync(portFile);i++) await sleep(50)
 assert.ok(existsSync(portFile),'mock server failed to start')
 const url=`http://127.0.0.1:${readFileSync(portFile,'utf8')}`
 function cmd(...args:string[]){return spawnSync(exe,['cli','queue',...args],{env:{...process.env,PACK_LAUNCHER_ROOT:root,PAD_MESH_TOKEN:'fake-pairing-token'},timeout:30000}).status}
 function read(){return JSON.parse(readFileSync(out,'utf8'))}
 assert.equal(cmd('submit-mesh','--url',url,'--thread','sample','--input',input,'--out',out),0);const id=read().id
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',id,'--out',out),0);const job=read()
 assert.equal(job.status,'accepted');assert.equal(job.result.peerTaskId,'pad-'+id)
 const got=JSON.parse(readFileSync(seenFile,'utf8'));assert.equal(got.path,'/mesh/v1/task');assert.equal(got.body.task,'远端测试');assert.equal(got.body.thread,'sample');assert.equal(got.token,'fake-pairing-token')
 assert.ok(!readFileSync(out,'utf8').includes('fake-pairing-token'))
 writeFileSync(input,'disconnect');assert.equal(cmd('submit-mesh','--url',url,'--input',input,'--out',out),0);const id2=read().id
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',id2,'--out',out),0);assert.equal(read().status,'delivery_unknown')
 assert.notEqual(cmd('retry',id2,'--out',out),0)
 assert.equal(cmd('submit-mesh','--url','http://127.0.0.1:1','--input',input,'--out',out),0);const id3=read().id
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',id3,'--out',out),0);assert.equal(read().status,'queued');assert.equal(read().result.status,'waiting_for_peer');assert.ok(read().availableAt)
 writeFileSync(input,'offline-then-online');writeFileSync(offlineFile,'1');assert.equal(cmd('submit-mesh','--url',url,'--input',input,'--out',out),0);const id4=read().id
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',id4,'--out',out),0);assert.equal(read().result.status,'waiting_for_peer')
 rmSync(offlineFile);const jobFile=join(root,'headless-queue',id4,'job.json'),offlineJob=JSON.parse(readFileSync(jobFile,'utf8'));offlineJob.availableAt='2000-01-01T00:00:00Z';writeFileSync(jobFile,JSON.stringify(offlineJob))
 assert.equal(cmd('worker','--once'),0);assert.equal(cmd('status',id4,'--out',out),0);assert.equal(read().status,'accepted')
 console.log('PASS Mesh accepted is not completed; uncertain delivery is not retried; pairing token not persisted')
} finally {server.kill();rmSync(root,{recursive:true,force:true})}
