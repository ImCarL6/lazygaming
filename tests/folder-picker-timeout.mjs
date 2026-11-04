import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';

const source = readFileSync('tv/app.js', 'utf8');
const requests = [];
const context = {desktop:true, base:'http://127.0.0.1:47832', LGCrypto:{nonce:()=> 'fixture'},
  request:async (url, options, timeout) => { requests.push({url, options, timeout}); return {path:null}; }};
vm.createContext(context);
vm.runInContext(source.slice(source.indexOf('async function api('), source.indexOf('function button(')), context);
await context.api('/api/hub/pick', {kind:'folder'});
assert.equal(requests.at(-1).timeout,600000,'escolha humana não expira após cinco segundos');
await context.api('/api/status');
assert.equal(requests.at(-1).timeout,5000,'status continua detectando conexão indisponível rapidamente');
await context.api('/api/launcher/input');
assert.equal(requests.at(-1).timeout,25000,'long polling preservado');
console.log('PASS: seletor local aguarda a escolha humana sem ampliar prazos de status ou input.');
