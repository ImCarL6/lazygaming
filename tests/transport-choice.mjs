import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
const source = readFileSync('tv/app.js', 'utf8');
const code = source.slice(source.indexOf('async function choosePresentation('), source.indexOf('function currentGame()'));
const flush = () => new Promise(resolve => setImmediate(resolve));
function fixture(desktop = false, libraryGames = []) {
  let now = 100000;
  const elements = {};
  for (const id of ['transport','transportTitle','chooseCable','chooseStream','cancelTransport','transportNote','launchChoices','launchChoiceLabel','launchChoiceButtons']) elements[id] = {id, hidden: id === 'transport', disabled:false,isConnected:true};
  elements.launchChoiceButtons.children=[];
  Object.defineProperty(elements.launchChoiceButtons,'innerHTML',{set(){this.children=[];}});
  elements.launchChoiceButtons.appendChild=function(button){this.children.push(button);};
  const previous = {id:'tile',isConnected:true};
  const waiting = {}, calls = [];
  const ctx = {desktop,libraryGames,base:'http://pc',presentationCache:{},presentationRequests:{},presentationOrigin:'',presentationEpoch:0,transportTicket:0,lastNavigationAt:0,
    controllerFocus:previous,document:{activeElement:previous,createElement(){return {attributes:{},setAttribute(k,v){this.attributes[k]=v;}};}},$:id=>elements[id],Date:class extends Date {static now(){return now;}},
    focusControl(element){ctx.controllerFocus=element;},reportStreamClient:async()=>{},
    api(path){calls.push(path);return new Promise((resolve,reject)=>waiting[path]={resolve,reject});}};
  vm.createContext(ctx);vm.runInContext(code,ctx);
  return {ctx,elements,calls,waiting,advance:ms=>now+=ms};
}
let f=fixture(), result=f.ctx.choosePresentation();
assert.equal(f.elements.transport.hidden,false,'diálogo aparece no mesmo clique, antes de respostas de rede');
assert.equal(f.elements.chooseCable.disabled,true);assert.equal(f.elements.chooseStream.disabled,true);
assert.equal(f.ctx.controllerFocus.id,'cancelTransport');await flush();
f.waiting['/api/presentation/cable'].resolve({available:true,note:null});await flush();
assert.equal(f.elements.chooseCable.disabled,false,'Cabo fica utilizável sem aguardar Sunshine');assert.equal(f.elements.chooseStream.disabled,true);
assert.equal(f.ctx.controllerFocus.id,'chooseCable');f.elements.chooseCable.onclick();assert.equal(await result,'cable');
f.waiting['/api/presentation'].resolve({stream:{available:true,note:null}});await flush();
assert.equal(f.elements.transport.hidden,true,'resposta tardia não reabre diálogo após escolha');assert.equal(f.ctx.controllerFocus.id,'tile');
assert.ok(f.calls.every(path=>!path.includes('/games/')),'consulta do seletor não inicia nem encerra jogos');

result=f.ctx.choosePresentation();assert.equal(f.elements.chooseCable.disabled,false,'consulta antecipada/cache responde de forma síncrona');
assert.equal(f.calls.length,2,'cache recente não repete consultas');f.elements.cancelTransport.onclick();assert.equal(await result,null);
f.advance(10001);result=f.ctx.choosePresentation();assert.equal(f.elements.chooseCable.disabled,true,'cache expirado exige nova consulta');
f.elements.cancelTransport.onclick();assert.equal(await result,null);await flush();
f.waiting['/api/presentation/cable'].resolve({available:true});f.waiting['/api/presentation'].resolve({stream:{available:true}});await flush();
assert.equal(f.elements.transport.hidden,true,'cancelamento imediato não depende da rede');assert.equal(f.ctx.controllerFocus.id,'tile');

f=fixture();result=f.ctx.choosePresentation();await flush();
f.ctx.lastNavigationAt=5;f.waiting['/api/presentation/cable'].resolve({available:true});await flush();
assert.equal(f.ctx.controllerFocus.id,'cancelTransport','retorno assíncrono preserva navegação manual');
f.waiting['/api/presentation'].reject(Error('offline'));await flush();assert.equal(f.elements.chooseStream.disabled,true);
assert.equal(f.elements.chooseCable.disabled,false,'falha de Stream não bloqueia Cabo');f.elements.cancelTransport.onclick();await result;

f=fixture();result=f.ctx.choosePresentation();await flush();
f.waiting['/api/presentation/cable'].reject(Error('offline'));f.waiting['/api/presentation'].resolve({stream:{available:true}});await flush();
assert.equal(f.elements.chooseStream.disabled,false);assert.equal(f.ctx.controllerFocus.id,'chooseStream');
f.elements.chooseStream.onclick();assert.equal(await result,'stream');
f.ctx.base='http://outro-pc';result=f.ctx.choosePresentation();assert.equal(f.elements.chooseStream.disabled,true,'troca de PC não reutiliza disponibilidade anterior');f.elements.cancelTransport.onclick();await result;
console.log('PASS: seletor imediato, Cabo independente, cache curto, cancelamento/falhas, foco e respostas tardias sem lançar jogos.');

f=fixture();let picked=null;
const game={choiceLabel:'Idioma',selectedChoice:'pt-br',choices:[{id:'pt-br',name:'Português (Brasil)',available:true},{id:'en',name:'English',available:true}]};
result=f.ctx.choosePresentation(game,value=>picked=value);assert.equal(f.elements.launchChoices.hidden,false);
assert.equal(f.elements.launchChoiceButtons.children[0].attributes['aria-pressed'],'true');
f.elements.launchChoiceButtons.children[1].onclick();assert.equal(f.elements.launchChoiceButtons.children[1].attributes['aria-pressed'],'true');
assert.equal(f.elements.launchChoiceButtons.children[0].attributes['aria-pressed'],'false');await flush();
f.waiting['/api/presentation/cable'].resolve({available:true});await flush();f.elements.chooseCable.onclick();
assert.equal(await result,'cable');assert.equal(picked,'en','language and transport selected in one panel');
picked=null;result=f.ctx.choosePresentation(game,value=>picked=value);f.elements.cancelTransport.onclick();await result;assert.equal(picked,null,'cancel does not change preference');
result=f.ctx.choosePresentation();assert.equal(f.elements.launchChoices.hidden,true,'ordinary games keep original panel');f.elements.cancelTransport.onclick();await result;
const missing={...game,choices:game.choices.map(v=>({...v,available:false}))};result=f.ctx.choosePresentation(missing,value=>picked=value);
assert.equal(f.elements.chooseCable.disabled,true,'no version available blocks launch');f.elements.cancelTransport.onclick();await result;
console.log('PASS: idioma e transporte no mesmo painel, seleção independente, cancelamento e jogos sem variantes.');

f=fixture(true);const wilds={id:'wilds',name:'Wilds'};result=f.ctx.choosePresentation(wilds);
assert.equal(f.elements.transportTitle.textContent,'Iniciar jogo?');assert.equal(f.elements.chooseStream.hidden,true,'PC sem Stream');
assert.equal(f.elements.chooseCable.textContent,'Jogar');await flush();
assert.ok(!f.calls.includes('/api/presentation'),'PC não consulta Stream');
f.waiting['/api/presentation/cable'].resolve({available:true,note:null});await flush();
assert.match(f.elements.transportNote.textContent,/Abrir Wilds\?/);assert.equal(f.ctx.controllerFocus.id,'chooseCable');
f.elements.chooseCable.onclick();assert.equal(await result,'cable','PC inicia sempre pelo caminho de tela local');
f=fixture(true,[{id:'ror2',name:'Risk of Rain 2',running:true}]);result=f.ctx.choosePresentation(wilds);
assert.equal(f.elements.transportTitle.textContent,'Trocar jogo?');assert.equal(f.elements.chooseCable.textContent,'Trocar');
f.waiting['/api/presentation/cable'].resolve({available:false,note:'Escolha a tela de jogo na Central do PC.'});await flush();
assert.equal(f.elements.chooseCable.hidden,false);assert.equal(f.elements.chooseCable.disabled,true,'tela indisponível bloqueia com aviso visível');
assert.match(f.elements.transportNote.textContent,/Encerrar Risk of Rain 2 e abrir Wilds\?.*Central do PC/);
f.elements.cancelTransport.onclick();assert.equal(await result,null);
console.log('PASS: launcher PC confirma iniciar/trocar sem Cabo/Stream; TV mantém seletor.');
