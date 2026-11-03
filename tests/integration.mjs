import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHmac, createHash, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { once } from 'node:events';

// Vetores independentes também validam o fallback HMAC usado no HTTP da TV.
vm.runInThisContext(readFileSync('tv/crypto.js', 'utf8'));
for (const text of ['', 'abc', 'TV — áudio e ação', 'x'.repeat(1000)]) {
  assert.equal(LGCrypto.sha(text), createHash('sha256').update(text).digest('hex'));
  const key = randomBytes(32).toString('hex');
  assert.equal(LGCrypto.hmac(key, text), createHmac('sha256', Buffer.from(key, 'hex')).update(text).digest('hex'));
}
const port = 47983, base = `http://127.0.0.1:${port}`;
const host = spawn('dotnet', [path.resolve('src/LazyGaming.Host/bin/Debug/net10.0-windows/LazyGaming.Host.dll'), '--test'], {
  env: { ...process.env, LAZYGAMING_PORT: String(port) }, stdio: ['ignore', 'pipe', 'pipe']
});
let output = ''; host.stdout.on('data', c => output += c); host.stderr.on('data', c => output += c);
let key, identity;
async function call(route, payload, headers = {}) {
  const response = await fetch(base + route, { method: payload === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json', ...headers }, body: payload === undefined ? undefined : JSON.stringify(payload) });
  const text = await response.text();
  let data; try { data = JSON.parse(text); } catch { data = { text }; }
  return { status: response.status, data };
}
function signed(route, payload, overrides = {}) {
  const time = overrides['X-LG-Time'] || String(Date.now()), nonce = randomBytes(16).toString('hex'), body = payload === undefined ? '' : JSON.stringify(payload);
  const method = payload === undefined ? 'GET' : 'POST';
  return { 'X-LG-Time': time, 'X-LG-Nonce': nonce, 'X-LG-Signature': createHmac('sha256', Buffer.from(key, 'hex'))
    .update(`${method}\n${route}\n${time}\n${nonce}\n${body}`).digest('hex'), ...overrides };
}
const api = (route, payload) => call(route, payload, signed(route, payload));
try {
  let ready = false;
  for (let i = 0; i < 60; i++) { try { await fetch(base + '/api/discover'); ready = true; break; } catch { await new Promise(r => setTimeout(r, 100)); } }
  assert.ok(ready, output);
  assert.equal((await call('/api/status')).status, 401);
  assert.equal((await call('/api/library/search?query=test')).status, 401);
  assert.equal((await call('/api/games/wilds/settings')).status, 401);
  assert.equal((await call('/api/games/wilds/settings', {name:'Test'})).status, 401);
  assert.equal((await call('/api/presentation')).status, 401);
  assert.equal((await call('/api/presentation/cable')).status, 401);
  assert.equal((await call('/api/stream/status')).status, 401);
  assert.equal((await call('/api/stream/configuration')).status, 401);
  assert.equal((await call('/api/stream/pair', {pin:'1234'})).status, 401);
  assert.equal((await call('/api/stream/confirm', {})).status, 401);
  assert.equal((await call('/api/stream/handoff')).status, 401);
  assert.equal((await call('/api/displays')).status, 401);
  assert.equal((await call('/api/displays', { monitorsEnabled: true })).status, 401);
  assert.equal((await call('/api/games/wilds/start', {})).status, 401);
  assert.equal((await call('/api/games/wilds/move-to-tv', {})).status, 401);
  const hostile = await call('/api/local-pairing', {}, { Origin: 'https://example.invalid' });
  assert.equal(hostile.status, 403);
  const code = (await call('/api/local-pairing', {})).data.code;
  assert.equal((await call('/api/pair', { code: '00000000' })).status, 409);
  const paired = await call('/api/pair', { code }); assert.equal(paired.status, 200);
  ({ key, identity } = paired.data);
  const gameBefore = (await api('/api/status')).data.state.games.find(g => g.id === 'wilds');
  assert.equal((await api('/api/games/wilds/settings', {name:'Minha biblioteca'})).status, 200);
  assert.equal((await api('/api/games/wilds/settings')).data.name, 'Minha biblioteca');
  const renamed = (await api('/api/status')).data.state.games.find(g => g.id === 'wilds');
  assert.equal(renamed.name, 'Minha biblioteca'); assert.equal(renamed.available, gameBefore.available);
  assert.equal(renamed.artworkId, gameBefore.artworkId); assert.equal(renamed.origin, gameBefore.origin);
  assert.equal((await api('/api/games/wilds/settings', {name:null})).status, 200);
  assert.equal((await api('/api/games/wilds/settings')).data.name, gameBefore.name);
  assert.equal((await api('/api/games/wilds/settings', {name:'x'.repeat(121)})).status, 409);
  assert.equal((await api('/api/games/wilds/settings', {storeId:'../invalid'})).status, 409);
  assert.equal((await api('/api/games/unknown/settings', {name:'Test'})).status, 409);
  assert.equal((await call('/api/pair', { code })).status, 409, 'código de uso único');
  const challenge = randomBytes(16).toString('hex');
  const discovered = (await call('/api/discover?challenge=' + challenge)).data;
  assert.equal(discovered.identity, identity);
  assert.equal(discovered.proof, createHmac('sha256', Buffer.from(key, 'hex')).update(`discover\n${challenge}\n${identity}`).digest('hex'));
  const statusHeaders = signed('/api/status');
  assert.equal((await call('/api/status', undefined, statusHeaders)).status, 200);
  assert.equal((await call('/api/status', undefined, statusHeaders)).status, 401, 'replay rejeitado');
  assert.equal((await api('/api/status')).data.controller.enabled, false);
  assert.equal((await call('/api/status', undefined, signed('/api/status', undefined, { 'X-LG-Time': '1' }))).status, 401);
  assert.equal((await call('/api/games/wilds/stop', { force: true }, signed('/api/games/wilds/stop', { force: false }))).status, 401, 'corpo adulterado rejeitado');
  assert.equal((await call('/desktop-session', {token:'invalid'}, {Origin:base})).status, 403);
  assert.equal((await call('/desktop-session')).status, 401);
  assert.equal((await call('/api/launcher/selection', {id:'wilds'})).status, 401);
  const shared = (await api('/api/launcher/sync?after=-1')).data;
  const nextSelection = api('/api/launcher/sync?after=' + shared.revision);
  await new Promise(r => setTimeout(r, 100));
  assert.equal((await api('/api/launcher/selection', {id:'wilds'})).status, 200);
  assert.equal((await nextSelection).data.id, 'wilds', 'seleção enviada ao outro cliente sem esperar polling');
  assert.equal((await api('/api/launcher/selection', {id:'unknown'})).status, 400);
  assert.equal((await api('/api/stream/configuration')).status, 409, 'simulação não divulga dados reais');
  assert.equal((await api('/api/stream/pair', {pin:'1234'})).status, 403, 'sem pareamento via cliente PC/simulação');
  assert.equal((await api('/api/stream/confirm', {})).status, 403);
  const presentation = await api('/api/presentation');
  const cableOnly = await api('/api/presentation/cable');
  assert.equal(cableOnly.status, 200);
  assert.deepEqual(cableOnly.data, presentation.data.cable, 'consulta de Cabo independente da verificação de Stream');
  assert.equal((await api('/api/displays', { monitorsEnabled: 'true' })).status, 400, 'perfil exige booleano');
  assert.equal((await api('/api/displays')).data.monitorsEnabled, true, 'monitores ligados sem preferência anterior');
  assert.equal((await api('/api/displays', { monitorsEnabled: true })).status, 200);
  assert.equal((await api('/api/displays')).data.monitorsEnabled, true);
  assert.equal((await api('/api/status')).data.state.environment.active, false, 'guardar perfil não prepara telas fora da sessão');
  assert.equal(presentation.data.cable.available, true);
  assert.equal(presentation.data.cable.active, false, 'TV inativa ainda permite Cabo');
  assert.equal(presentation.data.stream.available, false);
  assert.equal((await api('/api/stream/client', { installed: true })).status, 200);
  assert.equal((await api('/api/stream/status')).data.available, false, 'app instalado sozinho não comprova host pareado');
  assert.equal((await api('/api/stream/handoff')).status, 409, 'handoff bloqueado sem host pronto');
  assert.equal((await api('/api/launcher/background?client=paired-client-12345')).status, 403, 'pareamento LAN não dá acesso à captura privada PC');
  assert.equal((await api('/api/games/wilds/start', { mode: 'stream' })).status, 409);
  assert.equal((await api('/api/games/wilds/start', { mode: 'invalid' })).status, 409);
  assert.equal((await api('/api/status')).data.simulatedLaunches, 0, 'modo recusado não inicia jogo');
  assert.equal((await api('/api/status')).data.state.environment.active, false, 'modo recusado não prepara telas/áudio');
  assert.equal((await api('/api/games/wilds/move-to-tv', {})).status, 409, 'reparo não inicia jogo fechado');
  assert.equal((await api('/api/games/unknown/move-to-tv', {})).status, 409);
  const starts = await Promise.all(Array.from({ length: 8 }, () => api('/api/games/wilds/start', {})));
  assert.ok(starts.every(s => s.status === 200));
  assert.equal((await api('/api/status')).data.simulatedLaunches, 1, 'oito comandos abrem uma única instância');
  assert.equal((await api('/api/games/wilds/move-to-tv', {})).data.moved, true);
  assert.equal((await api('/api/status')).data.simulatedLaunches, 1, 'reparo autenticado move sem duplicar jogo');
  assert.equal((await api('/api/displays', { monitorsEnabled: false })).data.sessionActive, true);
  assert.equal((await api('/api/displays', { monitorsEnabled: true })).data.monitorsEnabled, true);
  assert.equal((await api('/api/status')).data.simulatedLaunches, 1, 'alternar monitores não reinicia jogo');
  assert.equal((await api('/api/games/mycopunk/start', {})).status, 409, 'troca não sobrepõe jogo');
  assert.equal((await api('/api/games/wilds/stop', { force: true })).status, 400, 'forçar exige confirmação');
  assert.equal((await api('/api/games/wilds/stop', { nextGame:'unknown',mode:'cable' })).status,409,'próximo jogo inválido não fecha atual');
  assert.equal((await api('/api/games/wilds/stop', { force: false })).status, 409, 'recusa de fechar não vira sucesso');
  assert.equal((await api('/api/games/mycopunk/start', {})).status, 409, 'troca continua bloqueada após recusa');
  assert.equal((await api('/api/games/wilds/stop', { force: true, confirm: 'LOSE_UNSAVED_PROGRESS',nextGame:'mycopunk',mode:'cable' })).status, 200);
  await new Promise(resolve=>setTimeout(resolve,6200));
  assert.equal((await api('/api/status')).data.state.environment.active,true,'troca preserva sessão além da tolerância normal de 5s');
  assert.equal((await api('/api/games/mycopunk/start', {})).status, 200);
  const state = (await api('/api/status')).data;
  assert.equal(state.simulation, true);
  assert.equal(state.state.games.find(g => g.id === 'mycopunk').running, true);
  assert.equal(state.state.games.find(g => g.id === 'wilds').running, false);
  assert.equal(state.state.environment.preparations, 1, 'troca rápida reaproveita sessão e evita outra transição HDMI');
  assert.equal((await api('/api/games/unknown/start', {})).status, 409);
  assert.equal((await fetch(base + '/')).status, 200);
  assert.equal((await fetch(base + '/crypto.js')).status, 200);
  assert.equal((await fetch(base + '/art/unknown/cover.jpg')).status, 404, 'arte não permite caminhos arbitrários');
  await api('/api/games/mycopunk/stop', { force: true, confirm: 'LOSE_UNSAVED_PROGRESS' });
  await new Promise(resolve => setTimeout(resolve, 6200));
  const idle = (await api('/api/status')).data;
  assert.equal(idle.state.environment.active, false, 'restaurar depois de fechar, mesmo sem app TV ativo');
  assert.equal(idle.state.environment.restorations, 1);
  const prefs={resolution:'auto',fps:60,bitrate:12,codec:'HEVC',adaptive:false};
  assert.equal((await fetch(base+'/api/stream/controls')).status,401);
  let streamShared=(await api('/api/stream/controls',{settings:prefs,action:'apply'})).data;
  assert.equal(streamShared.settings.bitrate,12);assert.equal(streamShared.command.action,'apply');
  await api('/api/stream/report',{completed:streamShared.command.id,active:true,profile:'1080p60 · 12 Mbps'});
  streamShared=(await api('/api/stream/controls')).data;
  assert.equal(streamShared.command,null);assert.equal(streamShared.active,true);assert.ok(streamShared.profile.includes('12 Mbps'));
  console.log('PASS: SHA/HMAC, autenticação, pareamento de uso único, origem local, descoberta autenticada, replay, validade, adulteração, comandos concorrentes, troca/fechamento, catálogo e interface HTTP. Nenhum jogo real executado.');
} catch (error) { console.error(output); throw error; } finally { const exited = once(host, 'exit'); host.kill(); await exited; }
