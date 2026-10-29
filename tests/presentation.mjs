import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync('tv/presentation.js', 'utf8');
const recoverClasses=new Set(); let recoveringSource={type:'TV',number:0}, switches=0, shows=0;
const recoverTv={systeminfo:{getPropertyValue(name,done){done({connected:[{type:'HDMI',number:1},{type:'HDMI',number:2}]});}},tvwindow:{getSource:()=>recoveringSource,hide(done){done();},setSource(source,done){switches++;recoveringSource=source;done();},show(done){shows++;done();}}};
const recoverCtx={};vm.createContext(recoverCtx);vm.runInContext(source,recoverCtx);
const recover=new recoverCtx.TVLiveBackground(recoverTv,{classList:{add:c=>recoverClasses.add(c),remove:c=>recoverClasses.delete(c)}});
recover.update(true,true,true,1);recover.update(true,true,true,1);
assert.equal(switches,1);assert.equal(shows,1);assert.ok(recoverClasses.has('live-game'),'recupera entrada HDMI conhecida e compõe fundo de qualquer jogo');
recover.suspend();assert.equal(recoverClasses.size,0);assert.equal(switches,1,'suspensão Stream não troca nem oculta MAIN');
function fixture() {
  const classes = new Set(), calls = [];
  let ok, fail;
  const tv = { tvwindow: {
    getSource: () => ({ type: 'HDMI', number: 1 }),
    show(done, error, rect, type, z) { calls.push(['show', rect, type, z]); ok = done; fail = error; },
    hide(done) { calls.push(['hide']); done(); }
  } };
  const ctx = { }; vm.createContext(ctx); vm.runInContext(source, ctx);
  const bg = new ctx.TVLiveBackground(tv, { classList: { add: c => classes.add(c), remove: c => classes.delete(c) } });
  return { bg, tv, calls, classes, ok: () => ok(), fail: () => fail() };
}
let f = fixture();
f.bg.update(true, true, true, 1);
assert.equal(f.classes.size, 0, 'arte fica até confirmação da API');
assert.equal(f.calls[0][3], 'BEHIND');
f.bg.update(true, true, true, 1); assert.equal(f.calls.length, 1, 'não renegociar por polling');
f.ok(); assert.ok(f.classes.has('live-game'));
f.bg.update(false, true, true, 1); assert.equal(f.classes.size, 0); assert.equal(f.calls.at(-1)[0], 'hide');
f = fixture(); f.bg.update(true, true, true, 1); f.bg.update(true, true, false, 1); f.ok();
assert.equal(f.classes.size, 0, 'callback tardio não deixa app oculto transparente');
assert.equal(f.calls.length, 1, 'app oculto não manipula MAIN enquanto o viewer assume HDMI');
f = fixture(); f.bg.update(true, true, true, 2); assert.equal(f.calls.length, 0, 'não mostrar Fire Stick nem trocar entrada');
f.bg.update(true, true, true, 0); assert.equal(f.calls.length, 0, 'exigir entrada do PC conhecida');
f.bg.update(true, true, true, 1); f.fail(); f.bg.update(true, true, true, 1);
assert.equal(f.calls.length, 1, 'falha mantém arte sem loop de tentativas');
f.bg.update(true, false, true, 1); f.bg.update(true, true, true, 1); assert.equal(f.calls.length, 2, 'toggle permite tentar outra vez');

// A escolha não pode fechar outro jogo nem iniciar qualquer um ao cancelar.
const app = readFileSync('tv/app.js', 'utf8');
const flow = app.slice(app.indexOf('async function play(game)'), app.indexOf('async function choosePresentation()'));
const calls = [];
const ctx = { choosePresentation: async () => null, api: async () => calls.push('api'), message() {}, refresh() {}, showHdmi() {} };
vm.createContext(ctx); vm.runInContext(flow, ctx); await ctx.play({ id: 'wilds' }); assert.equal(calls.length, 0);
console.log('PASS: HDMI sob menu, fallback, cancelamento, falha e callback tardio sem iniciar/fechar jogos.');

const classes = new Set(), revoked = [], fetched = [];
const image = { hidden: true, set src(value) { queueMicrotask(() => this.onload()); } };
const pc = { AbortController, setTimeout, clearTimeout, encodeURIComponent, URL: { createObjectURL: () => 'blob:frame', revokeObjectURL: url => revoked.push(url) },
  fetch: async (url, options) => { fetched.push([url, options]); return { status: 200, blob: async () => ({}) }; } };
vm.createContext(pc); vm.runInContext(source, pc);
const live = new pc.DesktopLiveBackground({ classList: { add: c => classes.add(c), remove: c => classes.delete(c) } }, image, 'private-client');
live.update(true, true, true);
await new Promise(resolve => setTimeout(resolve, 20));
assert.ok(classes.has('live-game')); assert.equal(image.hidden, false);
assert.equal(fetched[0][1].credentials, 'same-origin');
live.update(true, true, false);
assert.equal(classes.size, 0); assert.equal(image.hidden, true); assert.ok(revoked.includes('blob:frame'));
await new Promise(resolve => setTimeout(resolve, 120)); assert.equal(fetched.length, 1, 'ocultar encerra pedidos de captura');
console.log('PASS: fundo PC ao vivo, cookie local, descarte em memória e parada ao ocultar menu.');

const nativeClasses = new Set(), nativeCalls = []; let nativeMessage;
const nativePc = { window: { LGNativeDesktop: { background: value => nativeCalls.push(value) }, chrome: { webview: { addEventListener: (_, handler) => nativeMessage = handler } } }, fetch: () => { throw Error('fundo nativo não pode buscar JPEG'); } };
vm.createContext(nativePc); vm.runInContext(source, nativePc);
const nativeImage = { hidden: true };
const nativeLive = new nativePc.DesktopLiveBackground({ classList: { add: c => nativeClasses.add(c), remove: c => nativeClasses.delete(c) } }, nativeImage, 'native-client');
nativeLive.update(true, true, true); assert.equal(nativeCalls.at(-1), true); assert.equal(nativeClasses.size, 0, 'aguarda confirmação de superfície pelo host');
nativeMessage({ data: { type: 'background', active: true } }); assert.ok(nativeClasses.has('live-game')); assert.equal(nativeImage.hidden, true, 'DWM substitui imagem JPEG');
nativeLive.update(true, true, false); assert.equal(nativeCalls.at(-1), false); assert.equal(nativeClasses.size, 0);
nativeMessage({ data: { type: 'background', active: true } }); assert.equal(nativeClasses.size, 0, 'resposta tardia não reabre fundo oculto');
nativeLive.update(true, false, true); assert.equal(nativeCalls.at(-1), false);
nativeLive.update(true, true, true); nativeMessage({ data: { type: 'background', active: false } }); assert.equal(nativeClasses.size, 0, 'jogo minimizado/ausente conserva arte opaca');
console.log('PASS: fundo GPU nativo sem HTTP/JPEG, confirmação do host, toggle e ocultação sem callbacks tardios.');

const resumeCalls = [];
const resume = { desktop: false, local: false, presentationMode: 'cable', currentGame: () => ({ id: 'wilds', available: true, running: true }), message() {},
  api: async (path, body) => { resumeCalls.push([path, body]); }, refresh: async () => resumeCalls.push(['refresh']), showHdmi: async () => resumeCalls.push(['hdmi']) };
vm.createContext(resume);
vm.runInContext(app.slice(app.indexOf('async function playSelected()'), app.indexOf('function renderGames(')), resume);
await resume.playSelected();
assert.deepEqual(resumeCalls.map(c => c[0]), ['/api/games/wilds/start', 'refresh', '/api/games/wilds/focus', 'hdmi'], 'preparar TV antes de focar jogo existente/trocar HDMI');
assert.equal(resumeCalls[0][1].mode, 'cable');
resumeCalls.length = 0; resume.desktop = true; await resume.playSelected();
assert.deepEqual(resumeCalls.map(c => c[0]), ['/api/games/wilds/focus', 'hdmi'], 'Continuar no PC não prepara telas');
resumeCalls.length = 0; resume.desktop = false; resume.api = async () => { throw Error('TV ausente'); };
await assert.rejects(resume.playSelected(), /TV ausente/); assert.equal(resumeCalls.length, 0, 'falha de preparação não troca entrada');
console.log('PASS: Continuar TV prepara display antes do HDMI; Continuar PC preserva topologia.');
const streamFlow = { desktop: false, local: false, choosePresentation: async () => 'stream', api: async (path, body) => { streamFlow.calls.push([path, body]); return path === '/api/stream/handoff' ? { applicationId: 'MoonLightS.MoonlightWasm', serverUid: 'host', appId: 12 } : { state: { games: [] } }; }, calls: [],
  message() {}, refresh: async () => {}, showStream: async () => streamFlow.calls.push(['stream']), showHdmi: async () => { throw Error('HDMI não pertence ao Stream'); } };
streamFlow.window = { LGStream: {
  prepare: async () => streamFlow.calls.push(['prepare-stream']),
  start: async () => streamFlow.calls.push(['embedded-stream'])
} };
streamFlow.tizen = {};
vm.createContext(streamFlow); vm.runInContext(flow, streamFlow); await streamFlow.play({ id: 'wilds' });
assert.equal(streamFlow.calls.find(c => /start$/.test(c[0]))[1].mode, 'stream');
assert.equal(streamFlow.calls[0][0], 'prepare-stream', 'validar vídeo e pareamento antes de lançar jogo');
assert.equal(streamFlow.calls.at(-1)[0], 'embedded-stream');
streamFlow.calls.length = 0;
streamFlow.window.LGStream.prepare = async () => { throw Error('pareamento indisponível'); };
await assert.rejects(streamFlow.play({id:'wilds'}), /pareamento indisponível/);
assert.equal(streamFlow.calls.length, 0, 'falha de pareamento não inicia nem encerra jogo');
resume.presentationMode = 'stream'; resume.api = async path => resumeCalls.push([path]); resume.reportStreamClient = async () => {};
resume.showStream = async () => resumeCalls.push(['stream']); resumeCalls.length = 0; await resume.playSelected();
assert.deepEqual(resumeCalls.map(c => c[0]), ['/api/games/wilds/focus', 'stream'], 'retomar Stream não prepara HDMI');
console.log('PASS: jogar/retomar Stream faz motor integrado sem entrar no HDMI.');
