/* LazyGaming lifecycle over the pinned Moonlight Tizen 1.19.0 motor. */
'use strict';
// Upstream protocol diagnostics can include request URLs and certificates. Keep them private.
console.log = console.info = console.debug = console.warn = console.error = function () {};
var Module, host, configuration, initialized, phase = "carregar motor", active = false, terminating = null, pendingStart = null;
var lastStatsAt = 0;
var t = function (s) { return s; };
function emit(type, value) { parent.postMessage({ lazyStream: true, type: type, value: value }, '*'); }
function signalStopped(code) {
  active = false; stopAudioScheduler();
  if (pendingStart) { pendingStart.reject(Error('O player não iniciou o vídeo (' + code + ').')); pendingStart = null; }
  if (terminating) { var done = terminating; terminating = null; done(); }
  else if (code) emit('error', code === -200 ? 'A TV recusou este formato de vídeo. Reduza a resolução ou troque o codec.' : 'A transmissão foi interrompida (' + code + ').');
  emit('stopped');
}
function handleMessage(msg) {
  if (typeof msg !== 'string') return;
  if (msg.indexOf('streamTerminated: ') === 0) signalStopped(parseInt(msg.slice(18), 10));
  else if (msg === 'Connection Established') { active = true; emit('playing'); if (pendingStart) { pendingStart.resolve(); pendingStart = null; } }
  else if (msg.indexOf('StatMsg: ') === 0 && Date.now() - lastStatsAt >= 1000) { lastStatsAt = Date.now(); emit('stats', msg.slice(9)); }
  else if (msg.indexOf('WarningMsg: ') === 0) emit('warning', msg.slice(12));
}
function stored() { try { return JSON.parse(localStorage.getItem('lg-stream-identity') || 'null'); } catch (_) { return null; } }
async function setup(config) {
  phase = "carregar motor"; await initialized;
  configuration = config;
  var identity = stored();
  if (!identity) { identity = { uid: uniqueid(), cert: await sendMessage('makeCert', []) }; localStorage.setItem('lg-stream-identity', JSON.stringify(identity)); }
  await sendMessage('httpInit', [identity.cert.cert, identity.cert.privateKey, identity.uid]);
  host = new NvHTTP(config.address, identity.uid, config.address); host.httpPort = 47989; host.httpsPort = 47984;
  // Public certificate arrives through the already authenticated LazyGaming channel.
  host.ppkstr = config.certificate; host.serverUid = config.serverUid;
  phase = "identificar PC";
  try { await host.refreshServerInfo(); }
  catch (e) {
    if (Number(e) === -100 || identity.trustedServerUid === config.serverUid) throw Error('O vínculo existente não foi confirmado. Não será criado outro pareamento automaticamente.');
    host.ppkstr = null; await host.refreshServerInfo();
  }
  if (host.serverUid !== config.serverUid || !host.online && !host.appVersion) throw Error('O PC não confirmou sua identidade.');
  host._baseUrlHttp = 'http://' + config.address + ':' + host.httpPort;
  host._baseUrlHttps = 'https://' + config.address + ':' + host.httpsPort;
  if (!host.paired) {
    phase = "parear cliente";
    var pin = String(cryptoRand(10000)).padStart(4, '0');
    // Start challenge before asking the host to approve this exact pending request.
    var pairing = host.pair(pin); emit('pair', { pin: pin });
    if (!await pairing) throw Error('Pareamento não confirmado.');
    await host.refreshServerInfo();
  }
  if (host.ppkstr && host.ppkstr !== config.certificate) throw Error('Identidade do servidor divergente.');
  host.ppkstr = config.certificate; phase = 'verificar certificado'; await host.refreshServerInfo();
  identity.trustedServerUid = config.serverUid; localStorage.setItem('lg-stream-identity', JSON.stringify(identity));
  emit('paired', { certificate: identity.cert.cert });
  return true;
}
async function disconnect() {
  if (!active && !pendingStart) return;
  await new Promise(function (resolve, reject) {
    var timeout = setTimeout(function () { terminating = null; reject(Error('O vídeo não confirmou a desconexão.')); }, 8000);
    terminating = function () { clearTimeout(timeout); resolve(); };
    sendMessage('stopRequest', []).catch(function () { clearTimeout(timeout); terminating = null; reject(Error('Não foi possível desconectar o vídeo.')); });
  });
}
async function start(profile) {
  phase = "iniciar sessão";
  await disconnect();
  await host.refreshServerInfo();
  var apps = await host.getAppListWithCacheFlush(), desktop = apps.filter(function (a) { return a.title === 'Desktop'; });
  if (desktop.length !== 1) throw Error('Desktop do Sunshine não identificado.');
  var appId = desktop[0].id;
  if (host.currentGame && Number(host.currentGame) !== appId) throw Error('Outra transmissão está em uso. Encerre-a no cliente antes de continuar.');
  var key = generateRemoteInputKey(), keyId = generateRemoteInputKeyId();
  var mode = profile.width + 'x' + profile.height + 'x' + profile.fps;
  var result = Number(host.currentGame) === appId ? await host.resumeApp(mode, 0, key, keyId, 0, 1, 0x030002, 0) : await host.launchApp(appId, mode, 0, key, keyId, 0, 1, 0x030002, 0);
  var xml = $($.parseXML(result.toString())).find('root');
  if (xml.attr('status_code') !== '200') throw Error('O Sunshine recusou a transmissão.');
  var started = new Promise(function (resolve, reject) {
    var timer = setTimeout(function () { pendingStart = null; reject(Error('O vídeo não iniciou. Revise codec e resolução.')); }, 15000);
    pendingStart = { resolve: function () { clearTimeout(timer); resolve(); }, reject: function (error) { clearTimeout(timer); reject(error); } };
  });
  var request = sendMessage('startRequest', [host.address, host.httpPort, String(profile.width), String(profile.height), String(profile.fps), String(profile.bitrate * 1000), key, String(keyId), host.appVersion, host.gfeVersion, xml.find('sessionUrl0').text().trim(), host.serverCodecModeSupport, 1, 0, 0, 0, 0, 0, 'EMSS', 'Stereo', 1, 100, 1, 0, profile.codec, 0, 0, 0, 0, 1]);
  await Promise.all([request, started]);
}
var serial = Promise.resolve();
window.addEventListener('message', function (event) {
  if (event.source !== parent || !event.data || event.data.lazyStream !== true) return;
  var m = event.data;
  serial = serial.catch(function () {}).then(async function () {
    try {
      if (m.type === 'setup') await setup(m.value);
      else if (m.type === 'start') await start(m.value);
      else if (m.type === 'stop') await disconnect();
      else throw Error('Ação desconhecida.');
      emit('reply', { id: m.id, ok: true });
    } catch (_) { emit('reply', { id: m.id, ok: false, error: 'Falha ao ' + phase + '. Tente reconectar a transmissão.' }); }
  });
});
document.addEventListener('keydown', function (e) {
  if (e.keyCode === 10009 || e.keyCode === 27) { e.preventDefault(); e.stopImmediatePropagation(); if (!e.repeat) emit('menu'); }
}, true);
initialized = new Promise(function (resolve, reject) {
  Module = { onRuntimeInitialized: resolve, onAbort: function () { reject(Error('Motor de vídeo indisponível.')); emit('error', 'Motor de vídeo indisponível.'); }, print: function () {}, printErr: function () {} };
  var script = document.createElement('script'); script.src = 'moonlight-wasm.js'; script.onerror = reject; document.body.appendChild(script);
});
initialized.then(function () { emit('ready'); });
