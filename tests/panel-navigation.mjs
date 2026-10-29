import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync('tv/app.js', 'utf8');
const document = { activeElement: { tagName: 'BODY' } }, requests = [];
const panel = { hidden: false, scrollTop: 0, getBoundingClientRect: () => ({ top: 20, bottom: 300 }) };
function control(id, y, container = panel) {
  const classes = new Set();
  return { id, tagName: 'BUTTON', isConnected: true, offsetParent: {}, disabled: false, clicks: 0,
    classList: { add: c => classes.add(c), remove: c => classes.delete(c), contains: c => classes.has(c) },
    closest: () => container, focus() {}, click() { this.clicks++; },
    getBoundingClientRect: () => ({ left: 20, right: 220, top: y - container.scrollTop, bottom: y + 55 - container.scrollTop }) };
}
const audio = [control('audio1', 210), control('audio2', 280), control('audio3', 350)];
const elements = { options: panel, setup: { hidden: true }, modal: { hidden: true }, transport: { hidden: true }, connectionSettings: control('connectionSettings', 10), find: control('find', 10) };
elements.cancel = { click() { elements.modal.hidden = true; } };
elements.cancelTransport = { click() { elements.transport.hidden = true; } };
panel.querySelectorAll = () => audio;
const ctx = { document, desktop: true, local: true, setupParentOptions: false, connected: true, lastNavigationAt: 0,
  $: id => elements[id], api: async path => requests.push(path), message() {}, selectGame() {}, focusSelectedGame() { ctx.rootFocused = true; } };
vm.createContext(ctx);
vm.runInContext(source.slice(source.indexOf('var controllerFocus = null'), source.indexOf('function message(')), ctx);
vm.runInContext(source.slice(source.indexOf('function handleRemoteKey('), source.indexOf("document.addEventListener('keydown', handleRemoteKey)")), ctx);
const key = (keyCode, repeat = false) => ctx.handleRemoteKey({ keyCode, repeat, controller: true, preventDefault() {} });
ctx.focusControl(audio[0]); key(40); key(40);
assert.equal(ctx.controllerFocus, audio[2]);
assert.ok(audio[2].getBoundingClientRect().bottom <= 282, 'terceira saída de áudio fica dentro do painel com margem para destaque');
assert.ok(audio[2].classList.contains('controller-focused'));
assert.ok(!audio[1].classList.contains('controller-focused'));
key(13); key(13, true); assert.equal(audio[2].clicks, 1, 'X confirma uma vez');
key(38); key(38);
assert.ok(audio[0].getBoundingClientRect().top >= 38, 'subir revela novamente a primeira saída');
key(10009); assert.ok(panel.hidden); assert.equal(requests.length, 0, 'bolinha fecha configurações, mantendo launcher');
key(10009, true); assert.equal(requests.length, 0, 'bolinha mantida não atravessa duas telas');
key(10009); assert.deepEqual(requests, ['/api/launcher/hide'], 'só na biblioteca bolinha oculta launcher');
panel.hidden = false; elements.modal.hidden = false; key(10009);
assert.equal(elements.modal.hidden, true); assert.equal(panel.hidden, false); assert.equal(requests.length, 1);
elements.transport.hidden = false; key(10009); assert.equal(elements.transport.hidden, true); assert.equal(requests.length, 1);
elements.setup.hidden = false; panel.hidden = true; ctx.setupParentOptions = true; ctx.controllerFocus = elements.find; key(10009);
assert.equal(elements.setup.hidden, true); assert.equal(panel.hidden, false); assert.equal(ctx.controllerFocus, elements.connectionSettings);
ctx.desktop = false; panel.scrollTop = 0; ctx.focusControl(audio[2]);
assert.ok(audio[2].classList.contains('controller-focused'), 'TV também tem destaque explícito de foco');
assert.ok(audio[2].getBoundingClientRect().bottom <= 282);
console.log('PASS: foco visível com scroll para baixo/cima, X confirma, bolinha volta por nível e só sai na raiz.');
elements.setup.hidden = true; elements.options.hidden = true; elements.modal.hidden = true;
elements.settings = control('settings',10); elements.heroPlay = control('heroPlay',200);
elements.heroStop = control('heroStop',200); elements.heroStop.hidden = true;
elements.moveToTv = control('moveToTv',200); elements.moveToTv.hidden = true;
elements.streamShortcut = control('streamShortcut',200);
ctx.focusControl(elements.heroPlay); key(39);
assert.equal(ctx.controllerFocus, elements.streamShortcut, 'direita de Jogar chega à transmissão na barra inferior');
key(39); assert.equal(ctx.controllerFocus,elements.settings);
key(37); key(37); assert.equal(ctx.controllerFocus,elements.heroPlay);
elements.heroStop.hidden = false; elements.moveToTv.hidden = false;
for (const desktop of [true, false]) {
  ctx.desktop = desktop; ctx.focusControl(elements.heroPlay); key(39); key(39);
  assert.equal(ctx.controllerFocus, elements.moveToTv, 'reparo alcançável na barra no PC e TV');
  key(38); assert.ok(ctx.rootFocused, 'Cima no reparo retorna ao carrossel');
  ctx.focusControl(elements.moveToTv); key(39);
  assert.equal(ctx.controllerFocus, elements.streamShortcut);
}
elements.heroStop.hidden = true; elements.moveToTv.hidden = true;
panel.hidden=false; panel.contains=e=>audio.includes(e);
ctx.focusControl(elements.heroPlay); key(13);
assert.equal(elements.heroPlay.clicks,0,'painel impede ativar botão escondido atrás dele');
assert.equal(ctx.controllerFocus,audio[0],'foco é recuperado dentro do painel');
console.log('PASS: acesso direto à engrenagem e contenção do foco no painel aberto.');
elements.options.hidden = true;
elements.gameSettings = control('gameSettings', 150);
elements.gameOptions = { hidden: true, contains: e => [elements.closeGameOptions, elements.gameName, elements.saveGameName, elements.metadataQuery, elements.searchMetadata].includes(e) };
elements.closeGameOptions = control('closeGameOptions', 100);
elements.gameName = control('gameName', 180); elements.gameName.tagName = 'INPUT'; elements.gameName.blur = () => {};
elements.saveGameName = control('saveGameName', 240);
elements.metadataQuery = control('metadataQuery', 300); elements.metadataQuery.tagName = 'INPUT'; elements.metadataQuery.blur = () => {};
elements.searchMetadata = control('searchMetadata', 360);
elements.gameOptions.querySelectorAll = () => [elements.closeGameOptions, elements.gameName, elements.saveGameName, elements.metadataQuery, elements.searchMetadata];
elements.gameSettings.click = () => { elements.gameOptions.hidden = false; ctx.focusControl(elements.closeGameOptions); };
const tiles = [control('gameA', 420, { scrollTop: 0 }), control('gameB', 420, { scrollTop: 0 })];
tiles.forEach((tile, index) => { tile.dataset = { id: 'game' + index }; tile.classList.add('game-tile'); });
elements.games = { querySelectorAll: () => tiles };

ctx.focusSelectedGame = () => ctx.focusControl(tiles[ctx.selectedTile || 0]);
ctx.selectGame = id => { ctx.selectedTile = tiles.findIndex(tile => tile.dataset.id === id); };
ctx.closeGameOptions = () => { elements.gameOptions.hidden = true; ctx.focusSelectedGame(); };
for (const isDesktop of [true, false]) {
  ctx.desktop = isDesktop;
  ctx.selectedTile = 0; ctx.focusSelectedGame();
  key(40); assert.equal(ctx.controllerFocus,elements.heroPlay,'Baixo na capa chega direto às ações');
  assert.equal(elements.gameSettings.hidden,true,'Baixo não revela metadados');
  key(38); assert.equal(ctx.controllerFocus,tiles[0]);
  assert.equal(elements.gameSettings.hidden, true, 'metadados não ficam visíveis na navegação normal');
  key(38); assert.equal(ctx.controllerFocus, elements.gameSettings);
  assert.equal(elements.gameSettings.hidden, false, 'primeiro Cima revela metadados sob a capa');
  assert.equal(elements.gameOptions.hidden, true, 'primeiro Cima não abre menu');
  key(38, true); assert.equal(elements.gameOptions.hidden, true, 'Cima mantido não abre menu');
  key(40); assert.equal(ctx.controllerFocus, tiles[0]); assert.equal(elements.gameSettings.hidden, true);
  key(38); key(10009); assert.equal(ctx.controllerFocus, tiles[0], 'bolinha cancela destaque sem sair do launcher');
  key(38); key(39); assert.equal(ctx.controllerFocus, elements.heroPlay, 'direita no destaque chega à barra de ações');
  assert.equal(elements.gameSettings.hidden, true);
  key(39); assert.equal(ctx.controllerFocus,elements.streamShortcut);
  key(39); assert.equal(ctx.controllerFocus,elements.settings);
  key(38); assert.equal(ctx.controllerFocus,tiles[0], 'Cima na barra retorna à capa');
  key(39); assert.equal(ctx.controllerFocus,tiles[1]); key(37);
  key(38); key(38); assert.equal(elements.gameOptions.hidden, false, 'segundo Cima abre menu');
  assert.equal(elements.gameSettings.hidden, true, 'destaque desaparece dentro do menu');
  ctx.focusControl(elements.heroPlay); key(13); assert.equal(ctx.controllerFocus, elements.closeGameOptions, 'painel do jogo contém foco');
  ctx.focusControl(elements.gameName); key(65376); assert.equal(ctx.controllerFocus, elements.saveGameName, 'IME nome devolve foco a Salvar');
  ctx.focusControl(elements.metadataQuery); key(13); assert.equal(ctx.controllerFocus, elements.searchMetadata, 'IME busca devolve foco a Buscar');
  key(10009); assert.equal(elements.gameOptions.hidden, true); assert.equal(ctx.controllerFocus, tiles[0]);
  assert.equal(elements.gameSettings.hidden, true, 'bolinha fecha menu e volta à capa');
  key(38); key(13); assert.equal(elements.gameOptions.hidden, false, 'OK também abre metadados destacados');
  key(10009);
  ctx.focusControl(elements.heroPlay); key(38); assert.equal(ctx.controllerFocus, tiles[0], 'Cima em Jogar volta ao carrossel acima');
}
assert.equal(requests.length,1,'opções do jogo não iniciam jogo, ocultam launcher ou trocam telas');
console.log('PASS: metadados em dois toques Cima, cancelamento/direções/OK, IME e bolinha em PC/TV.');
