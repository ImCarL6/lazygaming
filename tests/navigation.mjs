import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';

const source = readFileSync('tv/app.js', 'utf8');
const handler = source.slice(source.indexOf('function handleRemoteKey('), source.indexOf("document.addEventListener('keydown', handleRemoteKey)"));
const document = { activeElement: null };
const elements = {};
for (const id of ['settings', 'address', 'find', 'code', 'pair', 'reconnect', 'cancel', 'accept', 'closeOptions']) {
  elements[id] = { id, tagName: ['address', 'code'].includes(id) ? 'INPUT' : 'BUTTON', offsetParent: {}, disabled: false, clicks: 0,
    focus() { document.activeElement = this; }, blur() { document.activeElement = null; }, click() { this.clicks++; } };
}
elements.setup = { hidden: false }; elements.modal = { hidden: true }; elements.options = { hidden: true };
document.querySelectorAll = () => ['settings', 'address', 'find', 'code', 'pair', 'reconnect'].map(id => elements[id]);
for (const id of ['setup', 'modal', 'options']) elements[id].querySelectorAll = document.querySelectorAll;
const context = { desktop: false, setupParentOptions: false, controllerFocus: null, focusControl: e => e.focus(), lastNavigationAt: 0, document, $: id => elements[id], connected: true, focusSelectedGame: () => elements.settings.focus(), run: () => {}, refreshAudio: () => {} };
vm.createContext(context); vm.runInContext(handler, context);
function key(keyCode, repeat = false) {
  let prevented = false;
  context.handleRemoteKey({ keyCode, repeat, preventDefault() { prevented = true; } });
  return prevented;
}
for (const code of [65376, 65385, 13, 10009]) {
  elements.address.focus(); assert.equal(key(code), false, 'não interceptar edição do IME'); assert.equal(document.activeElement.id, 'find');
  elements.code.focus(); key(code); assert.equal(document.activeElement.id, 'pair');
  assert.equal(elements.setup.hidden, false, 'fechar teclado mantém conexão aberta');
}
elements.code.focus(); key(40); assert.equal(document.activeElement.id, 'code', 'setas continuam navegando no IME');
key(65376); assert.equal(document.activeElement.id, 'pair');
key(40); assert.equal(document.activeElement.id, 'reconnect', 'setas funcionam depois de Concluído');
key(38); assert.equal(document.activeElement.id, 'pair');
assert.equal(key(13), true); assert.equal(elements.pair.clicks, 1);
key(13, true); assert.equal(elements.pair.clicks, 1, 'OK mantido não repete comando');
key(10009); assert.equal(elements.setup.hidden, true);
key(10009); assert.equal(elements.options.hidden, true, 'bolinha em ação inferior volta à biblioteca');
document.activeElement = null; key(10009); assert.equal(elements.options.hidden, false); assert.equal(document.activeElement.id, 'closeOptions');
console.log('PASS: IME Concluído/Cancelar/Voltar/Enter libera os campos, mantém a tela e devolve foco aos botões; setas e OK único conferidos.');
