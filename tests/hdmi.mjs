import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync('tv/app.js', 'utf8');
const functions = source.slice(source.indexOf('function videoSources()'), source.indexOf('async function connect()'));
function fixture(sources, fail = false) {
  const storage = new Map(), reports = [], elements = {
    options: { hidden: true }, hdmiSection: { hidden: true }, hdmiOptions: { textContent: '', appendChild() {}, querySelector() { return { focus() {} }; } }
  };
  const state = { selected: null, hidden: 0, message: '' };
  const context = { desktop: false,
    local: false, setTimeout, clearTimeout, localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, String(value)) },
    $: id => elements[id], button: () => ({}), message: text => state.message = text,
    api: async (path, report) => reports.push(report),
    tizen: {
      systeminfo: { getPropertyValue(type, done) { assert.equal(type, 'VIDEOSOURCE'); done({ connected: sources }); } },
      tvwindow: { setSource(selected, done, error, type) { assert.equal(type, 'MAIN'); if (fail) error(Error('rejeitado')); else { state.selected = selected.number; done(); } } },
      application: { launch(id, done) { assert.equal(id, 'org.tizen.tv-viewer'); assert.notEqual(state.selected, null, 'selecionar HDMI antes de abrir viewer'); state.hidden++; done(); } }
    }
  };
  vm.createContext(context);vm.runInContext(readFileSync('tv/platform.js','utf8'),context); vm.runInContext(functions, context);
  return { context, state, reports, storage, elements };
}
let f = fixture([{ type: 'HDMI', number: 2 }]);
await f.context.showHdmi(); assert.equal(f.state.selected, 2); assert.equal(f.state.hidden, 1);
assert.equal(f.reports[0].stage, 'source-selected');
f = fixture([{ type: 'HDMI', number: 1 }, { type: 'HDMI', number: 2 }]);
await f.context.showHdmi(); assert.equal(f.state.hidden, 0); assert.equal(f.elements.hdmiSection.hidden, false);
f.storage.set('lazygaming-hdmi', '2'); await f.context.showHdmi(); assert.equal(f.state.selected, 2);
f = fixture([{ type: 'HDMI', number: 1 }], true);
await f.context.showHdmi(); assert.equal(f.state.hidden, 0); assert.equal(f.reports[0].stage, 'switch-error');
assert.match(f.state.message, /não foi concluída/);
f = fixture([{ type: 'HDMI', number: 2 }]); f.storage.set('lazygaming-hdmi', '1');
await f.context.showHdmi(); assert.equal(f.state.selected, null, 'não trocar para Fire Stick quando o HDMI do PC estiver ausente');
console.log('PASS: HDMI único automático, escolha persistida, selecionar HDMI antes de abrir tv-viewer e falha sem falso sucesso.');
f = fixture([{ type: 'HDMI', number: 2 }]); f.storage.set('lazygaming-hdmi', '1');
let probes = 0;
f.context.setTimeout = (done, ms) => setTimeout(done, ms === 400 ? 0 : ms);
f.context.tizen.systeminfo.getPropertyValue = (_, done) => done({ connected: ++probes < 3 ? [{ type: 'HDMI', number: 2 }] : [{ type: 'HDMI', number: 1 }, { type: 'HDMI', number: 2 }] });
await f.context.showHdmi();
assert.equal(f.state.selected, 1, 'aguardar negociação HDMI do PC sem selecionar Fire Stick');
assert.equal(f.state.hidden, 1);
