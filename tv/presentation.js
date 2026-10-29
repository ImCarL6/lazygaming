/* HDMI é composto pela própria TV; nenhum frame passa pelo PC/rede.
   Nunca seleciona outra entrada ao abrir o menu. */
function TVLiveBackground(tv, root) {
  var shown = false, pending = false, desired = false, failed = false, foreground = true, retryAt = 0;
  function opaque() { root.classList.remove('live-game'); }
  function hide() {
    opaque();
    if (!shown) return;
    shown = false;
    try { tv.tvwindow.hide(function () {}, function () {}, 'MAIN'); } catch (_) { }
  }
  this.suspend = function () { foreground = false; desired = false; shown = false; opaque(); };
  this.update = function (running, enabled, visible, sourceNumber) {
    foreground = visible;
    // O viewer HDMI pode estar assumindo a tela. App oculto não manipula MAIN.
    if (!foreground) { desired = false; shown = false; opaque(); return; }
    desired = !!(running && enabled && visible && sourceNumber && tv && tv.tvwindow);
    if (!desired) { failed = false; hide(); return; }
    try {
      var source = tv.tvwindow.getSource('MAIN');
      if (source.type !== 'HDMI' || source.number !== Number(sourceNumber)) {
        hide();
        if (pending || Date.now() < retryAt || !tv.systeminfo) return;
        pending = true; retryAt = Date.now() + 2000;
        tv.systeminfo.getPropertyValue('VIDEOSOURCE', function (sources) {
          var selected = sources.connected.filter(function (s) { return s.type === 'HDMI' && s.number === Number(sourceNumber); })[0];
          if (!selected || !foreground || !desired) { pending = false; return; }
          tv.tvwindow.setSource(selected, function () { pending = false; failed = false; }, function () { pending = false; }, 'MAIN');
        }, function () { pending = false; });
        return;
      }
      if (failed && Date.now() >= retryAt) failed = false;
      if (shown || pending || failed) return;
      pending = true;
      tv.tvwindow.show(function () {
        pending = false; shown = true;
        if (!foreground) { shown = false; opaque(); }
        else if (desired) root.classList.add('live-game'); else hide();
      }, function () { pending = false; failed = true; retryAt = Date.now() + 2000; opaque(); }, ['0%', '0%', '100%', '100%'], 'MAIN', 'BEHIND');
    } catch (_) { pending = false; failed = true; retryAt = Date.now() + 2000; opaque(); }
  };
}

function DesktopLiveBackground(root, image, client) {
  // No host nativo, o DWM compõe a superfície original atrás do WebView.
  // Confirmação vem do host, que resolve somente a janela verificada do jogo.
  if (typeof window !== 'undefined' && window.LGNativeDesktop) {
    var desired = false;
    window.chrome.webview.addEventListener('message', function (event) {
      if (!event.data || event.data.type !== 'background') return;
      if (desired && event.data.active) root.classList.add('live-game');
      else root.classList.remove('live-game');
    });
    this.update = function (running, enabled, visible) {
      desired = !!(running && enabled && visible);
      image.hidden = true;
      if (!desired) root.classList.remove('live-game');
      window.LGNativeDesktop.background(desired);
    };
    return;
  }
  var active = false, generation = 0, controller, currentUrl;
  this.update = function (running, enabled, visible) {
    var wanted = !!(running && enabled && visible);
    if (wanted === active) return;
    active = wanted; var ticket = ++generation;
    if (controller) controller.abort();
    root.classList.remove('live-game'); image.hidden = true;
    if (currentUrl) { URL.revokeObjectURL(currentUrl); currentUrl = null; }
    if (wanted) loop(ticket);
  };
  async function loop(ticket) {
    while (active && ticket === generation) {
      var frameStarted = Date.now(), retry = false;
      controller = new AbortController();
      var timer = setTimeout(function () { controller.abort(); }, 4000), next;
      try {
        var response = await fetch('/api/launcher/background?client=' + encodeURIComponent(client), { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
        if (response.status === 200) {
          next = URL.createObjectURL(await response.blob());
          if (ticket !== generation) { URL.revokeObjectURL(next); break; }
          await new Promise(function (resolve, reject) { image.onload = resolve; image.onerror = reject; image.src = next; });
          if (ticket !== generation) { URL.revokeObjectURL(next); break; }
          if (currentUrl) URL.revokeObjectURL(currentUrl); currentUrl = next; next = null;
          image.hidden = false; root.classList.add('live-game');
        } else { root.classList.remove('live-game'); image.hidden = true; retry = true; }
      } catch (_) {
        retry = true;
        if (next) URL.revokeObjectURL(next);
        if (ticket === generation) { root.classList.remove('live-game'); image.hidden = true; }
      } finally { clearTimeout(timer); }
      // Alvo de 24fps incluindo captura/decodificação, sem somar pausa fixa por frame.
      var pause = retry ? 200 : Math.max(0, 42 - (Date.now() - frameStarted));
      await new Promise(function (resolve) { setTimeout(resolve, pause); });
    }
  }
}
