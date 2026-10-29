import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync('tv/app.js', 'utf8');
const code = source.slice(source.indexOf('function pollConnection()'), source.indexOf("document.addEventListener('visibilitychange', pollConnection)"));
let calls = [];
const refresh = () => {}, connect = () => {}, setup = { hidden: false };
const context = { desktop: false, session: {}, busy: false, connected: false, needsPair: false,
  document: { hidden: false, activeElement: { tagName: 'BUTTON' } }, $: () => setup, refresh, connect, run: fn => calls.push(fn) };
vm.createContext(context); vm.runInContext(code, context);
context.pollConnection(); assert.equal(calls.pop(), connect, 'reconectar após falha inicial com setup aberto');
context.connected = true; setup.hidden = true; context.pollConnection(); assert.equal(calls.pop(), refresh);
context.connected = false; context.document.activeElement.tagName = 'INPUT'; context.pollConnection(); assert.equal(calls.length, 0, 'não interromper edição');
context.document.activeElement.tagName = 'BUTTON'; context.needsPair = true; context.pollConnection(); assert.equal(calls.length, 0, 'não insistir com identidade incorreta');
context.needsPair = false; context.document.hidden = true; context.pollConnection(); assert.equal(calls.length, 0);
console.log('PASS: recuperação inicial e após desconexão, edição preservada, identidade incorreta e app oculto sem tentativas indevidas.');
