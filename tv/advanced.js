/* global $, api, focusControl, playMenuSound, syncPointer, refresh, focusSelectedGame, message */
/* Configurações avançadas no launcher do PC: pastas, Steam, inicialização e revisão do primeiro uso. */
(function () {
  'use strict';
  if (typeof document === 'undefined' || !document.getElementById('advanced')) return;
  var library = null, integration = null, busy = false;
  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text !== undefined) n.textContent = text; return n; }
  function note(text) { $('advLibraryNote').textContent = text || ''; }
  function render() {
    if (library) {
      var games = library.games.filter(function (g) { return g.enabled; }), steam = games.filter(function (g) { return g.origin === 'Steam'; });
      $('advSteam').textContent = library.preferences.steam === false ? 'Busca da Steam desligada.' : steam.length ? 'Steam: ' + steam.length + (steam.length === 1 ? ' jogo' : ' jogos') + ' · ' + games.length + ' no total.' : 'Nenhum jogo da Steam encontrado. Adicione a pasta da biblioteca Steam abaixo.';
      var host = $('advFolders'), focused = document.activeElement && host.contains(document.activeElement) ? document.activeElement.dataset.path : null; host.textContent = '';
      if (!library.folders.length) host.append(el('p', 'setting-note', 'Nenhuma pasta adicionada.'));
      library.folders.forEach(function (folder) {
        var row = el('div', 'adv-folder' + (folder.available ? '' : ' missing'));
        row.append(el('span', 'adv-folder-path', folder.path), el('span', 'adv-folder-state', folder.available ? 'observando' : 'indisponível'));
        var remove = el('button', '', 'Remover'); remove.dataset.path = folder.path; remove.onclick = function () { saveFolders(library.preferences.folders.filter(function (f) { return f.path !== folder.path; })); };
        row.append(remove); host.append(row); if (focused === folder.path) focusControl(remove);
      });
      $('advSteamToggle').setAttribute('aria-pressed', String(library.preferences.steam !== false));
      $('advAuto').setAttribute('aria-pressed', String(library.preferences.automatic !== false));
      var interval = $('advInterval'); $('advInterval').hidden = library.preferences.automatic === false;
      if (!interval.children.length) [1, 5, 15, 30].forEach(function (minutes) {
        var b = el('button', 'adv-chip', minutes + ' min'); b.dataset.minutes = minutes; b.onclick = function () { save({ intervalMinutes: minutes }); }; interval.append(b);
      });
      Array.prototype.forEach.call(interval.children, function (b) { b.setAttribute('aria-pressed', String(Number(b.dataset.minutes) === library.preferences.intervalMinutes)); });
    }
    if (integration) {
      $('advStartup').setAttribute('aria-pressed', String(!!integration.startWithWindows));
      $('advDesktop').setAttribute('aria-pressed', String(!!integration.desktopShortcut));
      $('advGuide').setAttribute('aria-pressed', String(integration.blockSteamGuide !== false));
      $('advGuideNote').textContent = integration.blockSteamGuide === false ? 'Desligado: o botão do controle pode abrir o Steam.' : integration.steamGuideNote || 'O Big Picture do Steam é fechado se abrir por cima do launcher ou do jogo.';
    }
  }
  async function guarded(task, text) {
    if (busy) return; busy = true; if (text) note(text);
    try { await task(); playMenuSound('confirm'); } catch (e) { note(e.message); } finally { busy = false; render(); }
  }
  function save(change) { return guarded(async function () { library = await api('/api/hub/settings', Object.assign({}, library.preferences, change)); note('Salvo. Biblioteca atualizada.'); }, 'Salvando…'); }
  function saveFolders(folders) { return guarded(async function () { library = await api('/api/hub/settings', Object.assign({}, library.preferences, { folders: folders })); note('Pastas atualizadas. ' + library.games.filter(function (g) { return g.enabled; }).length + ' jogos na biblioteca.'); }, 'Procurando jogos…'); }
  function setIntegration(change) { return guarded(async function () { integration = await api('/api/hub/integration', change); }); }
  async function open() {
    $('options').hidden = true; $('advanced').hidden = false; syncPointer(); focusControl($('closeAdvanced'));
    note('Carregando…');
    try { var results = await Promise.all([api('/api/hub'), api('/api/hub/integration')]); library = results[0]; integration = results[1]; note(''); }
    catch (e) { note(e.message); }
    render(); focusControl($('advAddFolder'));
  }
  function close() { $('advanced').hidden = true; $('options').hidden = false; syncPointer(); focusControl($('advancedSettings')); }
  $('advancedSettings').onclick = open;
  $('closeAdvanced').onclick = close;
  $('advAddFolder').onclick = function () {
    if (busy || !library) return;
    guarded(async function () {
      var picked = (await api('/api/hub/pick', { kind: 'folder' })).path; if (!picked) { note(''); return; }
      if (library.preferences.folders.some(function (f) { return f.path.toLowerCase() === picked.toLowerCase(); })) { note('Essa pasta já está na lista.'); return; }
      note('Procurando jogos…');
      library = await api('/api/hub/settings', Object.assign({}, library.preferences, { folders: library.preferences.folders.concat([{ path: picked, enabled: true }]) }));
      note('Pasta adicionada. ' + library.games.filter(function (g) { return g.enabled; }).length + ' jogos na biblioteca.');
    });
  };
  $('advScan').onclick = function () { if (library) guarded(async function () { library = await api('/api/hub/scan', {}); note('Busca concluída. ' + library.games.filter(function (g) { return g.enabled; }).length + ' jogos na biblioteca.'); }, 'Procurando jogos…'); };
  $('advSteamToggle').onclick = function () { if (library) save({ steam: library.preferences.steam === false }); };
  $('advAuto').onclick = function () { if (library) save({ automatic: library.preferences.automatic === false }); };
  $('advStartup').onclick = function () { if (integration) setIntegration({ startWithWindows: !integration.startWithWindows }); };
  $('advDesktop').onclick = function () { if (integration) setIntegration({ desktopShortcut: !integration.desktopShortcut }); };
  $('advGuide').onclick = function () { if (integration) setIntegration({ blockSteamGuide: integration.blockSteamGuide === false }); };
  $('advReview').onclick = async function () {
    $('advanced').hidden = true; $('options').hidden = true;
    if (window.LGFirstRun && await window.LGFirstRun.start(true)) { try { await refresh(); } catch (_) { } focusSelectedGame(); }
    else { $('options').hidden = false; focusControl($('advancedSettings')); }
  };
  $('advCentral').onclick = function () { guarded(async function () { await api('/api/hub/launch', {}); note('Central aberta no navegador do PC.'); }); };
  window.LGAdvanced = { close: close, get open() { return !$('advanced').hidden; } };
}());
