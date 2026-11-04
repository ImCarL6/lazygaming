/* Avisos transitórios compartilhados pelo launcher PC/TV e Central. Estados e instruções ficam nos próprios painéis. */
var LGNotices = (function () {
  'use strict';
  var entries = new Map(), states = new Map(), duration = 2800;
  function clear(element) {
    var entry = entries.get(element);
    if (entry) clearTimeout(entry.timer);
    entries.delete(element); element.textContent = ''; element.hidden = true;
  }
  function show(element, text) {
    if (!element) return;
    text = text || '';
    var previous = entries.get(element);
    // Polling não renova o prazo de uma mensagem que já está visível.
    if (text && previous && previous.text === text) return;
    clear(element);
    if (!text) return;
    element.textContent = text; element.hidden = false;
    var entry = { text: text };
    entries.set(element, entry);
    entry.timer = setTimeout(function () { if (entries.get(element) === entry) clear(element); }, duration);
  }
  function changed(channel, text, element) {
    text = text || '';
    var previous = states.get(channel) || '';
    if (previous === text) return;
    states.set(channel, text);
    if (text) show(element, text);
    else if (element && element.textContent === previous) clear(element);
  }
  function clearAll() { Array.from(entries.keys()).forEach(clear); }
  // Timers de páginas ocultas podem ser suspensos. Limpar também na transição de visibilidade.
  if (typeof document !== 'undefined') document.addEventListener('visibilitychange', clearAll);
  return { show: show, changed: changed, clearAll: clearAll };
})();
