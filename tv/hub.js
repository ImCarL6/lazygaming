'use strict';
const $ = id => document.getElementById(id);
let snapshot, selected, working = false, view = 'library', pcPreferences;
let setupPending = false, setupInitialized = false, setupControllerDetected = false, setupScreenSignature = '', setupReview = false, offline = false;
let capturePrefix, captureTimer, controllerKind, controllerName = "";
let swapEdited=false, controllerSourceChanged=false, controllerRefresh=false;
const bindings = new Map(), editedBindings = new Set();
async function refreshSetup() {
  const info = await api('/api/hub/setup'), p = info.preferences;
  setupPending = setupReview || !p.setupCompleted; $('setupPanel').hidden = !setupPending;
  controllerKind = info.controller.kind; rememberReopen(info.reopenAvailable);
  $('samsungHint').hidden = setupPending || !info.samsungHint;
  $('scan').hidden = setupPending;
  if (!setupPending) return;
  document.querySelectorAll('.view').forEach(n => n.hidden = true);
  $('pageTitle').textContent = 'Primeiro uso'; $('pageNote').textContent = 'Deixe o essencial pronto para jogar.';
  const chosen = setupInitialized ? $('setupDisplay').value : info.displayPath || '';
  const signature = JSON.stringify(info.screens);
  if (signature !== setupScreenSignature) {
    $('setupDisplay').replaceChildren(option('','Escolha a tela de jogo'),...info.screens.filter(s=>s.available).map((s,i)=>option(s.devicePath,(i+1)+' · '+s.name+' · '+s.width+' × '+s.height)));
    if (chosen && !info.screens.some(s=>s.available && s.devicePath===chosen)) $('setupDisplay').append(option(chosen,'Tela escolhida indisponível · reconecte ou escolha outra'));
    $('setupDisplay').value = chosen; setupScreenSignature = signature;
  }
  setupControllerDetected = info.controller.connected;
  $('setupController').textContent = info.controller.connected ? info.controller.name + ' detectado' : info.controller.name;
  $('setupControllerNote').textContent = info.controller.kind === 'dualsense' && !info.controller.microphone ? info.controller.note : '';
  $('setupControllerChoice').hidden = info.controller.connected;
  if (!setupInitialized) {
    $('setupControllerSource').value = p.controllerSource;
    const controls = info.controllerOptions || p.controller || p.controls; setupMouseOptions = controls;
    showShortcut('setupLauncherShortcut',controls.launcher || controls.launcherBinding);
    $('setupAudio').replaceChildren(option('','Áudio da tela de jogo · automático'),option('keep','Manter a saída atual'),...info.audio.map(d=>option(d.id,d.name)));
    $('setupAudio').value = p.audioDeviceId || '';
    $('setupOnlyDisplay').checked = p.displayPath ? info.onlyGameDisplay : true;
    setupInitialized = true;
  }
  if (!p.controller && !editedBindings.has('setupLauncherShortcut')) showShortcut('setupLauncherShortcut',info.controllerOptions.launcher || info.controllerOptions.launcherBinding);
  if (!p.controller && info.controller.connected) setupMouseOptions = info.controllerOptions;
}
let setupMouseOptions;
const padButtons = [[64,'Select / View / Create / −'],[128,'Start / Menu / Options / +'],[256,'Clique do analógico esquerdo'],[512,'Clique do analógico direito'],[16,'Superior esquerdo · LB / L1 / L'],[32,'Superior direito · RB / R1 / R'],[1024,'Gatilho esquerdo'],[2048,'Gatilho direito'],[1,'Botão inferior · A / X / B'],[2,'Botão da direita · B / círculo / A'],[4,'Botão da esquerda · X / quadrado / Y'],[8,'Botão superior · Y / triângulo / X'],[4096,'Microfone · DualSense'],[8192,'Touchpad · DualSense']];
function option(value, text) { const node = el('option', '', text); node.value = value; return node; }
function buttonSelect(id, multiple = false) { const node = el('select'); node.id = id; node.multiple = multiple; if (multiple) node.size = 4; node.append(...padButtons.map(([v,t]) => option(v,t))); return node; }
function shortcutField(prefix, title) {
  const group = el('fieldset'), legend = el('legend','',title); group.append(legend);
  const current = el('p','shortcut-value'); current.id = prefix + 'Value'; group.append(current);
  const actions = el('div','actions'), record = button('Escolher botão', () => beginCapture(prefix)); record.id=prefix+'Record';actions.append(record); group.append(actions);
  const hint = el('p','muted'); hint.id = prefix + 'Hint'; hint.setAttribute('role','status'); group.append(hint);
  const gesture = el('label','','Gesto'), select = el('select'); select.id = prefix + 'Gesture'; select.append(option('hold','Segurar'),option('double-tap','Duplo toque')); gesture.append(select); group.append(gesture);
  const hold = el('label','','Tempo ao segurar'), time = el('select'); hold.id = prefix + 'HoldLabel'; time.id = prefix + 'Hold'; for (const ms of [500,1000,1500,2000,3000]) time.append(option(ms,(ms/1000) + ' s')); hold.append(time); group.append(hold);
  select.onchange = () => { editedBindings.add(prefix); if(bindings.has(prefix))bindings.get(prefix).gesture=select.value; updateShortcut(prefix); };
  time.onchange = () => { editedBindings.add(prefix); if(bindings.has(prefix))bindings.get(prefix).holdMs=Number(time.value); updateShortcut(prefix); };
  return group;
}
$('shortcutFields').append(shortcutField('launcherShortcut','Abrir / ocultar launcher'),shortcutField('mouseShortcut','Ativar / desativar mouse'));
$('setupLauncherFields').append(shortcutField('setupLauncherShortcut','Abrir e ocultar o launcher'));
for (const id of ['leftClick','rightClick']) $(id).append(...padButtons.map(([v,t]) => option(v,t)));
function shortcutValue(prefix) { return { ...bindings.get(prefix), gesture: $(prefix+'Gesture').value, holdMs: Number($(prefix+'Hold').value) }; }
function buttonNames(value) {
  const names = controllerKind === 'dualsense' ? {64:'Create',128:'Options',256:'L3',512:'R3',16:'L1',32:'R1',1024:'L2',2048:'R2',1:'✕',2:'○',4:'□',8:'△',4096:'Microfone',8192:'Touchpad'} : controllerKind === 'switch-pro' ? {64:'−',128:'+',256:'L3',512:'R3',16:'L',32:'R',1024:'ZL',2048:'ZR',1:'B',2:'A',4:'Y',8:'X'} : {64:'View / Select',128:'Menu / Start',256:'L3',512:'R3',16:'LB',32:'RB',1024:'LT',2048:'RT',1:'A',2:'B',4:'X',8:'Y',4096:'Microfone',8192:'Touchpad'};
  return padButtons.filter(([v]) => (v & value) !== 0).map(([v]) => names[v] || (v === 1024 ? 'Gatilho esquerdo' : 'Gatilho direito')).join(' + ');
}
function updateShortcut(prefix) {
  const binding = bindings.get(prefix); if(!binding)return;
  $(prefix+'HoldLabel').hidden = $(prefix+'Gesture').value !== 'hold';
  const name=controllerName || (controllerKind==='dualsense'?'DualSense':controllerKind==='switch-pro'?'Nintendo':controllerKind==='xinput'?'Xbox / 8BitDo':'');
  $(prefix+'Value').textContent = (name?name+' · ':'') + buttonNames(binding.buttons) + ' · ' + ($(prefix+'Gesture').value === 'double-tap' ? 'duplo toque' : 'segurar ' + Number($(prefix+'Hold').value)/1000 + ' s');
}
function showShortcut(prefix, binding) { bindings.set(prefix,{...binding}); $(prefix+'Gesture').value = binding.gesture; $(prefix+'Hold').value = binding.holdMs; updateShortcut(prefix); }
async function beginCapture(prefix) {
  if(capturePrefix === prefix) { await api('/api/hub/controller-capture/cancel',{}); $(prefix+'Hint').textContent='';endCapture();return; }
  if(capturePrefix) await api('/api/hub/controller-capture/cancel',{});
  endCapture(); await api('/api/hub/controller-capture',{}); capturePrefix = prefix; $(prefix+'Record').textContent='Cancelar gravação';
  document.querySelectorAll('#initialSetup button[type=submit],#pcSettings button[type=submit]').forEach(n=>n.disabled=true);
  async function poll() {
    try {
      const state = await api('/api/hub/controller-capture');
      if(capturePrefix !== prefix)return;
      if(state.phase === 'done') { const previous=shortcutValue(prefix);showShortcut(prefix,state.singlePress ? {...previous,buttons:state.binding.buttons} : state.binding); editedBindings.add(prefix); $(prefix+'Hint').textContent = 'Botão escolhido: '+buttonNames(state.binding.buttons); endCapture(); return; }
      if(state.phase === 'timeout' || state.phase === 'idle') { $(prefix+'Hint').textContent='Não reconhecemos um atalho. Tente novamente.'; endCapture(); return; }
      $(prefix+'Hint').textContent = state.phase === 'release' ? 'Solte os botões do controle' : 'Aperte o botão que deseja usar';
      captureTimer=setTimeout(poll,120);
    } catch(error) { $(prefix+'Hint').textContent=error.message;endCapture(); }
  }
  await poll();
}
function endCapture() {
  clearTimeout(captureTimer);capturePrefix=null;
  for(const prefix of ['setupLauncherShortcut','launcherShortcut','mouseShortcut'])$(prefix+'Record').textContent='Escolher botão';
  document.querySelectorAll('#initialSetup button[type=submit],#pcSettings button[type=submit]').forEach(n=>n.disabled=false);
}
function pointerLabels() { $('mouseSpeedValue').textContent = $('mouseSpeed').value; $('mouseDeadzoneValue').textContent = $('mouseDeadzone').value + '%'; $('mouseCurveValue').textContent = $('mouseCurve').value; }
for (const id of ['mouseSpeed','mouseDeadzone','mouseCurve']) $(id).oninput = pointerLabels;
const titles = { library: ['Biblioteca', 'Confira os jogos e gerencie o que aparece no launcher.'], folders: ['Pastas e busca', 'Defina onde encontrar jogos e quando atualizar a biblioteca.'], add: ['Adicionar jogo', 'Traga uma instalação local para o seu launcher.'], system: ['Configurações', 'Ajuste as opções do LazyGaming neste PC.'] };
function notice(text, error = false) { $('notice').textContent = text; $('notice').hidden = !text; $('notice').classList.toggle('error', error); if ($('gameDialog').open) $('dialogNotice').textContent = text; }
async function api(path, body) {
  let response;
  try { response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) }); }
  catch (_) { showOffline(false); throw Error('Abra o LazyGaming para continuar.'); }
  if(response.status === 401) { showOffline(true); throw Error('Reabra a Central para continuar.'); }
  const result = await response.json(); if (!response.ok) throw Object.assign(Error(result.error || 'Não foi possível concluir a operação.'), { status: response.status }); return result;
}
function rememberReopen(available) { if(available)sessionStorage.setItem('lg-reopen','yes'); $('reopenClient').hidden = sessionStorage.getItem('lg-reopen') !== 'yes'; }
function showOffline(expired) {
  offline=true; endCapture(); $('offlinePanel').hidden=false; $('notice').hidden=true;
  $('offlineTitle').textContent=expired ? 'Reabra a Central' : 'LazyGaming fechado';
  $('offlineText').textContent=expired ? 'A conexão desta janela expirou.' : 'Abra o aplicativo para continuar.';
  $('connection').textContent='Fechado';document.body.classList.add('offline');
  $('content').inert=true;document.querySelector('nav').inert=true;$('scan').disabled=true;
  rememberReopen(false);
  window.scrollTo({top:0,left:0,behavior:'instant'});
  $('offlinePanel').scrollIntoView({block:'start',behavior:'instant'});
  $('offlineTitle').focus({preventScroll:true});
}
function controllerStatus(info) {
  const c=info.controller;controllerKind=c.kind;controllerName=c.connected?c.name:'';
  const text=c.connected ? c.name+' detectado' : 'Conecte seu controle';
  $('detectedController').textContent=text;
  $('controllerCapability').textContent=c.kind==='dualsense' && !c.microphone ? c.note : '';
  $('legacyMicLabel').hidden=c.kind!=='dualsense' && $('controllerSource').value!=='dualsense';$('bluetoothFeaturesLabel').hidden=$('legacyMicLabel').hidden;
  $('setupController').textContent=text;
  if(setupPending){setupControllerDetected=c.connected;$('setupControllerChoice').hidden=c.connected;}
  if(!swapEdited && !controllerSourceChanged)$('swapButtons').checked=info.controllerOptions.swapConfirmBack;
  for(const prefix of bindings.keys())updateShortcut(prefix);
  if(!setupPending && pcPreferences && !capturePrefix) {
    for(const [prefix,key] of [['launcherShortcut','launcher'],['mouseShortcut','mouse']])
      if(!editedBindings.has(prefix))showShortcut(prefix,info.controllerOptions[key] || info.controllerOptions[key+'Binding']);
  }
  for(const dt of $('systemStatus').querySelectorAll('dt'))if(dt.textContent==='Controle')dt.nextElementSibling.textContent=text;
}
async function refreshController() {
  if(offline || controllerRefresh || document.visibilityState!=='visible')return;
  controllerRefresh=true;
  try{controllerStatus(await api('/api/hub/controller'));}catch(_){}finally{controllerRefresh=false;}
}
async function run(action, message) {
  if (working) return; working = true; $('scan').disabled = true;
  try { await action(); if (message) notice(message); }
  catch (error) { if(!offline)notice(error.message, true); }
  finally { working = false; $('scan').disabled = offline; }
}
function el(tag, className, text) { const node = document.createElement(tag); if (className) node.className = className; if (text !== undefined) node.textContent = text; return node; }
function button(text, action) { const node = el('button', '', text); node.type = 'button'; node.onclick = () => run(action); return node; }
function time(value) { return value ? new Date(value).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : 'Ainda não realizada'; }
function rows() {
  const rows = [...snapshot.games];
  for (const registration of snapshot.registrations) if (!rows.some(g => g.id === registration.id)) rows.push({ ...registration, origin: 'Cadastro local', enabled: !(snapshot.preferences.excluded || []).includes(registration.id) });
  return rows;
}
function render() {
  const games = rows(); $('total').textContent = games.filter(g => g.enabled).length; $('navCount').textContent = games.length;
  $('available').textContent = games.filter(g => g.enabled && g.available).length; $('attention').textContent = games.filter(g => !g.available).length; $('lastScan').textContent = time(snapshot.lastScan);
  $('steam').checked = snapshot.preferences.steam; $('automatic').checked = snapshot.preferences.automatic; $('interval').value = snapshot.preferences.intervalMinutes;
  $('nextScan').textContent = snapshot.preferences.automatic ? 'Próxima busca: ' + (snapshot.nextScan ? time(snapshot.nextScan) : 'agendada pelo auxiliar') : 'Busca automática pausada.';
  $('folderList').replaceChildren(...snapshot.folders.map(folder => {
    const row = el('div', 'folder-row'), info = el('div'); info.append(el('div', 'folder-path', folder.path), el('span', 'badge ' + (folder.available ? '' : 'warning'), folder.available ? 'Disponível' : 'Pasta indisponível'));
    const label = el('label', 'toggle-line'), toggle = el('input'); toggle.type = 'checkbox'; toggle.checked = folder.enabled; toggle.setAttribute('aria-label', 'Buscar em ' + folder.path);
    toggle.onchange = () => run(async () => { await saveSettings({ folders: snapshot.preferences.folders.map(f => f.path === folder.path ? { ...f, enabled: toggle.checked } : f) }); }, 'Pasta atualizada.').finally(() => render()); label.append(toggle);
    row.append(info, label, button('Remover', async () => { await saveSettings({ folders: snapshot.preferences.folders.filter(f => f.path !== folder.path) }); notice('Pasta removida da busca. Os arquivos foram preservados.'); })); return row;
  }));
  $('scanNotes').replaceChildren(...(snapshot.notes.length ? snapshot.notes : ['Nenhum aviso na última busca.']).map(text => el('li', '', text)));
  renderGames();
}
function renderGames() {
  if (!snapshot) return;
  const query = $('search').value.trim().toLocaleLowerCase('pt-BR'), filter = $('filter').value;
  const games = rows().filter(g => g.name.toLocaleLowerCase('pt-BR').includes(query) && (filter !== 'attention' || !g.available) && (filter !== 'local' || g.origin !== 'Steam') && (filter !== 'hidden' || !g.enabled));
  $('empty').hidden = games.length > 0;
  $('gameList').replaceChildren(...games.map(game => {
    const row = el('article', 'game-row');
    const cover = game.cover ? el('img') : el('div', 'cover-placeholder', game.name.charAt(0)); if (game.cover) { cover.src = game.cover; cover.alt = ''; cover.loading = 'lazy'; }
    const title = el('div'); title.append(el('div', 'game-name', game.name), el('div', 'game-source', game.origin));
    const status = el('span', 'badge ' + (!game.available ? 'warning' : !game.enabled ? 'off' : ''), !game.available ? 'Indisponível' : !game.enabled ? 'Oculto' : 'Disponível');
    row.append(cover, title, status, el('span', 'game-source', game.origin), button('Gerenciar', async () => openGame(game.id))); return row;
  }));
}
function paths(target, values) { target.replaceChildren(); for (const [label, value] of values) if (value) target.append(el('dt', '', label), el('dd', '', value)); }
function openGame(id) {
  selected = rows().find(g => g.id === id); if (!selected) return;
  const registration = snapshot.registrations.find(r => r.id === id);
  $('detailOrigin').textContent = selected.origin; $('detailTitle').textContent = selected.name; $('detailName').value = selected.name;
  $('detailStatus').textContent = selected.available ? (selected.enabled ? 'Disponível na biblioteca.' : 'Oculto no launcher.') : registration?.reason || 'Instalação indisponível. Confira a pasta e o executável.';
  $('detailDescription').textContent = selected.metadata?.description || '';
  paths($('detailPaths'), [['Pasta', selected.folder], ['Executável', selected.executable], ['Conteúdo', selected.contentPath]]);
  $('rename').disabled = !snapshot.games.some(g => g.id === id); $('toggleGame').disabled = $('rename').disabled;
  $('toggleGame').textContent = selected.enabled ? 'Ocultar no launcher' : 'Mostrar no launcher';
  $('repairFields').hidden = !registration; $('repairExecutable').value = selected.executable || ''; $('repairContent').value = selected.contentPath || '';
  $('repairContentLabel').hidden = !selected.contentPath && !/eglantine\.exe$/i.test(selected.executable || '');
  $('dialogNotice').textContent = ''; if (!$('gameDialog').open) $('gameDialog').showModal();
}
async function reload() {
  snapshot = await api('/api/hub'); $('content').hidden = false; $('locked').hidden = true; $('connection').textContent = 'Pronto'; render();
}
async function saveSettings(change) { snapshot = await api('/api/hub/settings', { ...snapshot.preferences, ...change }); render(); }
async function addFolder(path) {
  if (!path) return; await saveSettings({ folders: [...snapshot.preferences.folders, { path, enabled: true }] }); $('folderPath').value = ''; notice('Pasta adicionada e biblioteca atualizada.');
}
async function system() {
  const [state, pc] = await Promise.all([api('/api/status'), api('/api/hub/pc-settings')]);
  const audio = pc.audio; pcPreferences = pc.preferences;
  const p = pcPreferences, c = pc.controllerOptions || p.controller || p.controls; controllerKind=pc.controller.kind; rememberReopen(pc.reopenAvailable);
  controllerStatus(pc);swapEdited=false;controllerSourceChanged=false;
  $('legacyMicLabel').hidden = pc.controller.kind !== 'dualsense' && p.controllerSource !== 'dualsense';
  $('bluetoothFeaturesLabel').hidden=$('legacyMicLabel').hidden;$('bluetoothFeatures').checked=p.dualSenseBluetoothFeatures!==false;
  $('tvIntegration').checked = p.tvIntegrationEnabled;
  $('tvCode').disabled = !pc.tvSetup.lanEnabled;
  $('tvIntegrationNote').textContent = (pc.tvSetup.lanEnabled ? 'Acesso pela rede local ativo. ' : 'Acesso à TV desativado neste momento. ') +
    (p.tvIntegrationEnabled !== pc.tvSetup.lanEnabled ? 'Salve e reinicie o auxiliar para aplicar. ' : '') +
    (pc.tvSetup.commandLineLan ? 'O atalho atual também ativa o acesso pela rede. ' : '') +
    (pc.tvSetup.paired ? 'Já existe um vínculo com TV; preserve-o para reconectar. ' : 'Nenhuma TV pareada. ') + pc.tvSetup.addresses.join(' · ');
  $('gameDisplay').replaceChildren(option('','Automático · tela única ou único HDMI'),...pc.screens.filter(s => s.available).map((s,i) => option(s.devicePath, (i+1) + ' · ' + s.name + ' · ' + s.width + ' × ' + s.height + ' · ' + Math.round(s.hz) + ' Hz')));
  if (p.displayPath && !pc.screens.some(s => s.available && s.devicePath === p.displayPath)) $('gameDisplay').append(option(p.displayPath,'Tela salva indisponível · reconecte ou escolha outra'));
  $('gameDisplay').value = p.displayPath || ''; $('restoreTv').checked = p.restoreTvState;
  $('sessionAudio').replaceChildren(option('','Automático · áudio da tela de jogo'),option('keep','Manter saída atual do Windows'),...audio.map(d => option(d.id,d.name)));
  if (p.audioDeviceId && p.audioDeviceId !== 'keep' && !audio.some(d => d.id === p.audioDeviceId)) $('sessionAudio').append(option(p.audioDeviceId,'Saída salva indisponível'));
  $('sessionAudio').value = p.audioDeviceId || '';
  $('controllerSource').value = p.controllerSource; $('swapButtons').checked = c.swapConfirmBack; $('legacyMic').checked = c.legacyMicrophone;
  showShortcut('launcherShortcut',c.launcher || c.launcherBinding); showShortcut('mouseShortcut',c.mouse || c.mouseBinding);
  $('mouseSpeed').value = c.sensitivity; $('mouseDeadzone').value = Math.round(c.deadzone*100); $('mouseCurve').value = c.curve;
  $('leftClick').value = c.leftClick; $('rightClick').value = c.rightClick; pointerLabels();
  $('monitors').checked = state.displays.monitorsEnabled;
  $('displayNote').textContent = state.displays.note || '';
  $('audio').replaceChildren(...audio.map(device => button(device.name + (device.current ? ' · Em uso' : ''), async () => { await api('/api/audio', { id: device.id }); await system(); notice('Saída de áudio atualizada.'); })));
  if (!audio.length) $('audio').append(el('p', 'muted', 'Nenhuma saída disponível nesta sessão.'));
  const controller = state.controller || {};
  $('activity').checked = state.enabled;
  paths($('systemStatus'), [['Aplicativo', state.enabled ? 'Ativo neste PC' : 'Pausado'], ['Versão', state.version], ['Biblioteca', state.state.games.length + ' jogos'], ['Controle', controller.connected ? 'Conectado' : 'Confira a conexão USB ou Bluetooth'], ['Busca automática', snapshot.preferences.automatic && state.enabled ? 'Ativada' : 'Pausada']]);
}
document.querySelectorAll('[data-view]').forEach(node => node.onclick = () => {
  if (setupPending) { $('setupPanel').scrollIntoView({ behavior: 'smooth' }); return; }
  view = node.dataset.view; document.querySelectorAll('[data-view]').forEach(n => n.toggleAttribute('aria-current', false)); node.setAttribute('aria-current', 'page');
  document.querySelectorAll('.view').forEach(n => n.hidden = n.id !== 'view-' + view);
  $('pageTitle').textContent = titles[view][0]; $('pageNote').textContent = titles[view][1]; if (view === 'system') run(system);
});
$('controllerSource').onchange=()=>{controllerSourceChanged=true;swapEdited=false;$('swapButtons').checked=$('controllerSource').value==='switch-pro' || $('controllerSource').value==='auto' && controllerKind==='switch-pro';};
$('swapButtons').onchange=()=>{swapEdited=true;};
$('search').oninput = renderGames; $('filter').onchange = renderGames;
$('scan').onclick = () => run(async () => { notice('Buscando jogos instalados…'); snapshot = await api('/api/hub/scan', {}); render(); notice('Busca concluída. ' + snapshot.games.filter(g => g.enabled).length + ' jogos na biblioteca.'); });
$('steam').onchange = () => run(() => saveSettings({ steam: $('steam').checked }), 'Busca Steam atualizada.').finally(render);
$('automatic').onchange = () => run(() => saveSettings({ automatic: $('automatic').checked }), 'Busca automática atualizada.').finally(render);
$('interval').onchange = () => run(() => saveSettings({ intervalMinutes: Number($('interval').value) }), 'Intervalo atualizado.').finally(render);
$('addFolder').onclick = () => run(async () => addFolder((await api('/api/hub/pick', { kind: 'folder' })).path));
$('saveFolder').onclick = () => run(() => addFolder($('folderPath').value.trim()));
document.querySelectorAll('[data-pick]').forEach(node => node.onclick = () => run(async () => { const result = await api('/api/hub/pick', { kind: node.dataset.pick }); if (result.path) $(node.dataset.target).value = result.path; }));
$('addGame').onsubmit = event => { event.preventDefault(); run(async () => {
  snapshot = await api('/api/hub/import', { title: $('addTitle').value.trim(), executable: $('addExecutable').value.trim(), contentPath: $('addContent').value.trim(), cover: $('addCover').value.trim(), backdrop: $('addBackdrop').value.trim() }); render(); $('addGame').reset(); notice('Jogo adicionado à biblioteca.');
}); };
$('importManifest').onclick = () => run(async () => { const result = await api('/api/hub/pick', { kind: 'manifest' }); if (!result.path) return; snapshot = await api('/api/hub/import', { manifest: result.path }); render(); notice('Ficha importada.'); });
$('rename').onclick = () => run(async () => { await api('/api/games/' + selected.id + '/settings', { name: $('detailName').value.trim() }); await reload(); openGame(selected.id); notice('Nome salvo.'); });
$('toggleGame').onclick = () => run(async () => { snapshot = await api('/api/hub/games/' + selected.id + '/enabled', { enabled: !selected.enabled }); render(); openGame(selected.id); notice('Biblioteca atualizada.'); });
$('checkGame').onclick = () => run(async () => { const id = selected.id; snapshot = await api('/api/hub/scan', {}); render(); openGame(id); notice(selected.available ? 'Executável encontrado. Nenhum jogo foi aberto.' : 'Instalação indisponível. Confira o caminho abaixo.'); });
$('repair').onclick = () => run(async () => { const id = selected.id; snapshot = await api('/api/hub/repair', { id, executable: $('repairExecutable').value.trim(), contentPath: $('repairContent').value.trim() }); render(); openGame(id); notice('Instalação atualizada; nome e ficha preservados.'); });
$('monitors').onchange = () => run(async () => { await api('/api/displays', { monitorsEnabled: $('monitors').checked }); await system(); }, 'Preferência de monitores salva.');
$('activity').onchange = () => run(async () => { await api('/api/hub/activity', { enabled: $('activity').checked }); await system(); }, 'Funcionamento atualizado.');
$('pcSettings').onsubmit = event => { event.preventDefault(); run(async () => {
  await api('/api/hub/pc-settings', { ...pcPreferences, tvIntegrationEnabled: $('tvIntegration').checked, displayPath: $('gameDisplay').value || null, restoreTvState: $('restoreTv').checked, audioDeviceId: $('sessionAudio').value || null, controllerSource: $('controllerSource').value,
    confirmBackOverride:swapEdited ? $('swapButtons').checked : controllerSourceChanged ? null : pcPreferences.confirmBackOverride,dualSenseBluetoothFeatures:$('bluetoothFeatures').checked,
    controller: { launcher: editedBindings.has('launcherShortcut') ? shortcutValue('launcherShortcut') : pcPreferences.controller?.launcher ?? null, mouse: editedBindings.has('mouseShortcut') ? shortcutValue('mouseShortcut') : pcPreferences.controller?.mouse ?? null, sensitivity: Number($('mouseSpeed').value), deadzone: Number($('mouseDeadzone').value)/100, curve: Number($('mouseCurve').value), leftClick: Number($('leftClick').value), rightClick: Number($('rightClick').value), swapConfirmBack: $('swapButtons').checked, legacyMicrophone: $('legacyMic').checked } });
  await system(); notice('Configuração do PC salva.');
}); };
async function start() {
  try {
    const token = location.hash.slice(1); history.replaceState(null, '', location.pathname);
    if (token) {
      let response;try { response = await fetch('/hub-session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) }); }catch(_){showOffline(false);return;}
      if (!response.ok) {showOffline(true);return;}
    }
    await refreshSetup(); await reload(); offline=false; document.body.classList.remove('offline'); $('offlinePanel').hidden=true;$('content').inert=false;document.querySelector('nav').inert=false;$('scan').disabled=false;
  } catch (error) { if(!offline){$('locked').hidden = false; $('content').hidden = true; $('connection').textContent = 'Aguardando acesso'; notice(error.message, true);} }
}
$('retry').onclick = start;
$('initialSetup').onsubmit = event => { event.preventDefault(); run(async () => {
  $('setupError').textContent = '';
  try {
    await api('/api/hub/setup', { displayPath: $('setupDisplay').value, controllerSource: setupControllerDetected ? 'auto' : $('setupControllerSource').value, audioDeviceId: $('setupAudio').value || null, onlyGameDisplay: $('setupOnlyDisplay').checked, controller: { ...setupMouseOptions,launcher:shortcutValue('setupLauncherShortcut') } });
    setupReview=false; await refreshSetup(); await reload(); document.querySelector('[data-view="library"]').click(); notice('Configuração salva.');
  } catch (error) { $('setupError').textContent = error.message; throw error; }
}); };
$('samsungLearn').onclick = () => { document.querySelector('[data-view="system"]').click(); setTimeout(() => $('tvIntegration').scrollIntoView({behavior:'smooth',block:'center'}),100); };
$('samsungDismiss').onclick = () => run(async () => { await api('/api/hub/samsung-hint/dismiss',{}); $('samsungHint').hidden = true; });
$('tvCode').onclick = () => run(async () => {
  const pc = await api('/api/hub/pc-settings');
  if (pc.tvSetup.paired && !confirm('Parear uma nova TV substitui o vínculo atual. Para a mesma TV, prefira Recuperar conexão no app. Gerar código?')) return;
  const result = await api('/api/local-pairing', {}); $('tvPairCode').textContent = 'Código: ' + result.code + ' · válido por 5 minutos';
  setTimeout(() => { $('tvPairCode').textContent = ''; }, result.expiresIn * 1000);
});
$('quitHelper').onclick = () => run(async () => {
  await api('/api/hub/quit', {}); showOffline(false);
});
$('reviewSetup').onclick = () => run(async () => { setupReview=true;setupInitialized=false;await refreshSetup(); });
start();
setInterval(() => { if (!offline && !working && snapshot && !$('gameDialog').open && document.visibilityState === 'visible') reload().catch(error => { if(!offline)notice(error.message, true); }); }, 15000);
setInterval(() => { if (!offline && setupPending && !working && document.visibilityState === 'visible') refreshSetup().catch(error => { if(!offline)$('setupError').textContent = error.message; }); },2000);
setInterval(refreshController,750);
