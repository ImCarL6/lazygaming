/* global $, api, focusControl, playMenuSound, playLauncherOpen, renderPadGlyphs, LGControllerArt */
/* Primeiro uso dentro do launcher do PC: mesma identidade, sons e navegação por controle. */
(function () {
  'use strict';
  if (typeof document === 'undefined' || !document.getElementById('firstRun')) return;
  var step = 0, info = null, library = null, scanning = false, choice = { display: '', audio: '', only: false, source: 'auto', startup: true, desktop: true, guide: true }, binding = null, edited = false;
  var offer = null, tvOnly = false, DONE = 6;
  var capturing = false, picking = false, captureTimer = 0, pollTimer = 0, refreshTimer = 0, finishing = false, resolveDone = null, chipState = '';
  var NAMES = {
    dualsense: { 64: 'Create', 128: 'Options', 256: 'L3', 512: 'R3', 16: 'L1', 32: 'R1', 1024: 'L2', 2048: 'R2', 1: '✕', 2: '○', 4: '□', 8: '△', 4096: 'Microfone', 8192: 'Touchpad' },
    switch: { 64: '−', 128: '+', 256: 'L3', 512: 'R3', 16: 'L', 32: 'R', 1024: 'ZL', 2048: 'ZR', 1: 'B', 2: 'A', 4: 'Y', 8: 'X' },
    xbox: { 64: 'View', 128: 'Menu', 256: 'L3', 512: 'R3', 16: 'LB', 32: 'RB', 1024: 'LT', 2048: 'RT', 1: 'A', 2: 'B', 4: 'X', 8: 'Y', 4096: 'Microfone', 8192: 'Touchpad' }
  };
  var ORDER = [64, 128, 256, 512, 16, 32, 1024, 2048, 1, 2, 4, 8, 4096, 8192];
  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text !== undefined) n.textContent = text; return n; }
  function family() {
    var c = info && info.controller;
    if (c && c.connected) return LGControllerArt.family(c.kind, c.name);
    return choice.source === 'auto' ? 'generic' : LGControllerArt.family(choice.source);
  }
  function names(mask) { var f = family(), map = NAMES[f === 'generic' ? 'xbox' : f]; return ORDER.filter(function (v) { return v & mask; }).map(function (v) { return map[v] || 'Gatilho'; }).join(' e '); }
  function sentence(node, b, purpose) {
    var seconds = (b.holdMs / 1000).toLocaleString('pt-BR');
    node.textContent = '';
    node.append(b.gesture === 'double-tap' ? 'Toque duas vezes em ' : 'Segure ', el('strong', '', names(b.buttons) || 'o botão escolhido'),
      b.gesture === 'double-tap' ? '' : ' por ' + seconds + ' s', purpose === 'mouse' ? ' para ligar ou desligar o mouse.' : ' para abrir ou ocultar o launcher.');
  }
  function options() { return info.controllerOptions || {}; }
  // Dicas do rodapé no padrão do controle em uso (ou do tipo escolhido), respeitando confirmar/voltar.
  function glyphs() {
    if (typeof renderPadGlyphs !== 'function') return;
    var c = info.controller, f = family();
    renderPadGlyphs({ adapterId: c.connected ? c.kind : f === 'switch' ? 'switch-pro' : f === 'xbox' ? 'xinput' : 'dualsense', swapConfirmBack: !!options().swapConfirmBack });
  }
  function defaultBinding() { var o = options(); return Object.assign({}, o.launcher || o.launcherBinding || { buttons: 192, gesture: 'hold', holdMs: 1000 }); }
  function option(label, detail, selected, onPick, badge) {
    var b = el('button', 'fr-option'); b.type = 'button'; b.setAttribute('aria-pressed', selected ? 'true' : 'false');
    var name = el('span', 'fr-option-name', label); b.append(name);
    if (badge) name.append(el('em', 'fr-badge', badge));
    if (detail) b.append(el('span', 'fr-option-detail', detail));
    b.onclick = function () { onPick(); var group = b.parentNode; Array.prototype.forEach.call(group.children, function (n) { n.setAttribute('aria-pressed', n === b ? 'true' : 'false'); }); };
    return b;
  }
  function renderDisplays() {
    var host = $('frDisplays'), screens = info.screens.filter(function (s) { return s.available; });
    var focused = document.activeElement && host.contains(document.activeElement) ? document.activeElement.dataset.path : null;
    host.textContent = '';
    if (!screens.length) { host.append(el('p', 'fr-note', 'Nenhuma tela disponível. Confira o cabo e aguarde.')); return; }
    if (!screens.some(function (s) { return s.devicePath === choice.display; })) choice.display = (screens.find(function (s) { return s.devicePath === info.displayPath; }) || screens[0]).devicePath;
    screens.forEach(function (screen, index) {
      var b = option(screen.name, screen.width + ' × ' + screen.height + (screens.length > 1 ? ' · tela ' + (index + 1) : ''), screen.devicePath === choice.display,
        function () { choice.display = screen.devicePath; focusControl($('frNext')); }, screen.devicePath === info.displayPath ? 'sugerida' : '');
      b.dataset.path = screen.devicePath; host.append(b);
      if (focused === screen.devicePath) focusControl(b);
    });
  }
  function renderAudio() {
    var host = $('frAudio'); host.textContent = '';
    var list = [['', 'Áudio da tela de jogo', 'automático']].concat([['keep', 'Manter a saída atual', '']], info.audio.map(function (d) { return [d.id, d.name.replace(/\s*\([^)]*\)\s*$/, ''), d.current ? 'atual' : '']; }));
    list.forEach(function (item) { host.append(option(item[1], item[2], item[0] === choice.audio, function () { choice.audio = item[0]; })); });
    $('frOnlyDisplay').setAttribute('aria-pressed', String(choice.only));
  }
  function renderGames() {
    if (!library) { $('frSteam').textContent = 'Procurando jogos da Steam…'; return; }
    var games = library.games.filter(function (g) { return g.enabled; }), steam = games.filter(function (g) { return g.origin === 'Steam'; });
    var card = $('frSteam'); card.textContent = ''; card.classList.toggle('found', steam.length > 0);
    card.append(el('strong', '', steam.length ? 'Steam encontrada' : 'Ainda não encontramos jogos da Steam'));
    card.append(el('span', '', steam.length ? steam.length + (steam.length === 1 ? ' jogo instalado' : ' jogos instalados') : 'Se você usa a Steam, escolha a pasta da biblioteca (por exemplo SteamLibrary ou Steam\\steamapps).'));
    if (!steam.length) { var pick = el('button', 'fr-pill small', 'Escolher pasta da Steam'); pick.type = 'button'; pick.onclick = function () { addFolder(); }; card.append(pick); }
    var host = $('frFolders'); host.textContent = '';
    (library.folders || []).forEach(function (folder) {
      var row = el('div', 'fr-folder' + (folder.available ? '' : ' missing'));
      row.append(el('span', 'fr-folder-path', folder.path), el('span', 'fr-folder-state', folder.available ? 'observando' : 'indisponível'));
      var remove = el('button', 'fr-pill small', 'Remover'); remove.type = 'button'; remove.onclick = function () { removeFolder(folder.path); }; row.append(remove);
      host.append(row);
    });
    $('frGamesCount').textContent = scanning ? 'Procurando jogos…' : games.length ? games.length + (games.length === 1 ? ' jogo pronto na biblioteca.' : ' jogos prontos na biblioteca.') : 'Nenhum jogo ainda. Você pode adicionar pastas agora ou depois.';
  }
  async function loadGames() { renderGames(); try { library = await api('/api/hub'); } catch (e) { $('frError').textContent = e.message; } renderGames(); }
  async function saveFolders(folders) {
    scanning = true; renderGames();
    try { library = await api('/api/hub/settings', Object.assign({}, library.preferences, { folders: folders })); }
    catch (e) { $('frError').textContent = e.message; }
    finally { scanning = false; renderGames(); }
  }
  async function addFolder() {
    if (scanning || picking || !library) return;
    picking = true;
    var picked; try { picked = (await api('/api/hub/pick', { kind: 'folder' })).path; } catch (e) { $('frError').textContent = e.message; return; }
    finally { picking = false; }
    if (!picked) return;
    if (library.preferences.folders.some(function (f) { return f.path.toLowerCase() === picked.toLowerCase(); })) { $('frGamesCount').textContent = 'Essa pasta já está na lista.'; return; }
    await saveFolders(library.preferences.folders.concat([{ path: picked, enabled: true }])); playMenuSound('confirm'); focusControl($('frNext'));
  }
  async function removeFolder(path) { if (scanning) return; await saveFolders(library.preferences.folders.filter(function (f) { return f.path !== path; })); focusControl($('frAddFolder')); }
  async function rescan() { if (scanning) return; scanning = true; renderGames(); try { library = await api('/api/hub/scan', {}); } catch (e) { $('frError').textContent = e.message; } finally { scanning = false; renderGames(); } }
  function renderSystem() { [['frStartup', 'startup'], ['frDesktop', 'desktop'], ['frSteamGuide', 'guide']].forEach(function (item) { $(item[0]).setAttribute('aria-pressed', String(choice[item[1]])); }); }
  function renderController() {
    var c = info.controller, f = family();
    glyphs();
    var state = (c.connected ? 'on:' + c.name : 'off') + ':' + choice.source;
    if (state !== chipState) {
      var arrived = chipState && c.connected && chipState.indexOf('on:') !== 0; chipState = state;
      $('frChip').textContent = c.connected ? c.name + ' conectado' : 'Conecte o controle por USB ou Bluetooth';
      $('frChip').classList.toggle('connected', c.connected);
      if (arrived) { $('frChip').classList.remove('arrived'); void $('frChip').offsetWidth; $('frChip').classList.add('arrived'); playMenuSound('confirm'); }
    }
    if (!edited) binding = defaultBinding();
    LGControllerArt.render($('frArt'), { family: f, buttons: binding.buttons, gesture: binding.gesture, holdMs: binding.holdMs, waiting: f === 'generic', listening: capturing });
    sentence($('frSentence'), binding, 'launcher');
    $('frDouble').setAttribute('aria-pressed', String(binding.gesture === 'double-tap'));
    $('frHold').setAttribute('aria-pressed', String(binding.gesture !== 'double-tap'));
    var times = $('frHoldTimes'); times.hidden = binding.gesture === 'double-tap';
    if (!times.children.length) [500, 1000, 1500, 2000].forEach(function (ms) {
      var b = el('button', 'fr-pill small', (ms / 1000).toLocaleString('pt-BR') + ' s'); b.type = 'button'; b.dataset.ms = ms;
      b.onclick = function () { edited = true; binding.holdMs = ms; renderController(); }; times.append(b);
    });
    Array.prototype.forEach.call(times.children, function (b) { b.setAttribute('aria-pressed', String(Number(b.dataset.ms) === binding.holdMs)); });
    $('frChange').disabled = !c.connected && !capturing;
    var kinds = $('frKinds'); kinds.hidden = c.connected;
    if (!kinds.children.length) [['auto', 'Detectar ao conectar'], ['dualsense', 'DualSense'], ['xinput', 'Xbox / 8BitDo'], ['switch-pro', 'Pro Controller']].forEach(function (k) {
      var b = el('button', 'fr-pill small', k[1]); b.type = 'button'; b.dataset.kind = k[0];
      b.onclick = function () { choice.source = k[0]; renderController(); }; kinds.append(b);
    });
    Array.prototype.forEach.call(kinds.children, function (b) { b.setAttribute('aria-pressed', String(b.dataset.kind === choice.source)); });
    var o = options(), mouse = o.mouse || o.mouseBinding;
    $('frMouse').hidden = !mouse || f === 'generic';
    if (!$('frMouse').hidden) {
      LGControllerArt.render($('frMouseArt'), { family: f, buttons: mouse.buttons, gesture: mouse.gesture, holdMs: mouse.holdMs });
      sentence($('frMouseSentence'), mouse, 'mouse');
      var dualsense = f === 'dualsense'; $('frTouch').hidden = !dualsense; if (dualsense) LGControllerArt.touchpadDemo($('frTouch'));
      $('frMouseMoves').textContent = dualsense ? 'Deslize no touchpad para mover · pressione para clicar · pressione e deslize para arrastar.'
        : 'O analógico direito move o cursor; ' + names(o.leftClick || 32) + ' clica e ' + names(o.rightClick || 16) + ' faz o clique direito.';
    }
  }
  // A etapa Samsung só existe quando o Windows vê alguma tela Samsung; contadores acompanham o total real.
  function total() { return offer && offer.show ? 6 : 5; }
  function renderCounters() {
    var dots = $('frDots'), count = total() + 1;
    while (dots.children.length > count) dots.removeChild(dots.lastChild);
    while (dots.children.length < count) dots.append(el('li'));
    Array.prototype.forEach.call(document.querySelectorAll('#firstRun .fr-step .fr-eyebrow'), function (n) {
      var s = Number(n.parentNode.dataset.step); n.textContent = 'Passo ' + (s + 1) + ' de ' + total() + (s === 5 ? ' · opcional' : '');
    });
  }
  function tvStep(text) { var li = el('li'); li.append(text); return li; }
  function renderTv(guide) {
    var o = offer || { screens: [], addresses: [] };
    $('frTvOffer').hidden = !!guide; $('frTvGuide').hidden = !guide;
    $('frTvScreens').textContent = o.screens.length ? 'Tela Samsung encontrada: ' + o.screens.join(', ') + '. Monitores Samsung também aparecem aqui; confirme que é uma TV com Tizen.' : '';
    var list = $('frTvSteps'); list.textContent = '';
    if (o.linked) {
      list.append(tvStep('Esta TV já está pareada com o PC. Abra o LazyGaming na TV para jogar.'),
        tvStep('Se ela não conectar, use Recuperar conexão no app da TV. Não é preciso instalar nem parear de novo.'));
      $('frTvState').textContent = o.lanEnabled ? 'Conexão pela rede local ativa neste PC.' : 'A conexão pela rede local está desligada neste PC: ative-a na Central e reinicie o LazyGaming.';
      return;
    }
    var ip = o.addresses.length ? ' (' + o.addresses.join(' ou ') + ')' : '';
    list.append(tvStep('Confirme que é uma TV Samsung com Tizen, na mesma rede deste PC.'),
      tvStep('Na TV, abra Apps, digite 12345 no controle, ligue o Developer Mode e informe o endereço deste PC' + ip + '. Se a TV pedir para reiniciar, reinicie-a você mesmo.'),
      tvStep('Instale o app LazyGaming na TV pelo kit TV-opcional, com um certificado Samsung autorizado para essa TV. O assistente ainda não instala sozinho.'),
      tvStep('Na Central, ative “Permitir conexão do app Tizen pela rede local”, salve e reinicie o LazyGaming. Gere o código e digite-o no app da TV.'));
    $('frTvState').textContent = 'A TV só estará conectada depois do pareamento no app da TV. Enquanto isso, tudo funciona por cabo.';
  }
  function show(next, quiet) {
    step = Math.max(0, Math.min(DONE, next));
    renderCounters();
    Array.prototype.forEach.call(document.querySelectorAll('#firstRun .fr-step'), function (n) { var on = Number(n.dataset.step) === step; n.hidden = !on; if (on) { n.classList.remove('entering'); void n.offsetWidth; n.classList.add('entering'); } });
    Array.prototype.forEach.call($('frDots').children, function (d, i) { d.className = i < step ? 'done' : i === step ? 'current' : ''; });
    $('frBack').disabled = step === 0; $('frBack').classList.toggle('ghost', step === 0);
    $('frNext').textContent = step === 4 && total() === 5 ? 'Concluir' : 'Continuar';
    $('frNext').hidden = step === 5;
    document.querySelector('#firstRun .fr-foot').hidden = step === DONE;
    $('frError').textContent = '';
    if (step === 1) { renderController(); startPolling(); } else stopPolling();
    if (step === 0) renderDisplays();
    if (step === 2) renderAudio();
    if (step === 3) loadGames();
    if (step === 4) renderSystem();
    if (step === 5) renderTv(tvOnly);
    if (!quiet) playMenuSound('confirm');
    var target = step === 0 ? $('frDisplays').querySelector('[aria-pressed="true"]') : step === 1 ? $('frChange') : step === 2 ? $('frAudio').querySelector('[aria-pressed="true"]') : step === 3 ? $('frAddFolder') : step === 4 ? $('frStartup') : step === 5 ? (tvOnly ? $('frTvDone') : $('frTvSetup')) : $('frPlay');
    focusControl(target || $('frNext'));
  }
  function startPolling() {
    stopPolling();
    pollTimer = setInterval(function () {
      if (capturing || finishing) return;
      api('/api/hub/controller').then(function (r) { info.controller = r.controller; info.controllerOptions = r.controllerOptions; if (step === 1) renderController(); }).catch(function () { });
    }, 750);
  }
  function stopPolling() { clearInterval(pollTimer); pollTimer = 0; }
  function endCapture() { clearTimeout(captureTimer); capturing = false; $('frChange').textContent = 'Mudar botão'; }
  async function capture() {
    if (capturing) { endCapture(); $('frCapture').textContent = ''; try { await api('/api/hub/controller-capture/cancel', {}); } catch (_) { } renderController(); return; }
    try { await api('/api/hub/controller-capture', {}); } catch (e) { $('frCapture').textContent = e.message; return; }
    capturing = true; $('frChange').textContent = 'Cancelar'; renderController();
    (async function poll() {
      if (!capturing) return;
      try {
        var state = await api('/api/hub/controller-capture');
        if (!capturing) return;
        if (state.phase === 'done') {
          edited = true; binding = state.singlePress ? Object.assign({}, binding, { buttons: state.binding.buttons }) : Object.assign({}, state.binding);
          endCapture(); $('frCapture').textContent = 'Pronto! ' + names(binding.buttons) + ' escolhido.'; renderController();
          $('frArt').classList.remove('picked'); void $('frArt').offsetWidth; $('frArt').classList.add('picked'); playMenuSound('confirm'); focusControl($('frNext')); return;
        }
        if (state.phase === 'timeout' || state.phase === 'idle') { endCapture(); $('frCapture').textContent = 'Não percebemos nenhum botão. Tente de novo quando quiser.'; renderController(); return; }
        $('frCapture').textContent = state.phase === 'release' ? 'Solte os botões do controle…' : 'Aperte no controle o botão que você quer usar';
        captureTimer = setTimeout(poll, 120);
      } catch (e) { endCapture(); $('frCapture').textContent = e.message; renderController(); }
    })();
  }
  async function finish() {
    if (finishing) return;
    if (capturing) { $('frError').textContent = 'Conclua ou cancele a escolha do botão.'; return; }
    if (!choice.display) { show(0, true); $('frError').textContent = 'Escolha a tela em que você vai jogar.'; return; }
    finishing = true; $('frNext').disabled = true; $('frBack').disabled = true; $('frNext').textContent = 'Salvando…';
    try {
      var c = info.controller;
      try { await api('/api/hub/integration', { startWithWindows: choice.startup, desktopShortcut: choice.desktop, blockSteamGuide: choice.guide }); }
      catch (e) { $('frError').textContent = 'Atalhos: ' + e.message; }
      // Configuração base gravada antes da etapa opcional: pular ou falhar na TV não exige refazer o resto.
      var saved = await api('/api/hub/setup', { displayPath: choice.display, controllerSource: c.connected ? 'auto' : choice.source, audioDeviceId: choice.audio || null, onlyGameDisplay: choice.only,
        controller: Object.assign({}, options(), { launcher: binding }) });
      sentence($('frDoneText'), binding, 'launcher');
      offer = saved && saved.samsungOffer || null;
      show(offer && offer.show ? 5 : DONE, true);
      try { playLauncherOpen(); } catch (_) { }
    } catch (e) { $('frError').textContent = e.message; $('frNext').textContent = 'Concluir'; }
    finally { finishing = false; $('frNext').disabled = false; $('frBack').disabled = step === 0; }
  }
  function close() {
    stopPolling(); endCapture(); clearInterval(refreshTimer); refreshTimer = 0;
    var panel = $('firstRun'); panel.classList.add('leaving');
    setTimeout(function () { panel.hidden = true; panel.classList.remove('leaving'); if (typeof syncPointer === 'function') syncPointer(); var done = resolveDone; resolveDone = null; if (done) done(true); }, 260);
  }
  async function leaveTv() {
    try { if (offer && !offer.linked) await api('/api/hub/samsung-hint/dismiss', {}); } catch (_) { }
    if (tvOnly) close(); else show(DONE);
  }
  $('frTvSetup').onclick = function () { renderTv(true); playMenuSound('confirm'); focusControl($('frTvDone')); };
  $('frTvSkip').onclick = leaveTv;
  $('frTvDone').onclick = leaveTv;
  $('frTvCentral').onclick = function () { api('/api/hub/launch', {}).then(function () { $('frTvState').textContent = 'Central aberta no navegador do PC.'; }).catch(function (e) { $('frError').textContent = e.message; }); };
  $('frNext').onclick = function () { if (step === 4) finish(); else if (step === 0 && !choice.display) $('frError').textContent = 'Escolha a tela em que você vai jogar.'; else show(step + 1); };
  $('frBack').onclick = function () { back(); };
  $('frPlay').onclick = close;
  $('frChange').onclick = capture;
  $('frDouble').onclick = function () { edited = true; binding.gesture = 'double-tap'; renderController(); };
  $('frHold').onclick = function () { edited = true; binding.gesture = 'hold'; if (!binding.holdMs) binding.holdMs = 1000; renderController(); };
  $('frAddFolder').onclick = addFolder; $('frScan').onclick = rescan;
  [['frStartup', 'startup'], ['frDesktop', 'desktop'], ['frSteamGuide', 'guide']].forEach(function (item) { $(item[0]).onclick = function () { choice[item[1]] = !choice[item[1]]; renderSystem(); }; });
  $('frOnlyDisplay').onclick = function () { choice.only = !choice.only; $('frOnlyDisplay').setAttribute('aria-pressed', String(choice.only)); };
  function back() {
    if (finishing) return; if (capturing) { capture(); return; }
    if (step === 5) { if (tvOnly) close(); else if (!$('frTvGuide').hidden) { renderTv(false); focusControl($('frTvSetup')); } else show(4); return; }
    if (step > 0 && step < 5) show(step - 1);
  }
  // Retomada pelo painel avançado: só o passo a passo da TV, sem refazer a configuração inicial.
  async function openTv() {
    if (!$('firstRun').hidden) return false;
    try { info = await api('/api/hub/samsung-hint/resume', {}); } catch (_) { return false; }
    offer = info.samsungOffer; tvOnly = true;
    $('firstRun').hidden = false; glyphs(); if (typeof syncPointer === 'function') syncPointer();
    show(5, true);
    return new Promise(function (resolve) { resolveDone = resolve; });
  }
  // Resolve true quando o primeiro uso terminou aqui (ou false se já estava configurado / indisponível).
  async function start(force) {
    if (!$('firstRun').hidden) return false;
    try { info = await api('/api/hub/setup'); } catch (_) { return false; }
    if (!info || !info.preferences || (info.preferences.setupCompleted && !force)) return false;
    edited = false; library = null; step = 0; tvOnly = false; offer = info.samsungOffer || null;
    choice.display = info.displayPath || ''; choice.audio = info.preferences.audioDeviceId || ''; choice.only = info.preferences.displayPath ? info.onlyGameDisplay : false;
    choice.source = info.preferences.controllerSource || 'auto';
    $('firstRun').hidden = false; glyphs(); if (typeof syncPointer === 'function') syncPointer();
    api('/api/hub/integration').then(function (i) { choice.guide = i.blockSteamGuide !== false; if (force) { choice.startup = !!i.startWithWindows; choice.desktop = !!i.desktopShortcut; } if (step === 4) renderSystem(); }).catch(function () { });
    show(0, true);
    // Telas e saídas de áudio podem mudar durante a configuração (cabo, Bluetooth).
    clearInterval(refreshTimer);
    refreshTimer = setInterval(function () {
      if ($('firstRun').hidden) { clearInterval(refreshTimer); refreshTimer = 0; return; }
      if (finishing || capturing || step === 1 || step >= 3) return;
      api('/api/hub/setup').then(function (next) {
        if (!next || finishing) return;
        var screens = JSON.stringify(next.screens), audio = JSON.stringify(next.audio), changed = screens !== JSON.stringify(info.screens) || audio !== JSON.stringify(info.audio);
        info.screens = next.screens; info.audio = next.audio; info.displayPath = next.displayPath;
        if (changed && step === 0) renderDisplays(); if (changed && step === 2) renderAudio();
      }).catch(function () { });
    }, 2500);
    return new Promise(function (resolve) { resolveDone = resolve; });
  }
  window.LGFirstRun = { start: start, openTv: openTv, back: back, get active() { return !$('firstRun').hidden; } };
}());
