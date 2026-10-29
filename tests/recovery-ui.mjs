import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
import {createHmac,randomBytes} from 'node:crypto';
const source=readFileSync('tv/app.js','utf8');
const functions=source.slice(source.indexOf('function persist()'),source.indexOf('function normalize('));
const hmac=(key,value)=>createHmac('sha256',Buffer.from(key,'hex')).update(value).digest('hex');
function fixture({rogue=false,badReply=false}={}) {
 const key='a'.repeat(64),id='b'.repeat(32),next='c'.repeat(64),store=new Map(),elements=new Map();let posted=0,connected=0;
 const context={JSON,Date,URL,Promise,Array,Error,encodeURIComponent,local:false,desktop:false,recoveryEnrolled:false,proofAt:0,needsPair:true,
  session:{identity:'old',key:'d'.repeat(64)},base:'http://192.168.0.18:47831',recoveryLink:{id,key,base:'http://192.168.0.18:47831'},
  LGCrypto:{hmac,nonce:()=>randomBytes(16).toString('hex')},localStorage:{setItem:(k,v)=>store.set(k,v)},
  $:id=>{if(!elements.has(id))elements.set(id,{value:'http://192.168.0.18:47831',hidden:false});return elements.get(id);},
  normalize:value=>new URL(value).origin,webapis:{network:{getIp:()=> '192.168.0.11'}},message:()=>{},
  connect:async()=>{connected++;},api:async()=>({id,key}),
  request:async(url,options)=>{
   const route=new URL(url);
   if(route.pathname==='/api/recovery/discover')return{available:true,identity:'verified-new-identity',proof:rogue?'0'.repeat(64):hmac(key,`recovery-discover\n${route.searchParams.get('challenge')}\nverified-new-identity\n${id}`)};
   posted++;assert.equal(options.headers['X-LG-Signature'],hmac(key,`POST\n/api/recovery/restore\n${options.headers['X-LG-Time']}\n${options.headers['X-LG-Nonce']}\n${options.body}`));
   return{identity:'verified-new-identity',key:next,proof:badReply?'0'.repeat(64):hmac(key,`recovery-restored\n${options.headers['X-LG-Nonce']}\nverified-new-identity\n${next}`)};
  }};
 vm.createContext(context);vm.runInContext(readFileSync('tv/platform.js','utf8'),context);vm.runInContext(functions,context);
 return{context,store,counts:()=>({posted,connected})};
}
const valid=fixture();await valid.context.recoverConnection();assert.equal(valid.context.session.key,'c'.repeat(64));assert.equal(valid.context.session.identity,'verified-new-identity');assert.equal(valid.counts().connected,1);assert.ok(valid.store.has('lazygaming-recovery'));
const rogue=fixture({rogue:true});await assert.rejects(rogue.context.recoverConnection());assert.equal(rogue.counts().posted,0,'PC sem prova não recebe comando assinado');assert.equal(rogue.context.session.identity,'old');
const badReply=fixture({badReply:true});await assert.rejects(badReply.context.recoverConnection());assert.equal(badReply.context.session.identity,'old','resposta não autenticada não substitui o pareamento');
const desktop=fixture();desktop.context.desktop=true;desktop.context.updateRecovery();assert.equal(desktop.context.$('recoverConnection').hidden,true,'PC não oferece recuperação da credencial da TV');
console.log('PASS: TV recupera conexão, reconhece PC antes do comando, autentica resposta, preserva vínculo em falha e oculta ação no PC.');
