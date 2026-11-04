/* global LGCrypto, webapis */
'use strict';
var $ = function (id) { return document.getElementById(id); };
var saved; try { saved = JSON.parse(localStorage.getItem('lazygaming') || 'null'); } catch (_) { saved = null; }
var base = saved ? saved.base : location.protocol === 'http:' ? location.origin : '';
var session = saved, busy = false, connected = false, needsPair = false, lastState = null, selectedAudio = '', previousFocus;
var recoveryLink; try { recoveryLink = JSON.parse(localStorage.getItem('lazygaming-recovery') || 'null'); } catch (_) { recoveryLink = null; }
var recoveryEnrolled = false;
var mouseEnabled = true;
var previousEnvironmentNote = '';
var setupParentOptions = false;
var presentationMode = 'cable';
var presentationCache = {}, presentationRequests = {}, presentationOrigin = '', presentationEpoch = 0;
var transportTicket = 0, connectionPolling = false;
var desktop = localStorage.getItem('desktop-client') === 'true', syncRevision = -1, selectionTimer, selectionDirty = false, applyingSync = false, saveSelectionTimer;
var inputClient = LGCrypto.nonce();
var proofAt = 0, lastNavigationAt = 0;
var libraryGames = [], artworkMap = {}, selectedGameId = localStorage.getItem('selected-game') || '', heroTicket = 0, heroSlot = 0, heroTimer, heroUrl = '';
$('address').value = base;
var local = /^https?:\/\/(localhost|127\.0\.0\.1)(:|\/|$)/.test(location.origin);
$('localSetup').hidden = !local;
var controllerFocus = null;
var metadataTabTimer, metadataTabOwner;
function metadataTab(show, immediate) {
  var cue = $('gameSettings'); if (!cue) return;
  if (!cue.style || !document.querySelector) { cue.hidden = !show; return; }
  clearTimeout(metadataTabTimer);
  if (show) {
    var tile = document.querySelector('.game-tile.selected'), rail = document.querySelector('.rail-area');
    if (!tile || !rail) return;
    if (metadataTabOwner && metadataTabOwner !== tile) metadataTabOwner.classList.remove('metadata-owner');
    metadataTabOwner = tile; tile.classList.add('metadata-owner');
    var width = tile.offsetWidth * 1.045, height = tile.offsetHeight * 1.045;
    var top = rail.getBoundingClientRect().top + tile.offsetTop - (height - tile.offsetHeight) / 2;
    var tabHeight = Math.max(42, Math.min(56, innerHeight * .044));
    cue.style.left = (innerWidth / 2 - width / 2) + 'px'; cue.style.width = width + 'px';
    cue.style.top = (top - tabHeight - 12) + 'px'; cue.style.height = tabHeight + 'px';
    var wasHidden = cue.hidden;
    cue.hidden = false; cue.disabled = false; cue.tabIndex = 0; cue.setAttribute('aria-hidden', 'false');
    if (wasHidden) { cue.classList.remove('metadata-open'); cue.getBoundingClientRect(); }
    cue.classList.add('metadata-open');
  } else {
    cue.classList.remove('metadata-open'); cue.disabled = true; cue.tabIndex = -1; cue.setAttribute('aria-hidden', 'true');
    function finish() { cue.hidden = true; if (metadataTabOwner) metadataTabOwner.classList.remove('metadata-owner'); metadataTabOwner = null; }
    if (immediate || cue.hidden) finish(); else metadataTabTimer = setTimeout(finish, 210);
  }
}
function markControlFocus(element) {
  metadataTab(element === $('gameSettings'));
  if (controllerFocus && controllerFocus.classList) controllerFocus.classList.remove('controller-focused');
  controllerFocus = element;
  if (element && element.classList) element.classList.add('controller-focused');
}
function focusControl(element) {
  if (!element) return;
  markControlFocus(element);
  element.focus({ preventScroll: true });
  var panel = element.closest && element.closest('.panel,.dialog');
  if (panel && element.getBoundingClientRect && panel.getBoundingClientRect) {
    var itemBounds = element.getBoundingClientRect(), panelBounds = panel.getBoundingClientRect(), margin = 18;
    if (itemBounds.bottom > panelBounds.bottom - margin) panel.scrollTop += itemBounds.bottom - panelBounds.bottom + margin;
    else if (itemBounds.top < panelBounds.top + margin) panel.scrollTop -= panelBounds.top + margin - itemBounds.top;
  }
  if (element.dataset && element.dataset.id) selectGame(element.dataset.id, true);
}
function message(s) { $('message').textContent = s || ''; }
function persist() { if (session) { session.base = base; localStorage.setItem('lazygaming', JSON.stringify(session)); } }
function updateRecovery() { if ($('recoverConnection')) $('recoverConnection').hidden = local || desktop || !recoveryLink; }
async function enrollRecovery() {
  if (local || desktop || recoveryEnrolled) return;
  var result = await api('/api/recovery/enroll', { id: recoveryLink && recoveryLink.id || null });
  if (!result || !/^[a-f0-9]{32}$/.test(result.id) || !/^[a-f0-9]{64}$/.test(result.key)) throw Error('Não foi possível preparar a recuperação nesta TV.');
  recoveryLink = { id: result.id, key: result.key, base: base };
  localStorage.setItem('lazygaming-recovery', JSON.stringify(recoveryLink)); recoveryEnrolled = true; updateRecovery();
}
async function recoveryProof(target, timeout) {
  var challenge = LGCrypto.nonce(), id = recoveryLink.id;
  var result = await request(target + '/api/recovery/discover?id=' + encodeURIComponent(id) + '&challenge=' + challenge, {}, timeout);
  if (!result.available || result.proof !== LGCrypto.hmac(recoveryLink.key, 'recovery-discover\n' + challenge + '\n' + result.identity + '\n' + id)) throw Error('O PC não confirmou o vínculo de recuperação desta TV.');
  return result;
}
async function findRecoveryPc() {
  var target; try { target = normalize($('address').value || recoveryLink.base || base); await recoveryProof(target); return target; } catch (_) { }
  var ip = LGPlatform.tv.networkIp();
  if (!ip) { try { ip = new URL(recoveryLink.base || base).hostname; } catch (_) { } }
  if (!/^192\.168\.\d{1,3}\.\d{1,3}$/.test(ip)) throw Error('Informe o endereço local do PC para recuperar a conexão.');
  var prefix = ip.split('.').slice(0,3).join('.') + '.', next = 1, found = null;
  async function worker() { while (next < 255 && !found) {
    var address = 'http://' + prefix + next++ + ':47831';
    try { await recoveryProof(address, 650); if (!found) found = address; } catch (_) { }
  } }
  await Promise.all(Array.from({ length: 12 }, worker));
  if (!found) throw Error('PC não encontrado com o vínculo desta TV. Confira se o auxiliar está aberto.');
  return found;
}
async function recoverConnection() {
  if (!recoveryLink || !/^[a-f0-9]{32}$/.test(recoveryLink.id) || !/^[a-f0-9]{64}$/.test(recoveryLink.key)) throw Error('Conecte uma vez com o código para preparar a recuperação.');
  message('Reconhecendo o PC…'); var target = await findRecoveryPc();
  // Nenhum segredo/comando é enviado antes da prova de posse do vínculo.
  await recoveryProof(target);
  var body = JSON.stringify({ id: recoveryLink.id }), time = Date.now().toString(), nonce = LGCrypto.nonce();
  var result = await request(target + '/api/recovery/restore', { method: 'POST', headers: {
    'Content-Type': 'application/json', 'X-LG-Time': time, 'X-LG-Nonce': nonce,
    'X-LG-Signature': LGCrypto.hmac(recoveryLink.key, 'POST\n/api/recovery/restore\n' + time + '\n' + nonce + '\n' + body)
  }, body: body });
  if (!result || !/^[a-f0-9]{64}$/.test(result.key) || result.proof !== LGCrypto.hmac(recoveryLink.key, 'recovery-restored\n' + nonce + '\n' + result.identity + '\n' + result.key)) throw Error('O PC não confirmou a recuperação.');
  base = target; session = { identity: result.identity, key: result.key }; proofAt = 0; needsPair = false;
  $('address').value = target; persist(); recoveryLink.base = target; localStorage.setItem('lazygaming-recovery', JSON.stringify(recoveryLink));
  await connect(); message('PC conectado.');
}
function normalize(text) {
  var raw = text.trim(); if (!/^https?:\/\//.test(raw)) raw = 'http://' + raw;
  var url = new URL(raw);
  if (url.protocol !== 'http:' || !/^(localhost|127\.0\.0\.1|192\.168\.\d{1,3}\.\d{1,3})$/.test(url.hostname)) throw Error('Use o endereço local do PC.');
  if (!url.port) url.port = '47831'; return url.origin;
}
async function request(url, options, timeout) {
  var controller = new AbortController(), timer = setTimeout(function () { controller.abort(); }, timeout || 5000);
  try {
    var response = await fetch(url, Object.assign({}, options, { signal: controller.signal }));
    var text = await response.text(), data; try { data = JSON.parse(text); } catch (_) { throw Error('O PC não respondeu corretamente. Tente novamente após reconectar.'); } if (!response.ok) throw Error(data.error || 'Não foi possível conectar ao PC.'); return data;
  } catch (e) {
    if (e.name === 'AbortError' || e instanceof TypeError) {
      var offline = Error('O PC não respondeu. Tentando reconectar automaticamente…'); offline.retryable = true; throw offline;
    }
    throw e;
  }
  finally { clearTimeout(timer); }
}
async function proof(target, timeout) {
  var challenge = LGCrypto.nonce();
  var data = await request(target + '/api/discover?challenge=' + challenge, {}, timeout);
  if (!session || data.identity !== session.identity || data.proof !== LGCrypto.hmac(session.key, 'discover\n' + challenge + '\n' + session.identity)) {
    needsPair = true;
    connected = false; updateRecovery();
    $('setup').hidden = false;
    if (recoveryLink && !local && !desktop) focusControl($('recoverConnection'));
    throw Error(recoveryLink ? 'O pareamento mudou. Use Recuperar conexão.' : 'Este PC não corresponde ao pareamento. Conecte novamente com o código.');
  }
  return data;
}
async function api(path, payload) {
  if (!desktop && Date.now() - proofAt > 15000) { await proof(base); proofAt = Date.now(); }
  var method = payload === undefined ? 'GET' : 'POST', body = payload === undefined ? '' : JSON.stringify(payload);
  var time = Date.now().toString(), nonce = LGCrypto.nonce();
  // O seletor nativo espera a escolha humana; cinco segundos não são uma falha de conexão.
  if (desktop) return request(base + path, { method: method, headers: { 'Content-Type': 'application/json' }, body: body || undefined }, path === '/api/hub/pick' ? 600000 : /launcher\/(sync|input)/.test(path) ? 25000 : /api\/(displays|hub\/setup)$/.test(path) ? 15000 : /start$/.test(path) ? 30000 : 5000);
  return request(base + path, { method: method, headers: { 'Content-Type': 'application/json', 'X-LG-Time': time, 'X-LG-Nonce': nonce,
    'X-LG-Signature': LGCrypto.hmac(session.key, method + '\n' + path + '\n' + time + '\n' + nonce + '\n' + body) }, body: body || undefined }, /launcher\/(sync|input)/.test(path) ? 25000 : /api\/displays$/.test(path) ? 15000 : /\/(start|move-to-tv)$/.test(path) ? 30000 : 5000);
}
function button(text, action, className) {
  var b = document.createElement('button'); b.textContent = text; if (className) b.className = className;
  b.onclick = function () { run(action); }; return b;
}
async function run(action) {
  if (busy) return; busy = true; message('');
  try { await action(); } catch (e) {
    if (e.retryable) { connected = false; $('connection').textContent = 'Reconectando ao PC…'; updateLiveBackground(); }
    message(e.message);
  }
  finally { busy = false; }
}
function ask(title, text, accept) {
  previousFocus = controllerFocus && controllerFocus.isConnected ? controllerFocus : document.activeElement; $('modalTitle').textContent = title; $('modalText').textContent = text; $('accept').textContent = accept;
  $('modal').hidden = false; focusControl($('cancel'));
  return new Promise(function (resolve) {
    function close(value) { $('modal').hidden = true; if (previousFocus && previousFocus.isConnected) focusControl(previousFocus); resolve(value); }
    $('cancel').onclick = function () { close(false); }; $('accept').onclick = function () { close(true); };
  });
}
async function stop(game, nextGame, mode) {
  try { await api('/api/games/' + game.id + '/stop', { force: false, nextGame: nextGame || null, mode: mode || null }); }
  catch (e) {
    if (!await ask('Forçar encerramento?', e.message + ' O progresso não salvo pode ser perdido.', 'Encerrar mesmo assim')) return false;
    await api('/api/games/' + game.id + '/stop', { force: true, confirm: 'LOSE_UNSAVED_PROGRESS', nextGame: nextGame || null, mode: mode || null });
  }
  for (var i = 0; i < 15; i++) {
    var state = await api('/api/status');
    var current = state.state.games.find(function (g) { return g.id === game.id; });
    if (!current.running && !current.starting) return true;
    await new Promise(function (resolve) { setTimeout(resolve, 1000); });
  }
  message('O jogo ainda está aberto. Saia pelo menu do jogo e tente novamente.'); return false;
}
async function play(game) {
  var choice = null;
  var mode = await choosePresentation(game, function (value) { choice = value; }); if (!mode) return;
  if (mode === 'stream' && window.LGStream) await window.LGStream.prepare();
  var state = await api('/api/status'), other = state.state.games.find(function (g) { return g.id !== game.id && (g.running || g.starting); });
  if (other) {
    if (state.state.environment && state.state.environment.mode && state.state.environment.mode !== mode) throw Error(desktop ? 'Há uma transmissão ativa para a TV. Troque o jogo pelo app da TV.' : 'Mantenha o mesmo modo Cabo/Stream para trocar de jogo nesta sessão.');
    // No PC a confirmação de troca já aconteceu no diálogo de início.
    if (!desktop && !await ask('Trocar jogo?', 'Encerrar ' + other.name + ' antes de abrir ' + game.name + '?', 'Trocar')) return;
    if (!await stop(other, game.id, mode)) return;
  }
  message(mode === 'stream' ? 'Abrindo o jogo para transmissão…' : 'Preparando a TV e abrindo o jogo…');
  await api('/api/games/' + game.id + '/start', { mode: mode, choice: choice }); await refresh();
  if (mode === 'stream') await showStream(); else await showHdmi();
}
async function reportStreamClient() {
  if (desktop || local || typeof tizen === 'undefined') return;
  var installed = false;
  installed = LGPlatform.tv.appInstalled('MoonLightS.MoonlightWasm');
  await api('/api/stream/client', { installed: installed });
}
async function showStream() {
  if (desktop || local || typeof tizen === 'undefined') { message('Continue a transmissão pelo LazyGaming na TV.'); return; }
  if (!window.LGStream) throw Error('Motor de transmissão indisponível.');
  await window.LGStream.start();
}
async function choosePresentation(game, onChoice) {
  var previous = controllerFocus || document.activeElement, ticket = ++transportTicket, navigationAt = lastNavigationAt;
  var cable = null, stream = null;
  // Launcher do PC: sem Cabo/Stream neste teste; só confirma início ou troca (docs/STREAM-RETOMAR.md).
  var simple = desktop, replacing = simple && libraryGames.find(function (g) { return game && g.id !== game.id && (g.running || g.starting); });
  $('transportTitle').textContent = simple ? (replacing ? 'Trocar jogo?' : 'Iniciar jogo?') : 'Como jogar?';
  $('chooseCable').textContent = simple ? (replacing ? 'Trocar' : 'Jogar') : 'Cabo';
  $('chooseStream').hidden = simple;
  var question = !simple ? '' : replacing ? 'Encerrar ' + replacing.name + ' e abrir ' + game.name + '?' : 'Abrir ' + game.name + '?';
  var choices = game && game.choices || [], selected = game && game.selectedChoice;
  if (!choices.some(function (v) { return v.id === selected && v.available; })) selected = (choices.find(function (v) { return v.available; }) || {}).id;
  var choicePanel = $('launchChoices'), choiceButtons = $('launchChoiceButtons');
  if (choicePanel) {
    choicePanel.hidden = !choices.length; choiceButtons.innerHTML = '';
    $('launchChoiceLabel').textContent = game && game.choiceLabel || 'Versão';
    choices.forEach(function (v) {
      var button = document.createElement('button'); button.textContent = v.name; button.disabled = !v.available;
      button.setAttribute('aria-pressed', v.id === selected ? 'true' : 'false');
      button.onclick = function () {
        selected = v.id;
        Array.prototype.forEach.call(choiceButtons.children, function (node) { node.setAttribute('aria-pressed', node === button ? 'true' : 'false'); });
      };
      choiceButtons.appendChild(button);
    });
  }
  $('transport').hidden = false;
  return new Promise(function (resolve) {
    function close(mode) {
      if (ticket !== transportTicket) return;
      if (mode && choices.length && !selected) return;
      if (mode && onChoice) onChoice(selected || null);
      transportTicket++; $('transport').hidden = true;
      if (previous && previous.isConnected) focusControl(previous); resolve(mode);
    }
    function render() {
      if (ticket !== transportTicket) return;
      $('chooseCable').hidden = !simple && !!cable && cable.available === false;
      $('chooseCable').disabled = !cable || cable.available !== true || (choices.length > 0 && !selected);
      $('chooseStream').disabled = !stream || stream.available !== true || (choices.length > 0 && !selected);
      $('transportNote').textContent = simple
        ? [question, choices.length && !selected ? 'Os arquivos destas versões estão indisponíveis.' : '', cable ? cable.note : 'Conferindo a tela de jogo…'].filter(Boolean).join(' ')
        : [choices.length && !selected ? 'Os arquivos destas versões estão indisponíveis.' : '', cable ? cable.note : 'Verificando Cabo…', stream ? stream.note : 'Verificando Stream…'].filter(Boolean).join(' ');
      var current = controllerFocus || document.activeElement;
      if (!current || current.disabled || current.hidden || (current === $('cancelTransport') && lastNavigationAt === navigationAt)) {
        focusControl(!$('chooseCable').hidden && !$('chooseCable').disabled ? $('chooseCable') : !$('chooseStream').disabled ? $('chooseStream') : $('cancelTransport'));
      }
    }
    $('chooseCable').onclick = function () { if (!$('chooseCable').disabled) close('cable'); };
    $('chooseStream').onclick = function () { if (!$('chooseStream').disabled) close('stream'); };
    $('cancelTransport').onclick = function () { close(null); };
    // O diálogo responde ao clique antes de qualquer consulta de rede.
    $('chooseCable').disabled = $('chooseStream').disabled = true;
    focusControl($('cancelTransport')); render();
    (simple ? ['cable'] : ['cable', 'stream']).forEach(function (kind) {
      var saved = presentationOrigin === base && presentationCache[kind];
      if (saved && Date.now() - saved.at < 10000) { if (kind === 'cable') cable = saved.value; else stream = saved.value; render(); }
      presentationAvailability(kind).then(function (value) { if (kind === 'cable') cable = value; else stream = value; render(); });
    });
  });
}
function presentationAvailability(kind) {
  if (presentationOrigin !== base) { presentationOrigin = base; presentationCache = {}; presentationRequests = {}; presentationEpoch++; }
  var saved = presentationCache[kind], epoch = presentationEpoch;
  if (saved && Date.now() - saved.at < 10000) return Promise.resolve(saved.value);
  if (presentationRequests[kind]) return presentationRequests[kind];
  var request = (async function () {
    if (kind === 'cable') return api('/api/presentation/cable');
    await reportStreamClient();
    if (epoch !== presentationEpoch) throw Error('A conexão mudou. Tente novamente.');
    return (await api('/api/presentation')).stream;
  })().catch(function () { return { available: false, note: 'Não foi possível verificar ' + (kind === 'cable' ? 'Cabo' : 'Stream') + '. Feche e tente novamente.' }; });
  presentationRequests[kind] = request.then(function (value) {
    if (epoch === presentationEpoch) {
      if (value.available === true || !value.note || value.note.indexOf('Não foi possível verificar') !== 0) presentationCache[kind] = { at: Date.now(), value: value };
      delete presentationRequests[kind];
    }
    return value;
  });
  return presentationRequests[kind];
}
function warmPresentation() { presentationAvailability('cable'); if (!desktop) presentationAvailability('stream'); }
// Destino abaixo da biblioteca quando Jogar está indisponível (Transmissão fica oculta no PC).
function dockFallback() { return [$('streamShortcut'), $('settings')].find(function (b) { return b && !b.hidden && b.offsetParent !== null; }) || $('settings'); }
function currentGame() { return libraryGames.find(function (game) { return game.id === selectedGameId; }); }
function focusSelectedGame() { var tile = Array.from($('games').querySelectorAll('button')).find(function (b) { return b.dataset.id === selectedGameId; }); if (tile) focusControl(tile); }
function updateBackdrop(game) {
  var art = artworkMap[game.id], url = art && art.backdrop ? base + art.backdrop : '';
  if (url === heroUrl) return; heroUrl = url; clearTimeout(heroTimer); var ticket = ++heroTicket;
  if (!url) { $('backdropA').classList.remove('visible'); $('backdropB').classList.remove('visible'); return; }
  heroTimer = setTimeout(function () {
    var image = new Image(); image.decoding = 'async';
    image.onload = function () {
      if (ticket !== heroTicket) return;
      var layers = [$('backdropA'), $('backdropB')], next = 1 - heroSlot;
      layers[next].src = url; layers[next].classList.add('visible'); layers[heroSlot].classList.remove('visible'); heroSlot = next;
    };
    image.onerror = function () { if (ticket === heroTicket) heroUrl = ''; };
    image.src = url;
  }, 140);
}
// A seta responde localmente; o próximo comando retoma da posição atual, sem fila de animações.
function moveRail(target) {
  // Uma transformação composta pelo navegador, sem escrever scrollLeft a cada quadro.
  var transform = 'translate3d(' + (-target) + 'px,0,0)';
  if ($('games').style.transform !== transform) $('games').style.transform = transform;
}
function queueSelection() {
  selectionDirty = true; clearTimeout(selectionTimer);
  selectionTimer = setTimeout(async function () {
    if (!connected || !session) return;
    var id = selectedGameId;
    try { var result = await api('/api/launcher/selection', { id: id }); syncRevision = Math.max(syncRevision, result.revision); }
    catch (_) { return; }
    finally { if (selectedGameId === id) selectionDirty = false; }
  }, 70);
}
async function syncLauncher() {
  for (;;) {
    if (!session || !connected || document.hidden) { await new Promise(function (r) { setTimeout(r, 1000); }); continue; }
    try {
      var state = await api('/api/launcher/sync?after=' + syncRevision);
      if (state.revision > syncRevision) {
        syncRevision = state.revision;
        if (!selectionDirty && state.id && libraryGames.some(function (g) { return g.id === state.id; })) {
          applyingSync = true; selectGame(state.id, true);
          if (document.activeElement && document.activeElement.classList.contains('game-tile')) focusSelectedGame();
          applyingSync = false;
        } else if (!state.id) queueSelection();
      }
    } catch (_) { await new Promise(function (r) { setTimeout(r, 2000); }); }
  }
}
function selectGame(id, scroll) {
  var game = libraryGames.find(function (g) { return g.id === id; }); if (!game) return;
  var changed = selectedGameId !== id; selectedGameId = id;
  if (changed && $('gameSettings') && !$('gameSettings').hidden) metadataTab(false, true);
  if (changed) { clearTimeout(saveSelectionTimer); saveSelectionTimer = setTimeout(function () { localStorage.setItem('selected-game', id); }, 300);
    if (!applyingSync) queueSelection(); }
  Array.from($('games').querySelectorAll('button')).forEach(function (tile) {
    var selected = tile.dataset.id === id; tile.classList.toggle('selected', selected); tile.setAttribute('aria-current', selected ? 'true' : 'false');
    if (selected) {
      var target = tile.offsetLeft + tile.offsetWidth / 2 - innerWidth / 2;
      moveRail(target);
    }
  });
  $('gameTitle').textContent = game.name; $('gameOrigin').textContent = game.origin || 'PC';
  if ($('gameYear')) $('gameYear').textContent = game.releaseYear || '';
  var runningChoice = (game.choices || []).find(function (v) { return v.id === game.runningChoice; });
  $('gameState').textContent = game.running ? 'Em execução' + (runningChoice ? ' · ' + runningChoice.name : '') : game.starting ? 'Abrindo…' : game.available ? '' : 'Identificação pendente';
  $('heroPlay').firstChild.nodeValue = (game.running ? 'Continuar' : game.starting ? 'Abrindo…' : 'Jogar') + ' ';
  $('heroPlay').disabled = !game.available || game.starting;
  var runningGame = libraryGames.find(function (item) { return item.running; });
  $('heroStop').hidden = !runningGame;
  $('moveToTv').hidden = !runningGame;
  $('heroStop').setAttribute('aria-label', runningGame ? 'Encerrar ' + runningGame.name : 'Encerrar jogo');
  updateBackdrop(game);
}
async function playSelected() {
  var game = currentGame(); if (!game || !game.available || game.starting) return;
  if (!game.running) return play(game);
  if (presentationMode === 'stream') { await api('/api/games/' + game.id + '/focus', {}); await reportStreamClient(); await showStream(); return; }
  // Continuar no PC só devolve foco; na TV precisa também reativar o destino HDMI.
  if (!desktop && !local) {
    message('Preparando a tela da TV…');
    await api('/api/games/' + game.id + '/start', { mode: 'cable' });
    await refresh();
  }
  await api('/api/games/' + game.id + '/focus', {}); await showHdmi();
}
function renderGames(games) {
  libraryGames = games;
  var signature = JSON.stringify(games.map(function (g) { return [g.id, g.name, g.available, artworkMap[g.id]]; }));
  if (signature !== lastState) {
    lastState = signature; var hadFocus = document.activeElement && document.activeElement.classList.contains('game-tile');
    $('games').textContent = '';
    games.forEach(function (game) {
      var tile = document.createElement('button'); tile.className = 'game-tile'; tile.dataset.id = game.id; tile.setAttribute('aria-label', game.name);
      var fallback = document.createElement('span'); fallback.className = 'cover-fallback'; fallback.textContent = game.name; tile.appendChild(fallback);
      var art = artworkMap[game.id];
      if (art && art.cover) { var cover = document.createElement('img'); cover.alt = ''; cover.loading = 'lazy'; cover.decoding = 'async'; cover.src = base + art.cover; cover.onerror = function () { this.hidden = true; this.dataset.failed = 'true'; }; cover.onload = function () { this.hidden = false; delete this.dataset.failed; }; tile.appendChild(cover); }
      var state = document.createElement('span'); state.className = 'tile-state'; state.hidden = true; tile.appendChild(state);
      tile.onfocus = function () { selectGame(game.id, true); }; // Hover parado não altera a seleção; somente movimento real ou foco do controle.
      tile.onclick = function () { selectGame(game.id, false); run(playSelected); }; $('games').appendChild(tile);
    });
    if (!games.some(function (g) { return g.id === selectedGameId; })) selectedGameId = games.length ? games[0].id : '';
    if (hadFocus) focusSelectedGame();
  }
  Array.from($('games').querySelectorAll('button')).forEach(function (tile) {
    var game = games.find(function (g) { return g.id === tile.dataset.id; }), state = tile.querySelector('.tile-state');
    state.hidden = !game.running && !game.starting; state.textContent = game.running ? 'Em execução' : 'Abrindo…';
    var cover = tile.querySelector('img'), art = artworkMap[game.id];
    if (cover && cover.dataset.failed && Number(cover.dataset.retries || 0) < 3 && art && art.cover) { cover.dataset.retries = Number(cover.dataset.retries || 0) + 1; cover.src = base + art.cover + '&retry=' + Date.now(); }
  });
  var running = games.find(function (g) { return g.running; }); $('runningSummary').textContent = running && running.id !== selectedGameId ? 'Em execução · ' + running.name : '';
  if (selectedGameId) selectGame(selectedGameId, false);
  else { $('gameTitle').textContent = 'Nenhum jogo encontrado'; $('heroPlay').disabled = true; if ($('gameYear')) $('gameYear').textContent = ''; }
}
var gameOptionsId = '', metadataSearchTicket = 0;
async function loadGameOptions(id) {
  var details = await api('/api/games/' + id + '/settings');
  if (gameOptionsId !== id || $('gameOptions').hidden) return;
  $('gameOptionsTitle').textContent = details.name;
  $('gameName').value = details.customName || details.name;
  $('metadataQuery').value = details.metadata ? details.metadata.title : details.originalName;
  var metadata = details.metadata;
  $('gameMetadataSummary').textContent = metadata ? [metadata.title, metadata.year, (metadata.developers || []).join(', '), (metadata.genres || []).join(' · '), metadata.language, metadata.source === 'local' ? 'PC' : 'Steam'].filter(Boolean).join(' · ') : 'Sem correspondência no catálogo.';
  if ($('gameDescription')) { $('gameDescription').textContent = metadata && metadata.description || ''; $('gameDescription').hidden = !$('gameDescription').textContent; }
  if ($('metadataCatalog')) $('metadataCatalog').hidden = !!(metadata && metadata.source === 'local');
}
function closeGameOptions() {
  $('gameOptions').hidden = true; gameOptionsId = ''; metadataSearchTicket++; focusSelectedGame();
}
if ($('gameSettings')) $('gameSettings').onclick = function () {
  if (busy || !$('transport').hidden || !$('modal').hidden || !$('setup').hidden || !$('options').hidden) return;
  var game = currentGame(); if (!game) return;
  gameOptionsId = game.id; metadataSearchTicket++;
  $('gameOptionsTitle').textContent = game.name; $('gameName').value = game.name; $('metadataQuery').value = game.name;
  $('gameMetadataSummary').textContent = ''; $('metadataResults').textContent = ''; $('metadataSearchState').textContent = '';
  if ($('gameDescription')) { $('gameDescription').textContent = ''; $('gameDescription').hidden = true; }
  if ($('metadataCatalog')) $('metadataCatalog').hidden = true;
  var icon = artworkMap[game.id] && artworkMap[game.id].icon; $('gameIcon').hidden = !icon;
  if (icon) { $('gameIcon').src = base + icon; $('gameIcon').onerror = function () { this.hidden = true; }; }
  $('gameOptions').hidden = false; focusControl($('closeGameOptions')); run(function () { return loadGameOptions(game.id); });
};
if ($('closeGameOptions')) {
  $('closeGameOptions').onclick = closeGameOptions;
  $('saveGameName').onclick = function () { var id = gameOptionsId, name = $('gameName').value; run(async function () {
    await api('/api/games/' + id + '/settings', { name: name }); await refresh(); await loadGameOptions(id); message('Nome salvo.');
  }); };
  $('resetGameName').onclick = function () { var id = gameOptionsId; run(async function () {
    await api('/api/games/' + id + '/settings', { name: null }); await refresh(); await loadGameOptions(id); message('Nome restaurado.');
  }); };
  $('searchMetadata').onclick = function () {
    var id = gameOptionsId, query = $('metadataQuery').value, ticket = ++metadataSearchTicket;
    $('metadataResults').textContent = ''; $('metadataSearchState').textContent = 'Buscando…';
    run(async function () {
      try {
        var matches = await api('/api/library/search?query=' + encodeURIComponent(query));
        if (ticket !== metadataSearchTicket || gameOptionsId !== id) return;
        $('metadataSearchState').textContent = matches.length ? 'Escolha o jogo para atribuir os metadados.' : 'Nenhum resultado. Tente outro nome ou o link Steam.';
        matches.forEach(function (match) {
          var result = button(match.name, async function () {
            if (gameOptionsId !== id) return;
            await api('/api/games/' + id + '/settings', { storeId: match.id });
            if (gameOptionsId !== id) return;
            metadataSearchTicket++; $('metadataResults').textContent = ''; $('metadataSearchState').textContent = 'Metadados atribuídos.'; await refresh(); await loadGameOptions(id);
            focusControl($('searchMetadata')); message('Metadados atribuídos. As imagens serão atualizadas.');
          }); $('metadataResults').appendChild(result);
        });
        if (matches.length) focusControl($('metadataResults').querySelector('button'));
      } catch (error) { if (ticket === metadataSearchTicket) $('metadataSearchState').textContent = error.message; throw error; }
    });
  };
}
async function refreshAudio() {
  await refreshDisplays();
  if (presentationMode === 'stream') { $('audio').textContent = 'Áudio da transmissão pelo Moonlight.'; selectedAudio = ''; return; }
  var devices = await api('/api/audio'), signature = JSON.stringify(devices);
  var focusedAudio = controllerFocus && $('audio').contains(controllerFocus) ? controllerFocus.dataset.device : null;
  if (signature === selectedAudio) return; selectedAudio = signature; $('audio').textContent = '';
  devices.forEach(function (device) {
    var label = device.name.startsWith('SAMSUNG') ? 'TV Samsung' : device.name.startsWith('EDIFIER') ? 'EDIFIER' : device.name.replace(/\s*\([^)]*\)\s*$/, '');
    var b = button(label + (device.current ? ' · Atual' : ''), async function () {
      await api('/api/audio', { id: device.id }); await refreshAudio(); message('Saída padrão do Windows alterada. Alguns jogos precisam atualizar a saída no próprio menu.');
    }, device.current ? 'current' : ''); b.dataset.device = device.id; $('audio').appendChild(b);
    if (focusedAudio === device.id) focusControl(b);
  });
}
function renderDisplays(state) {
  $('monitorsOn').disabled = $('monitorsOff').disabled = state.available === false;
  $('monitorsOn').classList.toggle('current', state.monitorsEnabled);
  $('monitorsOff').classList.toggle('current', !state.monitorsEnabled);
  $('monitorsOn').setAttribute('aria-pressed', String(state.monitorsEnabled));
  $('monitorsOff').setAttribute('aria-pressed', String(!state.monitorsEnabled));
  $('displayNote').textContent = state.note || (state.sessionActive ? 'TV principal · 4K / 60 Hz' : 'A escolha será aplicada ao jogar pela TV.');
}
async function refreshDisplays() { renderDisplays(await api('/api/displays')); }
async function setMonitors(enabled) {
  $('monitorsOn').disabled = $('monitorsOff').disabled = true;
  try { renderDisplays(await api('/api/displays', { monitorsEnabled: enabled })); }
  finally { try { await refreshDisplays(); } catch (_) { $('monitorsOn').disabled = $('monitorsOff').disabled = true; } }
}
async function refresh() {
  var result = await api('/api/status'); artworkMap = result.artwork || {}; renderGames(result.state.games);
  presentationMode = result.state.environment && result.state.environment.mode || 'cable';
  if (result.displays) renderDisplays(result.displays);
  connected = true; needsPair = false;
  $('connection').textContent = result.preview ? 'Prévia no PC' : result.simulation ? 'Simulação de testes' : 'PC conectado';
  $('setup').hidden = true; $('library').hidden = false;
  updateLiveBackground();
  if (result.state.startError) message(result.state.startError);
  var environmentNote = result.state.environment && result.state.environment.note || '';
  if (environmentNote) message(environmentNote);
  else if (previousEnvironmentNote && $('message').textContent === previousEnvironmentNote) message('');
  previousEnvironmentNote = environmentNote;
  renderPadGlyphs(result.controller);
  if ($('volumeButton') && !volumeChecked) { volumeChecked = true; loadVolume(false); }
  if (result.controller) {
    mouseEnabled = result.controller.enabled;
    syncPointer();
    $('touchpadMode').textContent = 'Mouse no touchpad: ' + (mouseEnabled ? 'ligado' : 'desligado');
    $('mouseHint').textContent = mouseEnabled ? 'Mouse no touchpad · ligado' : 'Touchpad · microfone 1,5 s';
    $('touchpadStatus').textContent = result.controller.connected ? (result.controller.touchAvailable ? 'Segure o botão de microfone por 1,5 s para ligar/desligar. Deslize para mover; pressione o touchpad para clicar ou mantenha pressionado e deslize para arrastar (janelas, barras de rolagem); pressione com dois dedos para clique direito. Durante a sessão na TV. Create + Options por 1 s abrem/ocultam o launcher; dois toques no microfone também funcionam no modo completo.' : 'Create + Options por 1 s abre o launcher. Bluetooth básico: microfone e touchpad indisponíveis neste modo.') : 'DualSense não encontrado para leitura compartilhada.';
  }
}
var liveBackground;
function updateLiveBackground() {
  var eligible = (desktop || (!local && typeof tizen !== 'undefined')) && presentationMode !== 'stream' && !(window.LGStream && window.LGStream.active);
  if (!liveBackground && eligible) liveBackground = desktop ? LGPlatform.desktop.background(document.documentElement, $('liveGameFrame'), inputClient) : LGPlatform.tv.background(document.documentElement);
  $('liveBackgroundSection').hidden = !eligible;
  if (!liveBackground) return;
  if (!eligible && !desktop && liveBackground.suspend) { liveBackground.suspend(); return; }
  var enabled = localStorage.getItem('live-game-background') !== 'false';
  $('liveBackgroundToggle').textContent = 'Jogo ao fundo: ' + (enabled ? 'ligado' : 'desligado');
  liveBackground.update(libraryGames.some(function (g) { return g.running; }), enabled && eligible, !document.hidden && connected, localStorage.getItem('lazygaming-hdmi'));
}
document.addEventListener('visibilitychange', updateLiveBackground);
function videoSources() { return LGPlatform.tv.sources(); }
async function probeTv() {
  if (local) return;
  try {
    var sources = await videoSources(), current = LGPlatform.tv.currentSource();
    if (!localStorage.getItem('lazygaming-hdmi') && current.type === 'HDMI' && sources.some(function (s) { return s.number === current.number; }))
      localStorage.setItem('lazygaming-hdmi', current.number);
    if (sources.length > 1) {
      $('hdmiOptions').textContent = ''; $('hdmiSection').hidden = false;
      sources.forEach(function (source) {
        $('hdmiOptions').appendChild(button('HDMI ' + source.number, async function () {
          localStorage.setItem('lazygaming-hdmi', source.number); await probeTv(); message('Entrada do PC selecionada: HDMI ' + source.number);
        }, Number(localStorage.getItem('lazygaming-hdmi')) === source.number ? 'current' : ''));
      });
    }
    await api('/api/tv', { stage: 'probe', version: '0.15.2', library: libraryGames.length, coversLoaded: Array.from($('games').querySelectorAll('img')).filter(function (i) { return i.complete && i.naturalWidth > 0; }).length, viewport: [innerWidth, innerHeight], hdmi: sources.map(function (s) { return s.number; }), current: { type: current.type, number: current.number }, selected: Number(localStorage.getItem('lazygaming-hdmi')) });
  }
  catch (e) { try { await api('/api/tv', { stage: 'probe-error', error: e.name + ': ' + e.message }); } catch (_) { } }
}
async function showHdmi() {
  if (desktop) { await api('/api/launcher/hide', {}); return; }
  if (local) { message('TV preparada. A troca de entrada é executada pelo app na TV.'); return; }
  try {
    var sources = await videoSources(), selected = null;
    var remembered = Number(localStorage.getItem('lazygaming-hdmi'));
    if (remembered) {
      selected = sources.find(function (s) { return s.number === remembered; });
      // Após habilitar display inativo, a TV pode demorar a anunciar o sinal.
      for (var attempt = 0; !selected && attempt < 12; attempt++) {
        await new Promise(function (resolve) { setTimeout(resolve, 400); });
        sources = await videoSources(); selected = sources.find(function (s) { return s.number === remembered; });
      }
      if (!selected) throw Error('A TV ainda não confirmou sinal na entrada HDMI do PC após a preparação. Tente Continuar novamente.');
    } else if (sources.length === 1) selected = sources[0];
    if (!selected) {
      if (!sources.length) throw Error('A TV não informou uma entrada HDMI conectada.');
      $('hdmiOptions').textContent = ''; $('hdmiSection').hidden = false;
      sources.forEach(function (source) {
        $('hdmiOptions').appendChild(button('HDMI ' + source.number, async function () {
          localStorage.setItem('lazygaming-hdmi', source.number); $('hdmiSection').hidden = true; await showHdmi();
        }));
      });
      $('options').hidden = false; message('Selecione a entrada HDMI em que o PC está conectado.'); focusControl($('hdmiOptions').querySelector('button')); return;
    }
    localStorage.setItem('lazygaming-hdmi', selected.number);
    await LGPlatform.tv.selectSource(selected);
    await api('/api/tv', { stage: 'source-selected', hdmi: selected.number });
    // hide() pode devolver a Home, mantendo HDMI apenas na prévia do Smart Hub.
    // Trazer o viewer da entrada para frente usa o aplicativo normal da TV.
    await api('/api/tv', { stage: 'viewer-launch-requested', hdmi: selected.number });
    await LGPlatform.tv.openViewer();
    await api('/api/tv', { stage: 'viewer-launched', hdmi: selected.number });
  } catch (e) {
    try { await api('/api/tv', { stage: 'switch-error', error: e.name + ': ' + e.message }); } catch (_) { }
    message('A troca automática não foi concluída: ' + e.message + ' Use Home para abrir o HDMI do PC.');
  }
}
async function connect() {
  base = normalize($('address').value); await proof(base); persist(); await refresh();
  focusSelectedGame();
  warmPresentation();
  // Preparar configurações/vínculo sem prender o clique nas capas.
  Promise.all([refreshAudio(), enrollRecovery()]).catch(function (error) { if (!busy) message(error.message); });
  probeTv();
}
$('newCode').onclick = function () { run(async function () {
  var result = await request(location.origin + '/api/local-pairing', { method: 'POST' }); $('pairCode').textContent = result.code;
  $('pcAddress').textContent = 'Endereço para a TV: ' + result.addresses.join(' ou ') + ' · Código válido por 5 minutos.';
  message('');
}); };
$('pair').onclick = function () { run(async function () {
  base = normalize($('address').value);
  var pair = await request(base + '/api/pair', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: $('code').value.trim() }) });
  session = pair; needsPair = false; persist(); $('code').value = ''; await connect();
}); };
$('reconnect').onclick = function () { run(connect); };
$('recoverConnection').onclick = function () { run(recoverConnection); };
updateRecovery();
$('refresh').onclick = function () { run(async function () { await refresh(); await refreshAudio(); }); };
$('rescan').onclick = function () { run(async function () { message('Buscando jogos instalados…'); var result = await api('/api/library/refresh', {}); await refresh(); message(result.count + ' jogos na biblioteca.'); }); };
if ($('openPcHub')) $('openPcHub').onclick = function () { run(async function () { await api('/api/hub/launch', {}); message('Central aberta no navegador do PC.'); }); };
$('settings').onclick = function () { if (!connected) { $('setup').hidden = false; focusControl($('find')); } else { $('options').hidden = false; focusControl(typeof window !== 'undefined' && window.LGStream && window.LGStream.active ? $('streamAuto') : $('closeOptions')); run(refreshAudio); } };
$('streamShortcut').onclick = function () { $('options').classList.add('stream-only'); $('options').hidden = false; focusControl($('streamAuto')); };
var openAllSettings = $('settings').onclick;
$('settings').onclick = function () { $('options').classList.remove('stream-only'); openAllSettings(); };
$('closeOptions').onclick = function () { $('options').hidden = true; focusSelectedGame(); };
$('closeSetup').onclick = function () {
  if (connected) {
    $('setup').hidden = true;
    if (setupParentOptions) { setupParentOptions = false; $('options').hidden = false; focusControl($('connectionSettings')); }
    else focusSelectedGame();
  } else focusControl($('find'));
};
$('connectionSettings').onclick = function () { setupParentOptions = true; $('options').hidden = true; $('setup').hidden = false; focusControl($('find')); };
$('heroPlay').onclick = function () { run(playSelected); };
$('moveToTv').onclick = function () { run(async function () {
  // Usa o jogo em execução, mesmo com outra capa selecionada.
  var state = await api('/api/status');
  var game = state.state.games.find(function (item) { return item.running; });
  if (!game) { await refresh(); throw Error('Nenhum jogo em execução para mover.'); }
  message('Movendo ' + game.name + ' para a TV…');
  await api('/api/games/' + game.id + '/move-to-tv', {});
  await refresh();
  message('A janela do jogo foi movida para a TV. Selecione Continuar.');
}); };
$('monitorsOn').onclick = function () { run(function () { return setMonitors(true); }); };
$('monitorsOff').onclick = function () { run(function () { return setMonitors(false); }); };
$('liveBackgroundToggle').onclick = function () {
  localStorage.setItem('live-game-background', localStorage.getItem('live-game-background') === 'false' ? 'true' : 'false'); updateLiveBackground();
};
$('heroStop').onclick = function () { run(async function () {
  var game = libraryGames.find(function (item) { return item.running; });
  if (!game || !await ask('Encerrar jogo?', 'Encerrar ' + game.name + '? O progresso não salvo pode ser perdido.', 'Encerrar jogo')) return;
  if (await stop(game)) { await refresh(); focusControl($('heroPlay')); }
}); };
$('find').onclick = function () { run(async function () {
  var ip = LGPlatform.tv.networkIp();
  if (!ip) { try { ip = new URL(normalize($('address').value)).hostname; } catch (_) { } }
  if (!/^192\.168\.\d{1,3}\.\d{1,3}$/.test(ip)) throw Error('Informe uma vez o endereço local do PC para buscar na mesma faixa de rede.');
  var prefix = ip.split('.').slice(0, 3).join('.') + '.', next = 1, found = null;
  message('Buscando PC…');
  async function worker() {
    while (next < 255 && !found) {
      var target = 'http://' + prefix + next++ + ':47831';
      try {
        var result = session ? await proof(target, 650) : await request(target + '/api/discover', {}, 650);
        if (!session || result.identity === session.identity) found = target;
      } catch (_) { }
    }
  }
  await Promise.all(Array.from({ length: 12 }, worker));
  if (!found) throw Error('PC não encontrado. Confira o auxiliar, a rede e a permissão do Firewall.');
  base = found; $('address').value = base;
  if (session) await connect(); else message('PC encontrado. Digite o código mostrado nele.');
}); };
function handleRemoteKey(event) {
  var before = controllerFocus, editing = before && before.tagName === 'INPUT';
  handleRemoteNavigation(event);
  if (editing) return;
  if (event.keyCode === VOLUME_KEY) return;
  // Ocultar o launcher toca o som de fechar no host; sem som de voltar duplicado.
  if (suppressBackSound) { suppressBackSound = false; return; }
  if ((event.keyCode === 10009 || event.key === 'Escape') && !event.repeat) playMenuSound('back');
  else if (event.keyCode === 13 && before && !before.disabled && !event.repeat) playMenuSound('confirm');
  else if (before !== controllerFocus && [37,38,39,40].indexOf(event.keyCode) !== -1) playMenuSound('move');
}
function handleRemoteNavigation(event) {
  if (handleVolumeKey(event)) return;
  var domFocus = document.activeElement;
  var active = (event.controller || !domFocus || domFocus.tagName === 'BODY') && controllerFocus && controllerFocus.isConnected ? controllerFocus : domFocus;
  var scope = $('firstRun') && !$('firstRun').hidden ? $('firstRun') : $('transport') && !$('transport').hidden ? $('transport') : !$('modal').hidden ? $('modal') : !$('setup').hidden ? $('setup') : $('gameOptions') && !$('gameOptions').hidden ? $('gameOptions') : $('advanced') && !$('advanced').hidden ? $('advanced') : !$('options').hidden ? $('options') : document;
  if ([13, 37, 38, 39, 40].indexOf(event.keyCode) !== -1 && scope !== document && scope.contains && (!active || !scope.contains(active))) {
    event.preventDefault();
    var first = Array.from(scope.querySelectorAll('button:not(:disabled),input')).filter(function (e) { return e.offsetParent !== null; })[0];
    focusControl(first); return;
  }
  var input = active && active.tagName === 'INPUT';
  if (!input && document.documentElement && [13, 37, 38, 39, 40, 10009].indexOf(event.keyCode) !== -1) document.documentElement.classList.add('controller-navigation');
  // Samsung IME Done/Cancel deixam o campo focado se o app não o remover.
  // Não bloquear as setas do teclado virtual enquanto ele está editando.
  if (input && ([65376, 65385, 13, 10009].indexOf(event.keyCode) !== -1 || event.key === 'Escape')) {
    active.blur();
    focusControl($(active.id === 'address' ? 'find' : active.id === 'gameName' ? 'saveGameName' : active.id === 'metadataQuery' ? 'searchMetadata' : 'pair'));
    return;
  }
  if (event.keyCode === 10009 || event.key === 'Escape') {
    event.preventDefault();
    if (event.repeat) return;
    if ($('firstRun') && !$('firstRun').hidden) { if (window.LGFirstRun) window.LGFirstRun.back(); return; }
    if ($('advanced') && !$('advanced').hidden) { if (window.LGAdvanced) window.LGAdvanced.close(); return; }
    if ($('transport') && !$('transport').hidden) $('cancelTransport').click();
    else if (!$('modal').hidden) $('cancel').click();
    else if (!$('setup').hidden) {
      if (connected) {
        $('setup').hidden = true;
        if (setupParentOptions) { setupParentOptions = false; $('options').hidden = false; focusControl($('connectionSettings')); }
        else focusSelectedGame();
      } else focusControl($('find'));
    }
    else if ($('gameOptions') && !$('gameOptions').hidden) closeGameOptions();
    else if (!$('options').hidden) { $('options').hidden = true; focusSelectedGame(); }
    else if (active && active.id === 'gameSettings') focusSelectedGame();
    else if (active && ['heroPlay','heroStop','moveToTv','streamShortcut','settings','returnGame'].indexOf(active.id) !== -1) focusSelectedGame();
    else if (typeof window !== 'undefined' && window.LGStream && window.LGStream.active) window.LGStream.fullscreen();
    else if (desktop) { suppressBackSound = true; api('/api/launcher/hide', {}).catch(function (e) { message(e.message); }); }
    else if (typeof tizen !== 'undefined' && !local) {
      if (libraryGames.some(function (g) { return g.running; })) run(presentationMode === 'stream' ? showStream : showHdmi);
      else LGPlatform.tv.exit();
    }
    else { $('options').hidden = false; focusControl($('closeOptions')); run(refreshAudio); }
    return;
  }
  if (event.keyCode === 13 && active && active.tagName === 'BUTTON') {
    event.preventDefault(); if (!active.disabled && !event.repeat) active.click(); return;
  }
  if ([37, 38, 39, 40].indexOf(event.keyCode) === -1 || input) return;
  event.preventDefault(); lastNavigationAt = Date.now();
  if (scope === document && active && active.classList && active.classList.contains('game-tile')) {
    if (event.keyCode === 37 || event.keyCode === 39) {
      var tiles = Array.from($('games').querySelectorAll('button')), tileIndex = tiles.indexOf(active);
      focusControl(tiles[Math.max(0, Math.min(tiles.length - 1, tileIndex + (event.keyCode === 37 ? -1 : 1)))]);
    } else if (event.keyCode === 38 && $('gameSettings') && !event.repeat) { focusControl($('gameSettings')); }
    else if (event.keyCode === 40) { focusControl($('heroPlay').disabled ? dockFallback() : $('heroPlay')); }
    return;
  }
  if (scope === document && active) {
    if (active.id === 'gameSettings') {
      if (event.keyCode === 38) { if (!event.repeat) active.click(); return; }
      if (event.keyCode === 40) { focusSelectedGame(); return; }
      if (event.keyCode === 37) { focusSelectedGame(); return; }
      if (event.keyCode === 39) { focusControl($('heroPlay').disabled ? dockFallback() : $('heroPlay')); return; }
    }
    if (['heroPlay','heroStop','moveToTv','streamShortcut','settings','returnGame'].indexOf(active.id) !== -1) {
      if (event.keyCode === 38) { focusSelectedGame(); return; }
      if (event.keyCode === 40) return;
      var dock = [$('heroPlay'),$('heroStop'),$('moveToTv'),$('streamShortcut'),$('settings'),$('returnGame')].filter(function (item) { return item && !item.hidden && !item.disabled; });
      var dockIndex = dock.indexOf(active);
      focusControl(dock[Math.max(0,Math.min(dock.length-1,dockIndex+(event.keyCode === 37 ? -1 : 1)))]); return;
    }
  }
  var controls = Array.from(scope.querySelectorAll('button:not(:disabled),input')).filter(function (e) { return e.offsetParent !== null; });
  if (active && active.getBoundingClientRect) {
    var origin = active.getBoundingClientRect(), ox = origin.left + origin.width / 2, oy = origin.top + origin.height / 2;
    var horizontal = event.keyCode === 37 || event.keyCode === 39, sign = event.keyCode === 37 || event.keyCode === 38 ? -1 : 1;
    var nearest = controls.filter(function (e) { return e !== active; }).map(function (e) {
      var r = e.getBoundingClientRect(), dx = r.left + r.width / 2 - ox, dy = r.top + r.height / 2 - oy;
      var main = horizontal ? dx : dy, cross = horizontal ? dy : dx;
      return { element: e, score: main * sign > 8 ? Math.abs(main) + Math.abs(cross) * 2 : Infinity };
    }).sort(function (a, b) { return a.score - b.score; })[0];
    if (nearest && isFinite(nearest.score)) { focusControl(nearest.element); return; }
  }
  var index = controls.indexOf(active), direction = event.keyCode === 37 || event.keyCode === 38 ? -1 : 1;
  if (controls.length) focusControl(controls[(index + direction + controls.length) % controls.length]);
}
var menuAudioContext, menuSoundAt = 0, suppressBackSound = false, launcherOpenBuffer = null;
// Som de abrir: buffer decodificado antes, disparado no primeiro quadro desenhado depois que
// o host mostra a janela (rAF só roda com a página visível). Nunca antes do pixel; o atraso
// restante é a latência de saída do áudio (~10–40 ms), abaixo do perceptível como dessincronia.
function launcherAudio() {
  if (typeof window === 'undefined') return null;
  var Context = window.AudioContext || window.webkitAudioContext; if (!Context) return null;
  if (!menuAudioContext) menuAudioContext = new Context();
  return menuAudioContext;
}
function prepareLauncherSounds() {
  var context = launcherAudio(); if (!context || launcherOpenBuffer || typeof fetch === 'undefined') return;
  fetch('assets/audio/launcher-open.wav').then(function (r) { if (!r.ok) throw Error(); return r.arrayBuffer(); })
    .then(function (data) { return new Promise(function (resolve, reject) { context.decodeAudioData(data, resolve, reject); }); })
    .then(function (buffer) { launcherOpenBuffer = buffer; }).catch(function () {});
}
function playLauncherOpen() {
  var context = launcherAudio(); if (!context || !launcherOpenBuffer) return;
  function start() { var source = context.createBufferSource(); source.buffer = launcherOpenBuffer; source.connect(context.destination); source.onended = function () { source.disconnect(); }; source.start(); }
  try { if (context.state === 'suspended') context.resume().then(start).catch(function () {}); else start(); } catch (_) { }
}
if (typeof window !== 'undefined' && window.chrome && window.chrome.webview) window.chrome.webview.addEventListener('message', function (event) {
  if (event.data && event.data.type === 'launcher-shown') requestAnimationFrame(playLauncherOpen);
});
// Decodificar já no carregamento do launcher PC, antes da primeira abertura.
if (typeof location !== 'undefined' && (desktop || /desktop=/.test(location.hash))) prepareLauncherSounds();
function playMenuSound(kind) {
  if (typeof window === 'undefined') return;
  var Context = window.AudioContext || window.webkitAudioContext;
  if (!Context || (kind === 'move' && Date.now() - menuSoundAt < 75)) return;
  menuSoundAt = Date.now();
  try {
    if (!menuAudioContext) menuAudioContext = new Context();
    var context = menuAudioContext;
    function tone() {
      var now = context.currentTime, notes = kind === 'confirm' ? [660,880] : kind === 'back' ? [480,340] : [560];
      notes.forEach(function (frequency,index) {
        var start = now + index * .045, length = kind === 'move' ? .055 : .09;
        var oscillator = context.createOscillator(), gain = context.createGain(); oscillator.type = 'sine'; oscillator.frequency.value = frequency;
        gain.gain.setValueAtTime(0,start); gain.gain.linearRampToValueAtTime(.035,start+.004); gain.gain.exponentialRampToValueAtTime(.0001,start+length);
        oscillator.connect(gain); gain.connect(context.destination); oscillator.start(start); oscillator.stop(start+length+.01);
        oscillator.onended = function () { oscillator.disconnect(); gain.disconnect(); };
      });
    }
    if (context.state === 'suspended') context.resume().then(tone).catch(function () {}); else tone();
  } catch (_) { }
}
// Volume rápido: botão superior do controle (ou clique no chip) abre; setas/analógico ajustam de 5 em 5.
var VOLUME_KEY = 1001, volumeLevel = null, volumeMuted = false, volumeChecked = false, volumeSending = false, volumePending = false, volumeHideTimer, volumeTicket = 0;
function volumeOpen() { var hud = $('volumeHud'); return !!hud && !hud.hidden; }
// Glifos no padrão dos botões do controle: face clara, símbolo escuro, pela posição física.
var PAD_FACES = { dualsense: { south: 'cross', east: 'circle', north: 'triangle' }, xinput: { south: 'A', east: 'B', north: 'Y' }, 'switch-pro': { south: 'B', east: 'A', north: 'X' } };
var padSignature = '', padGradient = 0;
// Cada SVG tem seu gradiente: referência a defs em elemento oculto (display:none) não pinta.
function padDefs() { var id = 'padFace' + (++padGradient); return { id: id, defs: '<defs><linearGradient id="' + id + '" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f4f5f8"/><stop offset="1" stop-color="#c7cbd3"/></linearGradient></defs>' }; }
function padFaceSvg(symbol) {
  var marks = { cross: '<path d="M8.2 8.2l7.6 7.6M15.8 8.2l-7.6 7.6"/>', circle: '<circle cx="12" cy="12" r="4.6"/>', triangle: '<path d="M12 7.1l5 8.6H7z"/>' }[symbol];
  var inner = marks ? '<g fill="none" stroke="#3a3f4a" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round">' + marks + '</g>'
    : '<text x="12" y="16.3" text-anchor="middle" font-size="12.5" font-weight="700" font-family="Segoe UI,Arial,sans-serif" fill="#3a3f4a">' + symbol + '</text>';
  var face = padDefs();
  return '<svg viewBox="0 0 24 24" aria-hidden="true">' + face.defs + '<circle cx="12" cy="12" r="11" fill="url(#' + face.id + ')" stroke="#8d94a1" stroke-width=".8"/>' + inner + '</svg>';
}
function padDpadSvg(horizontal) {
  var arrows = horizontal ? '<path d="M5.1 12l2.6-2.2v4.4zM18.9 12l-2.6-2.2v4.4z"/>' : '<path d="M5.1 12l2.6-2.2v4.4zM18.9 12l-2.6-2.2v4.4zM12 5.1l-2.2 2.6h4.4zM12 18.9l-2.2-2.6h4.4z"/>';
  var face = padDefs();
  return '<svg viewBox="0 0 24 24" aria-hidden="true">' + face.defs + '<path d="M9 2.5h6a1 1 0 0 1 1 1V8h4.5a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H16v4.5a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V16H3.5a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1H8V3.5a1 1 0 0 1 1-1z" fill="url(#' + face.id + ')" stroke="#8d94a1" stroke-width=".8"/>' +
    '<g fill="#3a3f4a">' + arrows + '</g></svg>';
}
function renderPadGlyphs(controller) {
  if (!desktop || typeof document === 'undefined' || !document.querySelectorAll) return;
  var adapter = controller && PAD_FACES[controller.adapterId] ? controller.adapterId : 'dualsense', swap = !!(controller && controller.swapConfirmBack);
  var faces = PAD_FACES[adapter], signature = adapter + ':' + swap;
  if (signature === padSignature) return; padSignature = signature;
  var names = { cross: '✕', circle: '○', triangle: '△' };
  var map = { confirm: faces[swap ? 'east' : 'south'], back: faces[swap ? 'south' : 'east'], top: faces.north };
  Array.prototype.forEach.call(document.querySelectorAll('[data-pad]'), function (node) {
    var kind = node.getAttribute('data-pad');
    node.classList.add('pad-glyph');
    if (kind === 'dpad-h' || kind === 'dpad') { node.innerHTML = padDpadSvg(kind === 'dpad-h'); node.setAttribute('aria-label', 'Direcional'); return; }
    var symbol = map[kind]; node.innerHTML = padFaceSvg(symbol); node.setAttribute('aria-label', names[symbol] || symbol);
  });
}
function deviceLabel(name) { return !name ? 'Volume' : name.indexOf('SAMSUNG') === 0 ? 'TV Samsung' : name.replace(/\s*\([^)]*\)\s*$/, ''); }
function renderVolume(state) {
  if (state) { volumeLevel = state.level; volumeMuted = !!state.muted; $('volumeDevice').textContent = deviceLabel(state.device); }
  if (volumeLevel === null) return;
  var silent = volumeMuted || volumeLevel === 0;
  $('volumeButton').hidden = false; $('volumeHint').hidden = false;
  $('volumeChipLevel').textContent = volumeMuted ? 'Mudo' : volumeLevel + '%';
  $('volumeValue').textContent = volumeMuted ? 'Mudo' : String(volumeLevel);
  $('volumeFill').style.width = (volumeMuted ? 0 : volumeLevel) + '%';
  $('volumeTrack').setAttribute('aria-valuenow', String(volumeMuted ? 0 : volumeLevel));
  $('volumeButton').setAttribute('aria-label', 'Volume ' + (volumeMuted ? 'mudo' : volumeLevel + '%'));
  $('volumeButton').classList.toggle('muted', silent); $('volumeHud').classList.toggle('muted', silent);
}
async function loadVolume(report) {
  try { renderVolume(await api('/api/volume')); }
  catch (e) { if (report) message('Volume indisponível: ' + e.message); else if (volumeLevel === null) { $('volumeButton').hidden = true; $('volumeHint').hidden = true; } }
}
function scheduleVolumeHide() { clearTimeout(volumeHideTimer); volumeHideTimer = setTimeout(closeVolume, 3500); }
function openVolume() {
  if (!connected) return;
  $('volumeHud').hidden = false; renderVolume(); scheduleVolumeHide();
  var ticket = ++volumeTicket;
  // Leitura fresca para refletir mudanças feitas pelo Windows ou pelo próprio jogo.
  if (!volumeSending) api('/api/volume').then(function (state) { if (ticket === volumeTicket && !volumeSending && !volumePending) renderVolume(state); })
    .catch(function (e) { message('Volume indisponível: ' + e.message); closeVolume(); });
}
function closeVolume() { clearTimeout(volumeHideTimer); $('volumeHud').hidden = true; }
function pulseVolume(direction) {
  var target = $(direction > 0 ? 'volumeUp' : 'volumeDown'); target.classList.add('pulse');
  setTimeout(function () { target.classList.remove('pulse'); }, 120);
}
function stepVolume(direction) {
  if (volumeLevel === null) return;
  var current = volumeMuted && direction > 0 ? 0 : volumeLevel;
  var next = Math.max(0, Math.min(100, direction > 0 ? Math.floor(current / 5) * 5 + 5 : Math.ceil(current / 5) * 5 - 5));
  volumeTicket++; pulseVolume(direction); scheduleVolumeHide();
  if (next === volumeLevel && !(volumeMuted && direction > 0)) return;
  volumeLevel = next; if (next > 0) volumeMuted = false; renderVolume(); playMenuSound('move'); sendVolume();
}
async function sendVolume() {
  if (volumeSending) { volumePending = true; return; }
  volumeSending = true;
  try {
    var applied;
    do { volumePending = false; applied = await api('/api/volume', { level: volumeLevel }); } while (volumePending);
    renderVolume(applied);
  } catch (e) { message('Não foi possível ajustar o volume: ' + e.message); loadVolume(false); }
  finally { volumeSending = false; }
}
function handleVolumeKey(event) {
  if (event.keyCode === VOLUME_KEY) {
    if (event.preventDefault) event.preventDefault();
    if (!event.repeat) { if (volumeOpen()) closeVolume(); else openVolume(); }
    return true;
  }
  if (!volumeOpen()) return false;
  if ([37, 38, 39, 40].indexOf(event.keyCode) !== -1) { event.preventDefault(); stepVolume(event.keyCode === 38 || event.keyCode === 39 ? 1 : -1); return true; }
  if (event.keyCode === 13 || event.keyCode === 10009 || event.key === 'Escape') { event.preventDefault(); if (!event.repeat) closeVolume(); return true; }
  return false;
}
if ($('volumeButton')) {
  $('volumeButton').onclick = function () { if (volumeOpen()) closeVolume(); else openVolume(); };
  $('volumeUp').onclick = function () { stepVolume(1); };
  $('volumeDown').onclick = function () { stepVolume(-1); };
}
document.addEventListener('keydown', handleRemoteKey);
document.addEventListener('click', function (event) { if (event.detail > 0 && event.target.closest('button:not(:disabled)')) playMenuSound('confirm'); });
document.addEventListener('focusin', function (event) {
  var target = event.target;
  if (target && (target.tagName === 'BUTTON' || target.tagName === 'INPUT')) markControlFocus(target);
});
function pollConnection() {
  if (!session || document.hidden || busy || needsPair || connectionPolling) return;
  if (typeof window !== 'undefined' && window.LGStream && window.LGStream.active && !window.LGStream.menuVisible) return;
  if (!connected) {
    if (document.activeElement && document.activeElement.tagName === 'INPUT') return;
    run(desktop ? refresh : connect); // Recupera também a falha inicial, mesmo com a tela Conexão visível.
  } else if ($('setup').hidden) {
    connectionPolling = true;
    Promise.resolve().then(refresh).catch(function (error) {
      if (error.retryable) { connected = false; $('connection').textContent = 'Reconectando ao PC…'; updateLiveBackground(); }
      if (!busy) message(error.message);
    }).finally(function () { connectionPolling = false; });
  }
}
document.addEventListener('visibilitychange', pollConnection);
setInterval(pollConnection, 4000);
function updateClock() { $('clock').textContent = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: 'America/Sao_Paulo' }); }
updateClock(); setInterval(updateClock, 30000);
window.addEventListener('resize', function () {
  if (selectedGameId) selectGame(selectedGameId, true);
  if (controllerFocus === $('gameSettings')) metadataTab(true);
});
async function initialize() {
  var token = new URLSearchParams(location.hash.slice(1)).get('desktop');
  if (local && (token || desktop)) {
    try {
      await request(location.origin + '/desktop-session', token ? { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-LG-Client': inputClient }, body: JSON.stringify({ token: token }) } : { headers: { 'X-LG-Client': inputClient } });
      desktop = true; localStorage.setItem('desktop-client', 'true'); session = { desktop: true }; base = location.origin;
      if ($('pcHubSection')) $('pcHubSection').hidden = false;
      $('streamShortcut').hidden = true; $('streamSettings').hidden = true; if ($('advancedEntry')) $('advancedEntry').hidden = false;
      document.documentElement.classList.add('controller-navigation', 'desktop-launcher'); prepareLauncherSounds(); renderPadGlyphs(null);
      history.replaceState(null, '', location.pathname); document.title = 'LazyGaming — PC';
      var back = button('Ocultar launcher', function () { return api('/api/launcher/hide', {}); }); back.id = 'returnGame'; $('dockActions').appendChild(back);
    } catch (_) { desktop = false; localStorage.removeItem('desktop-client'); }
  }
  if (session && desktop) (async function () {
    // Primeiro uso no próprio launcher; a biblioteca carrega depois (ou direto, se já configurado).
    if (document.readyState === 'loading') await new Promise(function (resolve) { document.addEventListener('DOMContentLoaded', resolve, { once: true }); });
    if (window.LGFirstRun) { try { await window.LGFirstRun.start(); } catch (_) { $('firstRun').hidden = true; } }
    run(async function () { await refresh(); focusSelectedGame(); });
  })();
  else if (session) run(connect);
  else focusControl(local ? $('newCode') : $('find'));
  syncLauncher();
}
// Entrada HID do auxiliar: independe da identificação/mapping da Gamepad API do navegador.
async function nativeControllerLoop() {
  for (;;) {
    if (!desktop) { await new Promise(function (r) { setTimeout(r, 1000); }); continue; }
    try {
      var input = await api('/api/launcher/input?client=' + inputClient);
      if (input.key) document.documentElement.classList.add('controller-navigation');
      if (input.key) {
        var error = '';
        try { handleRemoteKey({ keyCode: input.key, controller: true, repeat: !!input.repeat, preventDefault: function () {} }); } catch (e) { error = e.message; }
        api('/api/launcher/input/ack?client=' + inputClient, { key: input.key, selected: selectedGameId, focus: controllerFocus && (controllerFocus.dataset.id || controllerFocus.id), error: error }).catch(function () {});
      }
    } catch (_) { await new Promise(function (r) { setTimeout(r, 1000); }); }
  }
}
nativeControllerLoop(); initialize();

var cursorIdleTimer;
// Mouse físico liberado no primeiro uso e nas configurações avançadas (instalação é feita com mouse).
function panelOpen(id) { var node = document.getElementById ? document.getElementById(id) : null; return !!node && !node.hidden; }
function pointerAllowed() { return mouseEnabled || panelOpen('firstRun') || panelOpen('advanced'); }
function syncPointer() { if (desktop) document.documentElement.classList.toggle('mouse-navigation', pointerAllowed()); }
document.addEventListener('mousemove', function (event) {
  if (desktop) {
    if (!pointerAllowed() || (!event.movementX && !event.movementY)) return;
    document.documentElement.classList.add('mouse-navigation');
    document.documentElement.classList.remove('controller-navigation', 'cursor-idle');
    clearTimeout(cursorIdleTimer);
    cursorIdleTimer = setTimeout(function () { document.documentElement.classList.add('cursor-idle'); }, 1800);
    return;
  }
  if (!event.movementX && !event.movementY) return;
  document.documentElement.classList.remove('controller-navigation');
  var tile = event.target.closest && event.target.closest('.game-tile');
  if (tile) selectGame(tile.dataset.id, false);
});
