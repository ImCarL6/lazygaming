import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync('tv/platform.js', 'utf8');
const ctx = { Promise, Error, setTimeout, clearTimeout };
vm.createContext(ctx); vm.runInContext(source, ctx);
assert.equal(ctx.LGPlatform.tv.networkIp(), '');
assert.equal(ctx.LGPlatform.tv.appInstalled('example'), false);
await assert.rejects(ctx.LGPlatform.tv.sources(), /HDMI indisponível/);
assert.equal(ctx.LGPlatform.tv.streamLimits().width, 1920);
let calls = [];
ctx.webapis = { network: { getIp: () => '192.168.1.2' }, productinfo: { isUdPanelSupported: () => true } };
ctx.tizen = {
  systeminfo: { getPropertyValue(_, done) { done({ connected: [{ type: 'HDMI', number: 1 }, { type: 'TV', number: 0 }] }); } },
  tvwindow: { getSource: () => ({ type: 'HDMI', number: 1 }), setSource(value, done) { calls.push(value.number); done(); } },
  application: { launch(id, done) { calls.push(id); done(); }, getCurrentApplication: () => ({ exit() { calls.push('exit'); } }) }
};
assert.equal(ctx.LGPlatform.tv.networkIp(), '192.168.1.2');
assert.equal(ctx.LGPlatform.tv.streamLimits().width, 3840);
const hdmi = await ctx.LGPlatform.tv.sources(); assert.equal(hdmi.length, 1);
await ctx.LGPlatform.tv.selectSource(hdmi[0]); await ctx.LGPlatform.tv.openViewer();
ctx.LGPlatform.tv.exit(); assert.deepEqual(calls, [1, 'org.tizen.tv-viewer', 'exit']);
ctx.tizen.application.launch = (_, done, fail) => fail(Error('rejeitado'));
await assert.rejects(ctx.LGPlatform.tv.openViewer(), /rejeitado/);
const html = readFileSync('tv/index.html', 'utf8');
assert.ok(html.indexOf('src="platform.js"') < html.indexOf('src="app.js"'));
console.log('PASS: integrações Tizen isoladas, ausência no PC, HDMI, viewer, limites e falhas propagadas.');
