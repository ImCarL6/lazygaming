import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
const source = readFileSync('tv/app.js', 'utf8');
const code = source.slice(source.indexOf('async function choosePresentation()'), source.indexOf('function currentGame()'));
const flush = () => new Promise(resolve => setImmediate(resolve));
function fixture() {
  let now = 100000;
  const elements = {};
  for (const id of ['transport','chooseCable','chooseStream','cancelTransport','transportNote']) elements[id] = {id, hidden: id === 'transport', disabled:false,isConnected:true};
  const previous = {id:'tile',isConnected:true};
  const waiting = {}, calls = [];
  const ctx = {base:'http://pc',presentationCache:{},presentationRequests:{},presentationOrigin:'',presentationEpoch:0,transportTicket:0,lastNavigationAt:0,
    controllerFocus:previous,document:{activeElement:previous},$:id=>elements[id],Date:class extends Date {static now(){return now;}},
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
