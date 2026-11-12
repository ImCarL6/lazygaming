import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
import {join} from 'node:path';

// Execute the entire Central script, including startup and its real event bindings.
const elements = new Map(), timers = new Map(), requests = [], listeners = {};
let timerId = 0, reply, pendingPick;
class Element {
  constructor(tag = 'div') { this.tagName = tag; this.children = []; this.dataset = {}; this.value = ''; this.hidden = false; this.classList = {add(){},remove(){},toggle(){}}; }
  set id(value) { this._id = value; elements.set(value, this); } get id() { return this._id; }
  append(...nodes) { for (const node of nodes) if (node && typeof node === 'object') node.parent = this; this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = []; this.append(...nodes); }
  setAttribute() {} toggleAttribute() {} addEventListener(name, fn) { this['on' + name] = fn; }
  closest(tag) { return this.tagName === tag ? this : this.parent?.closest(tag); }
  focus() { this.focused = true; } dispatchEvent() { this.inputDispatched = true; }
  showModal() { this.open = true; } close() { this.open = false; this.onclose?.(); } scrollIntoView() {}
  reset() { for (const id of ['addTitle','addExecutable','addContent','addCover','addBackdrop']) elements.get(id).value = ''; }
  querySelectorAll() { return []; }
}
const uiRoot = process.argv[2] || 'tv';
const html = readFileSync(join(uiRoot,'hub.html'), 'utf8');
for (const match of html.matchAll(/id="([^"]+)"/g)) { const node = new Element(); node.id = match[1]; }
const views = [...html.matchAll(/data-view="([^"]+)"/g)].map(m => Object.assign(new Element('button'), {dataset:{view:m[1]}}));
const picks = [...html.matchAll(/data-pick="([^"]+)" data-target="([^"]+)"/g)].map(m => Object.assign(new Element('button'), {dataset:{pick:m[1],target:m[2]}}));
const sections = ['library','folders','add','system'].map(id => elements.get('view-' + id));
const $ = id => { assert.ok(elements.has(id), 'element exists: ' + id); return elements.get(id); };
const snapshot = {games:[],registrations:[],folders:[],preferences:{steam:false,automatic:false,intervalMinutes:5,folders:[]},notes:[],lastScan:null};
const response = (value, status = 200) => ({ok:status < 400,status,json:async()=>value});
function abortable(signal, work) { return new Promise((resolve,reject) => { signal.addEventListener('abort', () => reject(Object.assign(Error('aborted'), {name:'AbortError'})), {once:true}); work(resolve,reject); }); }
const ctx = {
  AbortController, Event, Map, JSON, Error, Object, Number, Date,
  document:{getElementById:$,createElement:tag=>new Element(tag),visibilityState:'visible',body:new Element(),
    querySelector:()=>new Element(),querySelectorAll:query=>query === '[data-view]' ? views : query === '[data-pick]' ? picks : query === '.view' ? sections : []},
  window:{addEventListener:(name,fn)=>listeners[name]=fn},
  location:{hash:'',pathname:'/hub.html'},history:{replaceState(){}},sessionStorage:{getItem(){return null;},setItem(){}},
  LGControllerArt:{family:()=> 'generic',render(){}},LGNotices:{show:(node,text)=>{node.textContent=text;node.hidden=!text;}},
  setTimeout:(fn,ms)=>{timers.set(++timerId,{fn,ms});return timerId;},clearTimeout:id=>timers.delete(id),setInterval(){},
  fetch:async(path,options)=>{
    const body = options.body && JSON.parse(options.body); requests.push({path,body});
    if (reply) return reply(path, options);
    if (path === '/api/hub/pick') return abortable(options.signal, resolve=>pendingPick=resolve);
    if (path === '/api/hub/setup') return response({preferences:{setupCompleted:true},controller:{kind:'xinput'},reopenAvailable:false});
    if (path === '/api/hub/import') return response({...snapshot,games:[{id:'local-fixture',name:body.title || 'Ficha',available:true,enabled:true}]});
    return response(snapshot);
  }
};
vm.createContext(ctx); vm.runInContext(readFileSync(join(uiRoot,'hub.js'),'utf8'), ctx);
const flush = ()=>new Promise(resolve=>setImmediate(resolve));
await flush();
assert.equal($('connection').textContent,'Pronto','complete script initialized: ' + $('notice').textContent);
assert.equal($('content').hidden,false);

const executable = picks.find(n=>n.dataset.target==='addExecutable');
let selection = executable.onclick(); await flush();
assert.equal($('pickerStatus').hidden,false,'pending native selection remains visible');
assert.equal($('scan').disabled,false,'a native selection does not hold the global action lock');
const count = requests.length;
await $('importManifest').onclick(); assert.equal(requests.length,count,'second selection is not duplicated');
await $('scan').onclick(); assert.match($('notice').textContent,/Conclua ou cancele/,'other actions explain what is pending');
$('cancelPick').onclick(); await selection;
assert.equal($('pickerStatus').hidden,true);
assert.match($('notice').textContent,/cancelada/);
await $('scan').onclick(); assert.equal(requests.at(-1).path,'/api/hub/scan','scan works after cancel');

for (const node of picks) {
  selection = node.onclick(); await flush();
  assert.equal(requests.at(-1).body.kind,node.dataset.pick);
  pendingPick(response({path:'C:\\Fixture\\' + node.dataset.target})); await selection;
  assert.equal($(node.dataset.target).value,'C:\\Fixture\\' + node.dataset.target,'selected path fills '+node.dataset.target);
  assert.equal($(node.dataset.target).inputDispatched,true);
}
selection = $('importManifest').onclick(); await flush(); pendingPick(response({path:'C:\\Fixture\\game.json'})); await selection;
assert.equal(requests.at(-1).path,'/api/hub/import');
assert.equal(requests.at(-1).body.manifest,'C:\\Fixture\\game.json');
assert.match($('notice').textContent,/Ficha importada/);

$('addTitle').value = 'Meu jogo'; $('addExecutable').value = 'C:\\Fixture\\Game.exe';
const submit = $('addGame').onsubmit({preventDefault(){}}); await flush();
assert.equal(requests.at(-1).body.title,'Meu jogo'); assert.equal(requests.at(-1).body.executable,'C:\\Fixture\\Game.exe');
assert.match($('notice').textContent,/Jogo adicionado/); assert.equal($('addTitle').value,'');

selection = $('addFolder').onclick(); await flush(); pendingPick(response({path:'C:\\Fixture'})); await selection;
assert.equal(requests.at(-1).path,'/api/hub/settings'); assert.equal(requests.at(-1).body.folders[0].path,'C:\\Fixture');

selection = executable.onclick(); await flush(); pendingPick(response({path:null})); await selection;
assert.equal($('pickerStatus').hidden,true,'Windows Cancel also releases the selection');
reply = async()=>response({error:'Executável não encontrado.'},409);
$('addTitle').value='Preservar'; $('addExecutable').value='C:\\missing.exe';
$('addGame').onsubmit({preventDefault(){}}); await flush();
assert.match($('notice').textContent,/Executável não encontrado/); assert.equal($('addTitle').value,'Preservar','failure preserves fields');
reply = (path,options)=>abortable(options.signal,()=>{});
const scan = $('scan').onclick(); await flush();
const scanTimer = [...timers.values()].find(t=>t.ms===30000); assert.ok(scanTimer); scanTimer.fn(); await scan;
assert.match($('notice').textContent,/demorou/); assert.equal($('scan').disabled,false,'timeout releases action lock');
assert.notEqual($('offlinePanel').hidden,false,'timeout does not claim the helper is closed');
// A response whose body stalls has the same deadline as the request headers.
reply = async(path,options)=>({ok:true,status:200,json:()=>abortable(options.signal,()=>{})});
const slowBody = $('scan').onclick(); await flush(); [...timers.values()].find(t=>t.ms===30000).fn(); await slowBody;
assert.match($('notice').textContent,/demorou/); assert.equal($('scan').disabled,false);
reply = undefined; selection=executable.onclick(); await flush(); listeners.pagehide(); await selection;
assert.equal($('pickerStatus').hidden,true,'leaving the page cancels its picker');
// Repair uses an HTML modal: its cancel button must remain inside that modal.
$('gameDialog').open=true;
selection=picks.find(n=>n.dataset.target==='repairExecutable').onclick(); await flush();
assert.equal($('dialogPickerStatus').hidden,false);
$('cancelDialogPick').onclick(); await selection;
assert.equal($('dialogPickerStatus').hidden,true);
$('gameDialog').open=false;
await $('scan').onclick(); assert.match($('notice').textContent,/Busca concluída/,'another action works after every failure');
const zelda = {id:'wiki-Q213911',metadata:{title:'The Legend of Zelda: Ocarina of Time',year:1998,description:'Aventura de Link.',platforms:['Nintendo 64'],developers:['Nintendo'],publishers:['Nintendo'],genres:['Aventura'],source:'wikipedia',sourceUrl:'https://pt.wikipedia.org/wiki/Zelda'},cover:'/api/hub/catalog/art/wiki-Q213911/cover',backdrop:'/api/hub/catalog/art/wiki-Q213911/backdrop'};
reply = async(path)=>response(path.includes('/search?') ? {matches:[zelda],warnings:[]} : zelda);
$('addTitle').value='Ocarina of Time'; await $('catalogSearch').onclick();
assert.equal($('catalogResults').children.length,1,'search renders selectable cards');
await $('catalogResults').children[0].onclick();
assert.equal($('addTitle').value,zelda.metadata.title); assert.equal($('addYear').value,1998); assert.equal($('addDescription').value,'Aventura de Link.');
assert.equal($('catalogPreview').hidden,false); assert.equal($('catalogClear').hidden,false);
$('addTitle').value='Meu Zelda'; $('addTitle').oninput();
assert.match($('catalogStatus').textContent,/nome na biblioteca/,'custom title keeps the selected identity');
reply=undefined; $('addYear').value='1998'; $('addGame').onsubmit({preventDefault(){}}); await flush();
assert.equal(requests.at(-1).body.catalogId,zelda.id); assert.equal(requests.at(-1).body.title,'Meu Zelda'); assert.equal(requests.at(-1).body.platforms,'Nintendo 64');
assert.equal($('catalogPreview').hidden,true,'successful import resets identity and preview');
// A stale response must never replace a newer query or selection.
let staleResolve;
reply=(path,options)=>path.includes('First') ? new Promise(resolve=>staleResolve=resolve) : Promise.resolve(response({matches:[],warnings:['Steam indisponível.']}));
$('addTitle').value='First'; const stale=$('catalogSearch').onclick(); await flush();
$('addTitle').value='Second'; await $('catalogSearch').onclick(); staleResolve(response({matches:[zelda],warnings:[]})); await stale;
assert.equal($('catalogResults').children.length,0); assert.match($('catalogStatus').textContent,/Nenhuma ficha/); assert.match($('catalogStatus').textContent,/Steam indisponível/);
reply=async()=>response(zelda); await ctx.chooseCatalog(zelda); $('catalogClear').onclick();
assert.equal($('addDescription').value,''); assert.equal($('catalogPreview').hidden,true);
assert.match($('catalogStatus').textContent,/Cadastro manual/);
const wallpaper={id:'wp-'+'a'.repeat(32),title:'Zelda',source:'Wallhaven',width:3840,height:2160,sourceUrl:'https://wallhaven.cc/w/example',thumbnail:'/api/hub/wallpapers/image/wp-'+'a'.repeat(32)+'/thumbnail',preview:'/api/hub/wallpapers/image/wp-'+'a'.repeat(32)+'/full'};
$('addTitle').value='Zelda'; reply=async()=>response({images:[wallpaper],warnings:[],hasMore:true});
await $('findAddBackdrop').onclick(); assert.equal($('wallpaperDialog').open,true); assert.equal($('wallpaperGrid').children.length,1); assert.equal($('wallpaperMore').hidden,false);
$('wallpaperGrid').children[0].onclick(); assert.equal($('wallpaperLarge').src,wallpaper.preview); assert.match($('wallpaperCaption').textContent,/3840 × 2160/);
await $('wallpaperUse').onclick(); assert.equal($('wallpaperDialog').open,false); assert.equal($('addWallpaperPreview').hidden,false);
reply=undefined; $('addGame').onsubmit({preventDefault(){}}); await flush(); assert.equal(requests.at(-1).body.wallpaperId,wallpaper.id); assert.equal($('addWallpaperPreview').hidden,true);
let oldGallery;
reply=(path)=>path.includes('First') ? new Promise(resolve=>oldGallery=resolve) : Promise.resolve(response({images:[],warnings:['Wallhaven indisponível.'],hasMore:false}));
$('addTitle').value='First'; const pendingGallery=$('findAddBackdrop').onclick(); await flush(); $('wallpaperQuery').value='Second'; await $('wallpaperSearchForm').onsubmit({preventDefault(){}});
oldGallery(response({images:[wallpaper],warnings:[],hasMore:true})); await pendingGallery;
assert.equal($('wallpaperGrid').children.length,0); assert.match($('wallpaperStatus').textContent,/Wallhaven indisponível/,'gallery preserves partial-source warnings and ignores obsolete responses');
$('wallpaperDialog').close();
console.log('PASS: full Central, chooser lifecycle, catalog, custom names, wallpaper gallery/preview/import, failures and stale responses.');
