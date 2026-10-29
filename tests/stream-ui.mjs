import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
let now=100000, tick, syncTick, handler, frame, sourceReady=false;
let shared={revision:1,settings:{},command:null}, sequence=0;
const calls=[], elements={};
const classes=new Set();
const document={hidden:false,activeElement:null,addEventListener(){},getElementById(id){return elements[id] ||= {id,hidden:false,textContent:'',offsetParent:{}};},
  createElement(){return {blur(){},contentWindow:{focus(){},postMessage(m){calls.push(m); queueMicrotask(()=>{
    if(m.type==='start') event('playing');
    if(m.type==='stop') event('stopped');
    event('reply',{id:m.id,ok:true});
  });}}};},body:{classList:{add(...c){c.forEach(x=>classes.add(x));},remove(...c){c.forEach(x=>classes.delete(x));}},prepend(f){frame=f;queueMicrotask(()=>event('ready'));}}};
function event(type,value){handler({source:frame.contentWindow,data:{lazyStream:true,type,value}});}
const ctx={document,console,URL,Date:class extends Date {static now(){return now;}},setTimeout,clearTimeout,setInterval:(f,ms)=>{if(ms===1000)tick=f;else syncTick=f;},
  window:{addEventListener(n,f){if(n==='message')handler=f;},focus(){}},localStorage:{getItem(){return null;},setItem(){}},
  desktop:false,local:false,tizen:{},base:'http://192.168.0.18:47831',webapis:{productinfo:{isUdPanelSupported:()=>true}},
  api:async (path,payload)=>{
    assert.ok(!path.includes('/games/'),'ajuste não inicia/encerra jogo');
    if(path.endsWith('/controls')) {if(payload){shared.settings=payload.settings;shared.revision++;if(payload.action)shared.command={id:++sequence,action:payload.action};}return {...shared};}
    if(path.endsWith('/report')){if(payload.completed===shared.command?.id)shared.command=null;shared.profile=payload.profile;return shared;}
    return {source:sourceReady ? {width:3840,height:2160} : null};},
  focusControl:e=>document.activeElement=e,focusSelectedGame(){},message(){},run:f=>f(),LGCrypto:{sha:()=>''}};
vm.createContext(ctx);vm.runInContext(readFileSync('tv/platform.js','utf8'),ctx);vm.runInContext(readFileSync('tv/stream-policy.js','utf8'),ctx);
ctx.LGStreamPolicy=ctx.window.LGStreamPolicy;
vm.runInContext(readFileSync('tv/stream.js','utf8'),ctx);
await ctx.window.LGStream.prepare();
assert.equal(calls.filter(c=>c.type==='start').length,0,'preparação aceita TV inativa sem iniciar vídeo ou inventar resolução');
sourceReady=true;
await ctx.window.LGStream.start();
assert.equal(calls.filter(c=>c.type==='start').length,1);
event('menu'); elements.options.hidden=false; document.activeElement=elements.streamBitrate;
now+=31000; event('warning','Slow connection to PC. Reduce your bitrate');
now+=6001; tick(); tick();
await new Promise(r=>setTimeout(r,20));
assert.equal(calls.filter(c=>c.type==='stop').length,1,'aviso persistente dispara só uma reconexão');
assert.equal(calls.filter(c=>c.type==='start').at(-1).value.bitrate,20);
assert.equal(elements.options.hidden,false,'ajuste mantém painel aberto');
assert.equal(document.activeElement.id,'streamBitrate','ajuste restaura foco');
assert.equal(classes.has('stream-fullscreen'),false,'menu não desaparece após ajuste');
assert.ok(elements.streamProfile.textContent.includes('20 Mbps'));
tick(); await new Promise(r=>setTimeout(r,10));
assert.equal(calls.filter(c=>c.type==='stop').length,1,'não reconecta sem novo aviso');
await elements.streamDisconnect.onclick();
await new Promise(r=>setTimeout(r,10));
assert.equal(ctx.window.LGStream.active,false);
console.log('PASS: ajuste serializado, sem comandos ao jogo, perfil, menu/foco preservados e desconexão.');
