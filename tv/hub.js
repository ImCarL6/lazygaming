'use strict';
const $ = id => document.getElementById(id);
let snapshot, selected, working = false, view = 'library';
const titles = { library: ['Biblioteca', 'Confira os jogos e gerencie o que aparece no launcher.'], folders: ['Pastas e busca', 'Defina onde encontrar jogos e quando atualizar a biblioteca.'], add: ['Adicionar jogo', 'Traga uma instalação local para o seu launcher.'], system: ['Configurações', 'Ajuste as opções do LazyGaming neste PC.'] };
function notice(text, error = false) { $('notice').textContent = text; $('notice').hidden = !text; $('notice').classList.toggle('error', error); if ($('gameDialog').open) $('dialogNotice').textContent = text; }
async function api(path, body) {
  const response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  const result = await response.json(); if (!response.ok) throw Object.assign(Error(result.error || 'Não foi possível concluir a operação.'), { status: response.status }); return result;
}
async function run(action, message) {
  if (working) return; working = true; $('scan').disabled = true;
  try { await action(); if (message) notice(message); }
  catch (error) { notice(error.message, true); }
  finally { working = false; $('scan').disabled = false; }
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
  snapshot = await api('/api/hub'); $('content').hidden = false; $('locked').hidden = true; $('connection').textContent = 'Auxiliar conectado'; render();
}
async function saveSettings(change) { snapshot = await api('/api/hub/settings', { ...snapshot.preferences, ...change }); render(); }
async function addFolder(path) {
  if (!path) return; await saveSettings({ folders: [...snapshot.preferences.folders, { path, enabled: true }] }); $('folderPath').value = ''; notice('Pasta adicionada e biblioteca atualizada.');
}
async function system() {
  const [state, audio] = await Promise.all([api('/api/status'), api('/api/audio')]);
  $('monitors').checked = state.displays.monitorsEnabled;
  $('displayNote').textContent = state.displays.note || 'A escolha é guardada para as próximas sessões.';
  $('audio').replaceChildren(...audio.map(device => button(device.name + (device.current ? ' · Em uso' : ''), async () => { await api('/api/audio', { id: device.id }); await system(); notice('Saída de áudio atualizada.'); })));
  if (!audio.length) $('audio').append(el('p', 'muted', 'Nenhuma saída disponível nesta sessão.'));
  const controller = state.controller || {};
  paths($('systemStatus'), [['Auxiliar', 'Conectado neste PC'], ['Biblioteca', state.state.games.length + ' jogos'], ['Controle', controller.connected ? 'Conectado' : 'Confira a conexão USB ou Bluetooth'], ['Busca automática', snapshot.preferences.automatic ? 'Ativada' : 'Pausada']]);
}
document.querySelectorAll('[data-view]').forEach(node => node.onclick = () => {
  view = node.dataset.view; document.querySelectorAll('[data-view]').forEach(n => n.toggleAttribute('aria-current', false)); node.setAttribute('aria-current', 'page');
  document.querySelectorAll('.view').forEach(n => n.hidden = n.id !== 'view-' + view);
  $('pageTitle').textContent = titles[view][0]; $('pageNote').textContent = titles[view][1]; if (view === 'system') run(system);
});
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
async function start() {
  try {
    const token = location.hash.slice(1); history.replaceState(null, '', location.pathname);
    if (token) { const response = await fetch('/hub-session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) }); if (!response.ok) throw Error('O acesso expirou. Abra a central novamente pelo atalho do PC.'); }
    await reload();
  } catch (error) { $('locked').hidden = false; $('content').hidden = true; $('connection').textContent = 'Aguardando acesso'; notice(error.message, true); }
}
$('retry').onclick = start;
start();
setInterval(() => { if (!working && snapshot && !$('gameDialog').open && document.visibilityState === 'visible') reload().catch(error => notice(error.message, true)); }, 15000);
