import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { resolve } from 'node:path';
const port = 47989, base = `http://127.0.0.1:${port}`;
const child = spawn('dotnet', [resolve(process.argv[2] || 'outputs/pc-package', 'LazyGaming.Host.dll'), '--test'], {
  env: { ...process.env, LAZYGAMING_PORT: String(port) }, stdio: ['ignore', 'ignore', 'pipe']
});
let error = ''; child.stderr.on('data', chunk => error += chunk);
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (child.exitCode !== null) throw Error('Pacote encerrou antes da verificação: ' + error);
    try { if ((await fetch(base + '/api/discover')).ok) { ready = true; break; } } catch { }
    await new Promise(done => setTimeout(done, 100));
  }
  assert.ok(ready, 'auxiliar publicado deve iniciar em simulação');
  const index = await fetch(base); assert.ok(index.ok);
  assert.match(await index.text(), /src="platform.js"/);
  const adapter = await fetch(base + '/platform.js'); assert.ok(adapter.ok);
  assert.match(await adapter.text(), /var LGPlatform/);
  const wasm = await fetch(base + '/stream-engine/moonlight-wasm.wasm'); assert.ok(wasm.ok);
  assert.equal(Buffer.from(await wasm.arrayBuffer()).subarray(0, 4).toString('hex'), '0061736d');
  assert.equal((await fetch(base + '/api/status')).status, 401, 'pacote preserva autenticação');
  console.log('PASS: pacote PC inicia isolado, serve interface/adaptador/runtime e preserva autenticação.');
} finally {
  if (child.exitCode === null) { const ended = once(child, 'exit'); child.kill(); await ended; }
}
