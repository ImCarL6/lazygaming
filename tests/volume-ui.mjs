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
const ctx = {connected:true, $:id=>elements[id], message(){}, playMenuSound(){}, setTimeout:()=>0, clearTimeout(){},
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
console.log('PASS: volume rápido abre pelo botão superior, ajusta de 5 em 5 por seta/analógico, limita 0–100 e fecha por OK/Voltar.');
