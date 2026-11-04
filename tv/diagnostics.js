/* Falhas da interface: fila local pequena, envio autenticado ao próprio auxiliar. */
var LGDiagnostics = (function () {
  'use strict';
  var key = 'lazygaming-diagnostic-queue', queue = [], seen = {}, sending = false;
  function clean(text) {
    return String(text || '').slice(0, 4000)
      .replace(/https?:\/\/[^\s)"<>]+/gi, '[endereço]')
      .replace(/(?:[A-Z]:[\\/]|\\\\)[^\r\n"<>|]*/gi, '[caminho local]')
      .replace(/\b(?:authorization|bearer|access[_-]?token|token|password|senha|secret|cookie|invite|nonce|signature|key)\b["'\s]*[:= ]+["']?[^\s,;"'}]+/gi, '[credencial omitida]')
      .replace(/\b[a-f0-9]{32,}\b/gi, '[identificador omitido]');
  }
  function save() { try { localStorage.setItem(key, JSON.stringify(queue)); } catch (_) {} }
  try { var saved = JSON.parse(localStorage.getItem(key) || '[]'); if (Array.isArray(saved)) queue = saved.slice(-10).map(function (x) { return { code: clean(x.code), message: clean(x.message), stack: clean(x.stack), file: clean(x.file), line: clean(x.line) }; }); } catch (_) {}
  function report(code, error, file, line) {
    var message = clean(error && error.message || error), stack = clean(error && error.stack);
    var signature = code + ':' + message, now = Date.now();
    if (seen[signature] && now - seen[signature] < 60000) return;
    if (Object.keys(seen).length > 40) seen = {};
    seen[signature] = now;
    var filename = String(file || '').split(/[?#]/)[0].split(/[\\/]/).pop();
    queue.push({ code: clean(code), message: message, stack: stack, file: /^[A-Za-z0-9_.-]+\.(js|html|css)$/i.test(filename) ? filename.slice(0,80) : '', line: clean(line) });
    if (queue.length > 10) queue.shift(); save(); flush();
  }
  async function flush() {
    if (sending || !queue.length || typeof api !== 'function' || (typeof connected !== 'undefined' && !connected)) return;
    sending = true;
    try {
      while (queue.length) {
        var item = queue[0];
        var result = await api('/api/diagnostics/report', Object.assign({}, item, { code: item.code.slice(0,80), message: item.message.slice(0,500), stack: item.stack.slice(0,900), file: item.file.slice(0,150), line: item.line.slice(0,12), source: typeof tizen !== 'undefined' ? 'tv-ui' : 'pc-ui',
          viewport: typeof innerWidth === 'number' ? innerWidth + 'x' + innerHeight + '@' + (window.devicePixelRatio || 1) : '',
          browser: typeof navigator !== 'undefined' ? clean(navigator.userAgent).slice(0,180) : '' }));
        if (!result || !result.saved) break;
        if (queue[0] === item) queue.shift(); save();
      }
    } catch (_) { /* Offline mantém a fila, sem gerar erro sobre o próprio diagnóstico. */ }
    finally { sending = false; }
  }
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('error', function (event) {
      if (!event.error && typeof tizen === 'undefined' && String(event.target && event.target.src || '').indexOf('$WEBAPIS') >= 0) return;
      report(event.error ? 'javascript-error' : 'resource-error', event.error || event.message || 'Recurso da interface não carregou', event.filename || event.target && event.target.src, event.lineno);
    }, true);
    window.addEventListener('unhandledrejection', function (event) { report('unhandled-rejection', event.reason); });
  }
  setInterval(flush, 15000);
  return { report: report, flush: flush };
})();
