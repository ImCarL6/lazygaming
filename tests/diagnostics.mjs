import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
let now=100000, periodic; const handlers={}, storage=new Map(), received=[];
const ctx={console,Date:class extends Date{static now(){return now;}},localStorage:{getItem:k=>storage.get(k)||null,setItem:(k,v)=>storage.set(k,v)},
  window:{devicePixelRatio:2,addEventListener:(name,fn)=>handlers[name]=fn},innerWidth:3840,innerHeight:2160,navigator:{userAgent:'Test Browser'},
  connected:false,setInterval:fn=>periodic=fn,api:async(route,payload)=>{received.push({route,payload});return {saved:true};}};
vm.createContext(ctx);vm.runInContext(readFileSync('tv/diagnostics.js','utf8'),ctx);
const flush=()=>new Promise(r=>setImmediate(r));
handlers.error({error:{message:'token=NeverExportToken123 C:\\Users\\PrivatePerson\\game.exe',stack:'at render http://host/?invite=DoNotPublishInvite'},filename:'http://host/app.js?key=DoNotPublishKey',lineno:42});
assert.equal(received.length,0,'sem conexão, falha fica na fila local');
const queued=storage.get('lazygaming-diagnostic-queue');
for(const secret of ['NeverExportToken123','PrivatePerson','DoNotPublishInvite','DoNotPublishKey']) assert.ok(!queued.includes(secret),'fila remove dados sensíveis');
ctx.connected=true;await ctx.LGDiagnostics.flush(); assert.equal(received.length,1); assert.equal(received[0].route,'/api/diagnostics/report');assert.equal(received[0].payload.file,'app.js');assert.equal(received[0].payload.viewport,'3840x2160@2');
assert.equal(JSON.parse(storage.get('lazygaming-diagnostic-queue')).length,0);
handlers.unhandledrejection({reason:new Error('Falha de promise')});await flush(); assert.equal(received.at(-1).payload.code,'unhandled-rejection');
ctx.connected=false;for(let i=0;i<25;i++)ctx.LGDiagnostics.report('issue',new Error('Caso '+i));
assert.equal(JSON.parse(storage.get('lazygaming-diagnostic-queue')).length,10,'fila offline limitada');
const count=JSON.parse(storage.get('lazygaming-diagnostic-queue')).length;ctx.LGDiagnostics.report('issue',new Error('Caso 24'));assert.equal(JSON.parse(storage.get('lazygaming-diagnostic-queue')).length,count,'erro repetido não duplica');
ctx.api=async()=>{throw Error('offline');};ctx.connected=true;await ctx.LGDiagnostics.flush();assert.equal(JSON.parse(storage.get('lazygaming-diagnostic-queue')).length,10,'erro no envio não gera outro erro nem perde fila');
ctx.api=async(route,payload)=>{received.push({route,payload});return {saved:true};};await periodic();assert.equal(JSON.parse(storage.get('lazygaming-diagnostic-queue')).length,0,'reconexão envia fila pelo auxiliar autenticado');
ctx.connected=false;ctx.LGDiagnostics.report('large',{message:'字'.repeat(8000),stack:'字'.repeat(8000)});ctx.connected=true;await ctx.LGDiagnostics.flush();assert.ok(Buffer.byteLength(JSON.stringify(received.at(-1).payload))<8192,'relatório cabe no limite HTTP mesmo com Unicode');
console.log('PASS: falhas JS/promises, fila offline limitada, reenvio autenticado, privacidade e payload Unicode.');
