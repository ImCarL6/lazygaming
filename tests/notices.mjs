import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
let now = 0, next = 0; const tasks = new Map(), events = {};
const context = {Map, Array, document:{addEventListener:(name,fn)=>events[name]=fn},
  setTimeout:(fn,ms)=>{tasks.set(++next,{fn,at:now+ms});return next;}, clearTimeout:id=>tasks.delete(id)};
vm.createContext(context); vm.runInContext(readFileSync('tv/notices.js','utf8'), context);
const notices=context.LGNotices, main={textContent:''}, advanced={textContent:''}, dialog={textContent:''};
function tick(ms) { now+=ms; for(const [id,task] of [...tasks]) if(task.at<=now){tasks.delete(id);task.fn();} }
notices.show(main,'Preparando a TV e abrindo o jogo…'); tick(2500);
assert.ok(main.textContent); notices.show(main, main.textContent); tick(300);
assert.equal(main.textContent,'','mensagem repetida não prolonga o prazo de 2,8 s');
notices.show(main,'Antiga'); tick(2000); notices.show(main,'Saída de áudio alterada'); tick(800);
assert.equal(main.textContent,'Saída de áudio alterada','timer antigo não apaga mensagem nova'); tick(2000); assert.equal(main.textContent,'');
notices.changed('status','Não foi possível abrir o jogo',main); tick(2800);
for(let i=0;i<20;i++){notices.changed('status','Não foi possível abrir o jogo',main);tick(4000);}
assert.equal(main.textContent,'','polling não ressuscita aviso que já expirou');
notices.changed('status','',main); notices.changed('status','Não foi possível abrir o jogo',main); assert.ok(main.textContent,'nova ocorrência após resolver o estado é avisada');
notices.show(advanced,'Salvo'); notices.show(dialog,'Erro'); events.visibilitychange();
assert.equal(main.textContent+advanced.textContent+dialog.textContent,'','ocultar ou reabrir limpa todos os avisos mesmo com timers suspensos');
notices.changed('status','Não foi possível abrir o jogo',main); assert.equal(main.textContent,'','reabertura não repete estado antigo');
notices.show(main,'Áudio'); notices.clearAll(); notices.show(main,'Novo aviso'); tick(2800); assert.equal(main.textContent,'');
for(const file of ['tv/index.html','tv/hub.html']) assert.ok(readFileSync(file,'utf8').includes('src="notices.js"'),'ambas as interfaces carregam a mesma política');
const app=readFileSync('tv/app.js','utf8');
assert.ok(app.includes("LGNotices.changed('game-status'")); assert.ok(app.includes('launcher-shown|launcher-hidden'));
const elements={message:main,connection:{},setup:{},library:{},volumeButton:{}};
const status={state:{games:[],startError:'Falha de abertura',environment:{mode:'cable',note:''}}};
Object.assign(context,{$:id=>elements[id],api:async()=>status,renderGames(){},syncSetupLook(){},updateLiveBackground(){},renderPadGlyphs(){},
  connected:true,desktop:true,needsPair:false,volumeChecked:true});
vm.runInContext(app.slice(app.indexOf('async function refresh()'),app.indexOf('var liveBackground;')),context);
await context.refresh(); assert.equal(main.textContent,'Falha de abertura'); tick(2800);
for(let i=0;i<5;i++){await context.refresh();tick(4000);assert.equal(main.textContent,'','refresh real não ressuscita o erro');}
status.state.startError=''; status.state.environment.note='Aguardando o áudio da TV';
await context.refresh(); assert.equal(main.textContent,'Aguardando o áudio da TV'); tick(2800); await context.refresh(); assert.equal(main.textContent,'');
let nativeEvent, opened=0;
context.window={chrome:{webview:{addEventListener:(name,fn)=>nativeEvent=fn}}};
context.requestAnimationFrame=fn=>fn();context.playLauncherOpen=()=>opened++;
vm.runInContext(app.slice(app.indexOf("if (typeof window !== 'undefined' && window.chrome"),app.indexOf('// Decodificar já')),context);
notices.show(main,'Áudio alterado'); nativeEvent({data:{type:'launcher-hidden'}}); assert.equal(main.textContent,'');
notices.show(main,'Abertura antiga'); nativeEvent({data:{type:'launcher-shown'}}); assert.equal(main.textContent,''); assert.equal(opened,1);
console.log('PASS: avisos expiram em 2,8 s no PC/TV/Central; polling, concorrência e reabertura não preservam mensagens antigas.');
