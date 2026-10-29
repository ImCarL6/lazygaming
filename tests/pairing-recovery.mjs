import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHmac,randomBytes} from 'node:crypto';
import {once} from 'node:events';
import path from 'node:path';
const port=47984, base=`http://127.0.0.1:${port}`;
const child=spawn('dotnet',[path.resolve('src/LazyGaming.Host/bin/Debug/net10.0-windows/LazyGaming.Host.dll'),'--test'],{env:{...process.env,LAZYGAMING_PORT:String(port)},stdio:['ignore','pipe','pipe']});
let output='';child.stdout.on('data',v=>output+=v);child.stderr.on('data',v=>output+=v);
const hmac=(key,value)=>createHmac('sha256',Buffer.from(key,'hex')).update(value).digest('hex');
async function call(route,payload,key,custom={}) {
 const method=payload===undefined?'GET':'POST', body=payload===undefined?'':JSON.stringify(payload), time=String(Date.now()),nonce=randomBytes(16).toString('hex');
 const headers={'Content-Type':'application/json',...key?{'X-LG-Time':time,'X-LG-Nonce':nonce,'X-LG-Signature':hmac(key,`${method}\n${route}\n${time}\n${nonce}\n${body}`)}:{},...custom};
 const response=await fetch(base+route,{method,headers,body:body||undefined}); const data=await response.json(); return {status:response.status,data,headers,body};
}
try {
 let ready=false;for(let i=0;i<80;i++){try{await fetch(base+'/api/discover');ready=true;break;}catch{await new Promise(r=>setTimeout(r,100));}}
 assert.ok(ready,output);
 assert.equal((await call('/api/recovery/enroll',{})).status,401);
 const code=await call('/api/local-pairing',{}), paired=await call('/api/pair',{code:code.data.code});
 const first=paired.data.key, enrolled=await call('/api/recovery/enroll',{},first), link=enrolled.data;
 assert.equal(enrolled.status,200);
 const nextCode=await call('/api/local-pairing',{}), changed=await call('/api/pair',{code:nextCode.data.code});
 assert.equal((await call('/api/status',undefined,first)).status,401);
 const challenge=randomBytes(16).toString('hex');
 const proof=await call(`/api/recovery/discover?id=${link.id}&challenge=${challenge}`);
 assert.equal(proof.data.proof,hmac(link.key,`recovery-discover\n${challenge}\n${proof.data.identity}\n${link.id}`));
 assert.equal((await call('/api/recovery/restore',{id:link.id},changed.data.key)).status,401,'segredo de API não autoriza recuperação');
 const recovered=await call('/api/recovery/restore',{id:link.id},link.key);
 assert.equal(recovered.status,200);
 assert.equal(recovered.data.proof,hmac(link.key,`recovery-restored\n${recovered.headers['X-LG-Nonce']}\n${recovered.data.identity}\n${recovered.data.key}`));
 assert.equal((await call('/api/status',undefined,changed.data.key)).status,401);
 assert.equal((await call('/api/status',undefined,recovered.data.key)).status,200);
 assert.equal((await call('/api/recovery/restore',{id:link.id},link.key,recovered.headers)).status,401,'replay recusado');
 assert.equal((await call('/api/recovery/restore',{id:link.id},link.key,{'X-LG-Time':String(Date.now()-60000)})).status,401);
 await call('/api/recovery/enroll',{},recovered.data.key);
 assert.equal((await call('/api/recovery/restore',{id:link.id},link.key)).status,401,'TV anterior revogada por novo enrolamento');
 console.log('PASS: recuperação HTTP isolada sem código, vínculo independente, chave rotativa invalidada, prova e resposta autenticadas, replay/expiração/revogação.');
}finally{const exited=once(child,'exit');child.kill();await exited;}
