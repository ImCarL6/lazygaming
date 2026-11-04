import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
const source = readFileSync('tv/app.js', 'utf8');
const code = source.slice(source.indexOf('// Volume rápido:'), source.indexOf("document.addEventListener('keydown', handleRemoteKey)"));
const flush = () => new Promise(resolve => setImmediate(resolve));
const elements = {};
for (const id of ['volumeHud','volumeButton','volumeHint','volumeChipLevel','volumeValue','volumeFill','volumeTrack','volumeDevice','volumeUp','volumeDown','volumeChipKey','volumeHintKey','volumeCloseKey'])
  elements[id] = {id, hidden: id === 'volumeHud' || id === 'volumeButton', textContent:'', style:{}, attributes:{}, setAttribute(k,v){this.attributes[k]=v;}, classList:{set:new Set(),add(c){this.set.add(c);},remove(c){this.set.delete(c);},toggle(c,v){v?this.set.add(c):this.set.delete(c);}}};
let server = {level: 37, muted: false, device: 'SAMSUNG (NVIDIA High Definition Audio)'};
const posts = [];
const ctx = {connected:true, desktop:true, $:id=>elements[id], message(){}, playMenuSound(){}, setTimeout:()=>0, clearTimeout(){},
  api: async (path, body) => { if (body) { posts.push(body.level); server = {...server, level: body.level, muted: false}; } return {...server}; }};
vm.createContext(ctx); vm.runInContext(code, ctx);
const key = (keyCode, extra = {}) => ctx.handleVolumeKey({keyCode, preventDefault(){}, ...extra});

assert.equal(key(39), false, 'setas seguem a navegação normal com o volume fechado');
await ctx.loadVolume(false);
assert.equal(elements.volumeButton.hidden, false, 'chip aparece quando o PC informa o volume');
assert.equal(elements.volumeChipLevel.textContent, '37%');
assert.equal(elements.volumeDevice.textContent, 'TV Samsung');
assert.equal(key(1001), true); assert.equal(elements.volumeHud.hidden, false, 'botão superior abre em um comando');
await flush();
key(39); assert.equal(ctx.volumeLevel, 40, 'primeiro passo arredonda para múltiplo de 5');
key(38, {repeat:true}); assert.equal(ctx.volumeLevel, 45, 'seta/analógico mantido repete');
key(37); key(40); assert.equal(ctx.volumeLevel, 35);
await flush(); await flush();
assert.equal(posts.at(-1), 35, 'último valor sempre chega ao PC'); assert.ok(posts.length <= 4, 'pedidos não acumulam além dos passos');
for (let i = 0; i < 30; i++) key(37); assert.equal(ctx.volumeLevel, 0, 'limite inferior');
for (let i = 0; i < 30; i++) key(39); assert.equal(ctx.volumeLevel, 100, 'limite superior');
assert.equal(key(13), true); assert.equal(elements.volumeHud.hidden, true, 'OK fecha');
key(1001); assert.equal(key(10009), true); assert.equal(elements.volumeHud.hidden, true, 'Voltar fecha sem sair de outra camada');
key(1001); key(1001); assert.equal(elements.volumeHud.hidden, true, 'botão superior alterna');
key(1001, {repeat:true}); assert.equal(elements.volumeHud.hidden, true, 'repetição não reabre');
await flush();
// Na TV o volume é o do controle Samsung: nada de chip/HUD nem tecla capturada.
ctx.desktop = false; elements.volumeHud.hidden = true;
assert.equal(key(1001), false, 'TV não captura a tecla de volume'); assert.equal(elements.volumeHud.hidden, true, 'TV não abre o HUD');
console.log('PASS: volume rápido abre pelo botão superior, ajusta de 5 em 5 por seta/analógico, limita 0–100 e fecha por OK/Voltar; na TV fica desligado.');

// Jogo aberto: duas linhas; o jogo vem escolhido; ▲▼ troca de linha; ◀▶ ajusta só a linha escolhida.
{
  const el = {};
  for (const id of ['volumeHud','volumeButton','volumeHint','volumeChipLevel','volumeValue','volumeFill','volumeTrack','volumeDevice','volumeUp','volumeDown','volumeChipKey','volumeHintKey','volumeCloseKey',
    'volumeSystem','volumeGame','volumeGameName','volumeGameValue','volumeGameFill','volumeGameTrack','volumeGameUp','volumeGameDown','volumeSwitchHint'])
    el[id] = {id, hidden: id === 'volumeHud' || id === 'volumeGame', textContent:'', style:{}, attributes:{}, setAttribute(k,v){this.attributes[k]=v;}, classList:{set:new Set(),add(c){this.set.add(c);},remove(c){this.set.delete(c);},toggle(c,v){v?this.set.add(c):this.set.delete(c);},contains(c){return this.set.has(c);}}};
  let srv = {level: 60, muted: false, device: 'Alto-falantes', game: {name: 'Eglantine', level: 100, muted: false}};
  const sent = [];
  const c2 = {connected:true, desktop:true, $:id=>el[id], message(){}, playMenuSound(){}, setTimeout:()=>0, clearTimeout(){},
    api: async (path, body) => { if (body) { sent.push(body); if (body.target === 'game') srv = {...srv, game: {...srv.game, level: body.level}}; else srv = {...srv, level: body.level}; } return JSON.parse(JSON.stringify(srv)); }};
  vm.createContext(c2); vm.runInContext(code, c2);
  const k = (keyCode, extra = {}) => c2.handleVolumeKey({keyCode, preventDefault(){}, ...extra});
  await c2.loadVolume(false); k(1001); await flush();
  assert.equal(el.volumeGame.hidden, false, 'linha do jogo aparece com jogo aberto');
  assert.equal(el.volumeGameName.textContent, 'Eglantine', 'nome do jogo/emulador');
  assert.equal(el.volumeGame.classList.contains('selected'), true, 'jogo já vem escolhido');
  k(37); k(37); await flush(); await flush();
  assert.equal(sent.at(-1).target, 'game'); assert.equal(sent.at(-1).level, 90, '◀ baixa só o jogo');
  assert.equal(srv.level, 60, 'volume geral intacto');
  k(38); assert.equal(el.volumeSystem.classList.contains('selected'), true, '▲ escolhe o PC');
  k(37); await flush(); await flush();
  assert.equal(sent.at(-1).target, undefined); assert.equal(srv.level, 55, '◀ baixa o geral');
  assert.equal(srv.game.level, 90, 'jogo intacto');
  k(40); assert.equal(el.volumeGame.classList.contains('selected'), true, '▼ volta ao jogo');
  srv = {...srv, game: null}; await c2.loadVolume(false);
  assert.equal(el.volumeGame.hidden, true, 'jogo fechado: volta a uma linha');
  k(38); await flush(); await flush(); assert.equal(srv.level, 60, 'sem jogo, ▲ volta a ajustar o geral');
  console.log('PASS: volume do jogo/emulador separado do geral, escolha por ▲▼ e ajuste por ◀▶.');
}
