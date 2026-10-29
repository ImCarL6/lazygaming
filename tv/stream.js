/* Embedded video stays in one persistent frame; opening the menu never restarts it. */
(function () {
  'use strict';
  var frame, ready, sequence = 0, pending = {}, active = false, menu = true, configured = false, setupConfig, applying = false, pairedHash = null;
  var settings; try { settings = JSON.parse(localStorage.getItem('lg-stream-settings') || '{}'); } catch (_) { settings = {}; }
  var currentProfile, sessionProfile, guard = new LGStreamPolicy.Guard(), reconnecting = false, appliedAuto = false;
  var sharedRevision = -1, sharedBusy = false, editing = false, saveTimer;
  var television = !desktop && !local && typeof tizen !== 'undefined';
  async function saveShared(action) {
    var result = await api('/api/stream/controls', { settings: settings, action: action || null });
    editing = false; if (result && result.revision) sharedRevision = result.revision;
    return result;
  }
  function changed() {
    editing = true; clearTimeout(saveTimer);
    saveTimer = setTimeout(function () { saveShared().catch(function (e) { note(e.message); }); }, 300);
  }
  async function reportShared(completed) {
    return api('/api/stream/report', { completed: completed || 0, active: active, notice: document.getElementById('streamNote').textContent, profile: currentProfile && active ? currentProfile.width + ' × ' + currentProfile.height + ' · ' + currentProfile.fps + ' FPS · ' + currentProfile.bitrate + ' Mbps · ' + currentProfile.codec : '' });
  }
  async function syncShared() {
    if (sharedBusy || document.hidden || (typeof connected !== 'undefined' && !connected)) return;
    sharedBusy = true;
    try {
      var state = await api('/api/stream/controls'); if (!state || !state.settings) return;
      if (state.revision === 0 && television) { await saveShared(); return; }
      if (state.revision !== sharedRevision && !editing && !applying) { settings = state.settings; sharedRevision = state.revision; label(); }
      var status = document.getElementById('streamSharedStatus');
      status.textContent = state.online ? (state.active ? 'Transmissão ativa na TV' : 'TV conectada') : 'Abra o LazyGaming na TV para transmitir.';
      if (!television) { document.getElementById('streamProfile').textContent = state.profile || ''; if (state.notice && !editing) note(state.notice); }
      if (television && state.command && !applying) {
        var completed = state.command.id;
        try {
          settings = state.settings; label();
          if (state.command.action === 'apply') await apply();
          else if (state.command.action === 'start') await start();
          else { await command('stop'); sessionProfile = null; showMenu(); }
        } catch (e) { note(e.message); }
        finally { await reportShared(completed); }
      } else if (television) await reportShared(0);
    } catch (_) { /* A conexão principal apresenta erros de rede; não interromper o controle. */ }
    finally { sharedBusy = false; }
  }
  async function remoteAction(action) { clearTimeout(saveTimer); await saveShared(action); note('Pedido enviado à TV.'); await syncShared(); }
  function note(text) { var e = document.getElementById('streamNote'); if (e.textContent !== text) e.textContent = text; }
  function profileLabel() { if (currentProfile) document.getElementById('streamProfile').textContent = currentProfile.width + ' × ' + currentProfile.height + ' · ' + currentProfile.fps + ' FPS · ' + currentProfile.bitrate + ' Mbps · ' + currentProfile.codec; }
  function label() {
    document.getElementById('streamAuto').textContent = 'Qualidade: ' + (LGStreamPolicy.adaptive(settings) ? 'Automática' : 'Manual');
    document.getElementById('streamResolution').textContent = 'Resolução: ' + (settings.resolution || 'auto').replace('auto', 'Automática');
    document.getElementById('streamBitrate').textContent = 'Bitrate: ' + (settings.bitrate || 'Automático') + (settings.bitrate ? ' Mbps' : '');
    document.getElementById('streamCodec').textContent = 'Codec: ' + (settings.codec || 'HEVC');
    document.getElementById('streamFps').textContent = 'Quadros: ' + (settings.fps || 60) + ' FPS';
  }
  function showMenu() {
    menu = true; document.body.classList.remove('stream-fullscreen');
    document.getElementById('options').hidden = true;
    if (frame) frame.blur(); window.focus();
    if (typeof focusSelectedGame === 'function') focusSelectedGame();
    if (typeof refresh === 'function' && typeof run === 'function') run(refresh);
  }
  function fullscreen() {
    if (!active) return; menu = false; document.body.classList.add('stream-fullscreen');
    if (frame) frame.contentWindow.focus();
  }
  function ensure() {
    if (ready) return ready;
    ready = new Promise(function (resolve, reject) {
      frame = document.createElement('iframe'); frame.id = 'streamFrame'; frame.title = 'Transmissão'; frame.src = 'stream-engine/engine.html'; document.body.prepend(frame);
      var timeout = setTimeout(function () { reject(Error('Motor de vídeo não carregou.')); }, 20000);
      window.addEventListener('message', function (event) {
        if (event.source !== frame.contentWindow || !event.data || event.data.lazyStream !== true) return;
        var data = event.data;
        if (data.type === 'ready') { clearTimeout(timeout); resolve(); }
        else if (data.type === 'reply') {
          var task = pending[data.value.id]; if (!task) return; delete pending[data.value.id]; clearTimeout(task.timer);
          if (data.value.ok) task.resolve(); else task.reject(Error(data.value.error));
        } else if (data.type === 'pair') {
          api('/api/stream/pair', { pin: data.value.pin }).catch(function (e) { message(e.message); });
        } else if (data.type === 'paired') pairedHash = LGCrypto.sha(data.value.certificate);
        else if (data.type === 'menu') showMenu();
        else if (data.type === 'playing') { active = true; document.body.classList.add('stream-live'); }
        else if (data.type === 'stopped') { active = false; document.body.classList.remove('stream-live', 'stream-fullscreen'); }
        else if (data.type === 'error') { showMenu(); message(data.value); }
        else if (data.type === 'warning') {
          guard.warning(String(data.value || ''), Date.now());
          note(data.value ? 'Conexão instável com o PC.' + (appliedAuto ? ' A qualidade será reduzida se persistir.' : ' Reduza o bitrate ou ative a qualidade automática.') : '');
        }
      });
    });
    return ready;
  }
  async function command(type, value) {
    await ensure();
    return new Promise(function (resolve, reject) {
      var id = ++sequence;
      pending[id] = { resolve: resolve, reject: reject, timer: setTimeout(function () { delete pending[id]; reject(Error('A transmissão demorou para responder.')); }, 30000) };
      frame.contentWindow.postMessage({ lazyStream: true, type: type, value: value, id: id }, '*');
    });
  }
  async function prepare() {
    if (typeof tizen === 'undefined' || desktop || local) throw Error('A transmissão integrada é usada na TV.');
    setupConfig = await api('/api/stream/configuration'); setupConfig.address = new URL(base).hostname;
    if (!configured) { await command('setup', setupConfig); await api('/api/stream/confirm', { certHash: pairedHash }); configured = true; }
  }
  async function start(override) {
    if (active) { fullscreen(); return; }
    await prepare();
    var limits = LGPlatform.tv.streamLimits();
    var requested = override || sessionProfile;
    var profile = requested ? LGStreamPolicy.profile(setupConfig.source, { resolution: requested.width + 'x' + requested.height, fps: requested.fps, bitrate: requested.bitrate, codec: requested.codec }, limits) : LGStreamPolicy.profile(setupConfig.source, settings, limits);
    if (!override) appliedAuto = LGStreamPolicy.adaptive(settings);
    message('Conectando a transmissão…'); await command('start', profile);
    currentProfile = profile; guard.reset(Date.now()); profileLabel(); note('');
    message(''); fullscreen();
  }
  async function apply() {
    if (applying) return; applying = true;
    try {
      localStorage.setItem('lg-stream-settings', JSON.stringify(settings));
      sessionProfile = null;
      if (active) { await command('stop'); await start(); showMenu(); document.getElementById('options').hidden = false; focusControl(document.getElementById('streamApply')); }
      else message('Qualidade salva.');
    } finally { applying = false; }
  }
  function cycle(key, choices) {
    var current = settings[key]; settings[key] = choices[(choices.indexOf(current) + 1) % choices.length];
    if (key === 'bitrate') settings.adaptive = !settings.bitrate;
    else { settings.adaptive = false; if (!settings.bitrate) settings.bitrate = currentProfile ? currentProfile.bitrate : 20; }
    label(); changed(); note('Selecione Aplicar qualidade para confirmar.');
  }
  document.getElementById('streamAuto').onclick = function () {
    var enabled = !LGStreamPolicy.adaptive(settings); settings.adaptive = enabled;
    if (enabled) { settings.bitrate = 0; settings.resolution = 'auto'; }
    else if (!settings.bitrate) settings.bitrate = currentProfile ? currentProfile.bitrate : 20;
    label(); changed(); note('Selecione Aplicar qualidade para confirmar.');
  };
  document.getElementById('streamResolution').onclick = function () { cycle('resolution', ['auto', '1920x1080', '2560x1440', '3840x2160']); };
  document.getElementById('streamBitrate').onclick = function () { cycle('bitrate', [0, 6, 8, 10, 12, 15, 20, 25, 30, 40, 60, 80]); };
  document.getElementById('streamCodec').onclick = function () { cycle('codec', ['HEVC', 'H264']); };
  document.getElementById('streamFps').onclick = function () { cycle('fps', [60, 30]); };
  document.getElementById('streamApply').onclick = function () { run(function () { return remoteAction('apply'); }); };
  document.getElementById('streamTest').onclick = function () { if (!applying) run(function () { return remoteAction('start'); }); };
  document.getElementById('streamDisconnect').onclick = function () { if (!applying) run(function () { return remoteAction('stop'); }); };
  setInterval(function () {
    if (!active || applying || reconnecting || document.hidden || (typeof busy !== 'undefined' && busy) || !currentProfile || !appliedAuto) return;
    var next = guard.next(currentProfile, Date.now()); if (!next) return;
    reconnecting = true; applying = true;
    var wasMenu = menu, wasOptions = !document.getElementById('options').hidden;
    var focused = document.activeElement && document.activeElement.id;
    (async function () {
      try {
        note('Ajustando qualidade…'); await command('stop'); sessionProfile = next; await start(next);
        if (wasMenu) { showMenu(); document.getElementById('options').hidden = !wasOptions; var target = document.getElementById(focused); if (target && target.offsetParent !== null) focusControl(target); }
        note('Qualidade reduzida para estabilizar a conexão.');
      } catch (e) { sessionProfile = null; showMenu(); note('Não foi possível ajustar. Reconecte a transmissão ou selecione qualidade manual.'); }
      finally { reconnecting = false; applying = false; }
    }());
  }, 1000);
  document.addEventListener('keydown', function (e) {
    if (active && !menu && (e.keyCode === 10009 || e.keyCode === 27)) { e.preventDefault(); e.stopImmediatePropagation(); if (!e.repeat) showMenu(); }
  }, true);
  window.LGStream = { start: start, prepare: prepare, fullscreen: fullscreen, get active() { return active; }, get menuVisible() { return menu; } };
  document.getElementById('streamSettings').hidden = false;
  document.getElementById('streamTest').textContent = television ? 'Retomar vídeo' : 'Iniciar na TV';
  setInterval(syncShared, 2000);
  label();
}());
