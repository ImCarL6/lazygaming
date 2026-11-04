/* Ilustrações originais e estilizadas de controles (sem logotipos), usadas na Central do PC
   para mostrar onde fica o botão do atalho e o gesto (toque duplo / segurar). */
(function () {
  'use strict';
  var NS = 'http://www.w3.org/2000/svg';
  var reduce = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  function node(tag, attrs, parent) {
    var item = document.createElementNS(NS, tag);
    for (var key in attrs) item.setAttribute(key, attrs[key]);
    if (parent) parent.appendChild(item);
    return item;
  }
  // Máscaras de PadButtons (Core/ControllerContracts.cs).
  var SOUTH = 1, EAST = 2, WEST = 4, NORTH = 8, L1 = 16, R1 = 32, SELECT = 64, START = 128, L3 = 256, R3 = 512, L2 = 1024, R2 = 2048, MIC = 4096, PAD = 8192;
  var bodies = {
    dualsense: 'M70 40Q180 24 290 40Q322 46 330 90L350 175Q356 210 325 214Q300 216 285 190L255 152Q180 162 105 152L75 190Q60 216 35 214Q4 210 10 175L30 90Q38 46 70 40Z',
    xbox: 'M78 46Q180 32 282 46Q322 53 333 100L347 168Q353 207 322 211Q298 213 281 186L253 154Q180 164 107 154L79 186Q62 213 38 211Q7 207 13 168L27 100Q38 53 78 46Z',
    switch: 'M74 44Q180 34 286 44Q324 50 334 96L348 170Q353 207 323 211Q299 213 282 188L254 156Q180 164 106 156L78 188Q61 213 37 211Q7 207 12 170L26 96Q36 50 74 44Z'
  };
  // Cada botão: forma, posição, rótulo/símbolo e máscara.
  function layout(family) {
    var face = family === 'dualsense' ? { n: 'tri', e: 'circ', s: 'cross', w: 'sq' } : family === 'switch' ? { n: 'X', e: 'A', s: 'B', w: 'Y' } : { n: 'Y', e: 'B', s: 'A', w: 'X' };
    var fx = family === 'dualsense' ? 275 : 272, fy = 92;
    var items = [
      { mask: NORTH, shape: 'circle', cx: fx, cy: fy - 22, r: 11, glyph: face.n },
      { mask: EAST, shape: 'circle', cx: fx + 22, cy: fy, r: 11, glyph: face.e },
      { mask: SOUTH, shape: 'circle', cx: fx, cy: fy + 22, r: 11, glyph: face.s },
      { mask: WEST, shape: 'circle', cx: fx - 22, cy: fy, r: 11, glyph: face.w }
    ];
    if (family === 'dualsense') items.push(
      { mask: L2, shape: 'rect', x: 62, y: 10, w: 58, h: 16, rx: 8, label: 'L2', back: true }, { mask: R2, shape: 'rect', x: 240, y: 10, w: 58, h: 16, rx: 8, label: 'R2', back: true },
      { mask: L1, shape: 'rect', x: 56, y: 27, w: 70, h: 11, rx: 5.5, label: 'L1' }, { mask: R1, shape: 'rect', x: 234, y: 27, w: 70, h: 11, rx: 5.5, label: 'R1' },
      { mask: PAD, shape: 'rect', x: 128, y: 42, w: 104, h: 56, rx: 11, label: '' },
      { mask: SELECT, shape: 'rect', x: 108, y: 46, w: 9, h: 17, rx: 4.5, label: '' }, { mask: START, shape: 'rect', x: 243, y: 46, w: 9, h: 17, rx: 4.5, label: '' },
      { mask: 0, shape: 'circle', cx: 180, cy: 110, r: 7, label: '' },
      { mask: MIC, shape: 'rect', x: 169, y: 124, w: 22, h: 7, rx: 3.5, label: '' },
      { mask: L3, shape: 'circle', cx: 135, cy: 140, r: 18, stick: true }, { mask: R3, shape: 'circle', cx: 225, cy: 140, r: 18, stick: true },
      { dpad: true, cx: 85, cy: 92 });
    else {
      var nintendo = family === 'switch';
      items.push(
        { mask: L2, shape: 'rect', x: 62, y: 15, w: 56, h: 16, rx: 8, label: nintendo ? 'ZL' : 'LT', back: true }, { mask: R2, shape: 'rect', x: 242, y: 15, w: 56, h: 16, rx: 8, label: nintendo ? 'ZR' : 'RT', back: true },
        { mask: L1, shape: 'rect', x: 55, y: 32, w: 76, h: 12, rx: 6, label: nintendo ? 'L' : 'LB' }, { mask: R1, shape: 'rect', x: 229, y: 32, w: 76, h: 12, rx: 6, label: nintendo ? 'R' : 'RB' },
        { mask: L3, shape: 'circle', cx: 92, cy: 90, r: 18, stick: true }, { mask: R3, shape: 'circle', cx: 220, cy: 140, r: 18, stick: true },
        { dpad: true, cx: 140, cy: 140 });
      if (nintendo) items.push(
        { mask: SELECT, shape: 'circle', cx: 150, cy: 72, r: 7, glyph: '−', small: true }, { mask: START, shape: 'circle', cx: 210, cy: 72, r: 7, glyph: '+', small: true },
        { mask: 0, shape: 'rect', x: 151, y: 96, w: 12, h: 12, rx: 3, label: '' }, { mask: 0, shape: 'circle', cx: 207, cy: 102, r: 7, label: '' });
      else items.push(
        { mask: SELECT, shape: 'circle', cx: 152, cy: 92, r: 7, glyph: 'view', small: true }, { mask: START, shape: 'circle', cx: 208, cy: 92, r: 7, glyph: 'menu', small: true },
        { mask: 0, shape: 'circle', cx: 180, cy: 64, r: 11, label: '', ring: true });
    }
    return items;
  }
  function glyph(item, parent) {
    var g = node('g', { class: 'glyph' }, parent), x = item.cx, y = item.cy;
    if (item.glyph === 'tri') node('path', { d: 'M' + x + ' ' + (y - 5.2) + 'l5 8.6h-10z' }, g);
    else if (item.glyph === 'circ') node('circle', { cx: x, cy: y, r: 4.6 }, g);
    else if (item.glyph === 'cross') node('path', { d: 'M' + (x - 4) + ' ' + (y - 4) + 'l8 8M' + (x + 4) + ' ' + (y - 4) + 'l-8 8' }, g);
    else if (item.glyph === 'sq') node('rect', { x: x - 4, y: y - 4, width: 8, height: 8, rx: 1 }, g);
    else if (item.glyph === 'view') { node('rect', { x: x - 3.5, y: y - 2.5, width: 5, height: 4, rx: 1 }, g); node('rect', { x: x - 1.5, y: y - .5, width: 5, height: 4, rx: 1 }, g); }
    else if (item.glyph === 'menu') node('path', { d: 'M' + (x - 3.5) + ' ' + (y - 2.5) + 'h7M' + (x - 3.5) + ' ' + y + 'h7M' + (x - 3.5) + ' ' + (y + 2.5) + 'h7' }, g);
    else { var text = node('text', { x: x, y: y + (item.small ? 3.4 : 4.4), 'text-anchor': 'middle' }, g); text.textContent = item.glyph; g.setAttribute('class', 'glyph letter' + (item.small ? ' small' : '')); }
  }
  function shapeOf(item, parent, grow, cls) {
    var g = grow || 0;
    if (item.shape === 'circle') return node('circle', { cx: item.cx, cy: item.cy, r: item.r + g, class: cls }, parent);
    return node('rect', { x: item.x - g, y: item.y - g, width: item.w + 2 * g, height: item.h + 2 * g, rx: item.rx + g, class: cls }, parent);
  }
  function center(item) { return item.shape === 'circle' ? [item.cx, item.cy] : [item.x + item.w / 2, item.y + item.h / 2]; }
  var animations = new WeakMap();
  function stop(host) { (animations.get(host) || []).forEach(function (a) { try { a.cancel(); } catch (_) { } }); animations.set(host, []); }
  function play(host, element, frames, options) { if (reduce || !element.animate) return; var a = element.animate(frames, options); animations.get(host).push(a); }
  function family(kind, name) {
    if (kind === 'dualsense') return 'dualsense';
    if (kind === 'switch-pro') return 'switch';
    if (kind === 'xinput') return 'xbox';
    return 'generic';
  }
  // opts: { family, buttons, gesture: 'hold'|'double-tap', holdMs, waiting, listening }
  function render(host, opts) {
    var kind = opts.family === 'generic' ? 'xbox' : opts.family;
    var signature = JSON.stringify([kind, opts.family, opts.buttons, opts.gesture, opts.holdMs, !!opts.waiting, !!opts.listening]);
    if (host.dataset.signature === signature) return; host.dataset.signature = signature;
    stop(host); host.textContent = '';
    host.classList.add('pad-art'); host.classList.toggle('waiting', !!opts.waiting); host.classList.toggle('listening', !!opts.listening);
    var svg = node('svg', { viewBox: '0 0 360 228', role: 'img', 'aria-hidden': 'true' }, host);
    var items = layout(kind), hot = [];
    items.filter(function (i) { return i.back; }).forEach(function (i) { var s = shapeOf(i, svg, 0, 'btn back' + (i.mask & opts.buttons ? ' hot' : '')); if (i.mask & opts.buttons) hot.push([i, s]); });
    node('path', { d: bodies[kind], class: 'body' }, svg);
    items.forEach(function (item) {
      if (item.back) return;
      if (item.dpad) {
        var d = node('g', { class: 'dpad' }, svg), x = item.cx, y = item.cy;
        node('path', { d: 'M' + (x - 7) + ' ' + (y - 21) + 'h14v14h14v14h-14v14h-14v-14h-14v-14h14z' }, d);
        return;
      }
      var hit = item.mask && (item.mask & opts.buttons);
      var group = node('g', { class: 'btn' + (item.stick ? ' stick' : '') + (item.ring ? ' home' : '') + (hit ? ' hot' : '') + (item.mask ? '' : ' inert') }, svg);
      var shape = shapeOf(item, group, 0, 'cap');
      if (item.stick) node('circle', { cx: item.cx, cy: item.cy, r: item.r - 6, class: 'stick-top' }, group);
      if (item.glyph) glyph(item, group);
      if (item.label) { var c = center(item), t = node('text', { x: c[0], y: c[1] + 3.6, 'text-anchor': 'middle', class: 'label' }, group); t.textContent = item.label; }
      if (hit) hot.push([item, group]);
    });
    hot.forEach(function (pair) {
      var item = pair[0], element = pair[1], c = center(item);
      element.style.transformOrigin = c[0] + 'px ' + c[1] + 'px';
      var halo = shapeOf(item, svg, 7, 'halo');
      if (opts.waiting || opts.listening) return;
      if (opts.gesture === 'double-tap') {
        play(host, element, [{ transform: 'scale(1)' }, { transform: 'scale(.82)', offset: .08 }, { transform: 'scale(1)', offset: .17 }, { transform: 'scale(.82)', offset: .26 }, { transform: 'scale(1)', offset: .36 }, { transform: 'scale(1)' }], { duration: 1700, iterations: Infinity, easing: 'ease-out' });
        [0, .18].forEach(function (offset) {
          var ripple = shapeOf(item, svg, 4, 'ripple'); ripple.style.transformOrigin = c[0] + 'px ' + c[1] + 'px';
          play(host, ripple, [{ opacity: 0, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(1)', offset: offset + .06 }, { opacity: .9, transform: 'scale(1)', offset: offset + .07 }, { opacity: 0, transform: 'scale(1.9)', offset: Math.min(1, offset + .42) }, { opacity: 0, transform: 'scale(1.9)' }], { duration: 1700, iterations: Infinity, easing: 'ease-out' });
        });
      } else {
        var hold = Math.max(500, opts.holdMs || 1000), total = hold + 1100, fill = hold / total;
        var ring = shapeOf(item, svg, 7, 'hold-ring'); ring.setAttribute('pathLength', '100');
        halo.style.opacity = '.25';
        play(host, ring, [{ strokeDashoffset: 100, opacity: 1 }, { strokeDashoffset: 0, opacity: 1, offset: fill }, { strokeDashoffset: 0, opacity: 0, offset: Math.min(1, fill + .25) }, { strokeDashoffset: 0, opacity: 0 }], { duration: total, iterations: Infinity, easing: 'linear' });
        play(host, element, [{ transform: 'scale(1)' }, { transform: 'scale(.88)', offset: .05 }, { transform: 'scale(.88)', offset: fill }, { transform: 'scale(1.08)', offset: Math.min(1, fill + .05) }, { transform: 'scale(1)', offset: Math.min(1, fill + .14) }, { transform: 'scale(1)' }], { duration: total, iterations: Infinity });
        var burst = shapeOf(item, svg, 9, 'burst'); burst.style.transformOrigin = c[0] + 'px ' + c[1] + 'px';
        play(host, burst, [{ opacity: 0, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(1)', offset: fill }, { opacity: 1, transform: 'scale(1)', offset: Math.min(1, fill + .01) }, { opacity: 0, transform: 'scale(1.7)', offset: Math.min(1, fill + .22) }, { opacity: 0, transform: 'scale(1.7)' }], { duration: total, iterations: Infinity, easing: 'ease-out' });
      }
    });
    if (opts.family === 'generic' && !reduce && svg.animate) play(host, svg, [{ opacity: .42 }, { opacity: .7 }, { opacity: .42 }], { duration: 2600, iterations: Infinity, easing: 'ease-in-out' });
  }
  // Demonstração do mouse pelo touchpad: dedo desliza e o cursor acompanha numa telinha.
  function touchpadDemo(host) {
    if (host.dataset.ready) return; host.dataset.ready = '1'; animations.set(host, []);
    host.classList.add('touch-demo');
    var svg = node('svg', { viewBox: '0 0 300 110', 'aria-hidden': 'true' }, host);
    node('rect', { x: 6, y: 10, width: 136, height: 90, rx: 16, class: 'pad-surface' }, svg);
    node('path', { d: 'M150 55h22m-6-6 6 6-6 6', class: 'arrow' }, svg);
    node('rect', { x: 182, y: 12, width: 112, height: 72, rx: 7, class: 'screen' }, svg);
    node('rect', { x: 226, y: 84, width: 24, height: 12, class: 'stand' }, svg);
    var trail = node('circle', { cx: 0, cy: 0, r: 13, class: 'finger-glow' }, svg), finger = node('circle', { cx: 0, cy: 0, r: 8, class: 'finger' }, svg);
    var cursor = node('path', { d: 'M0 0v13l3.5-3.2 2.6 5.6 2.4-1.1-2.6-5.4h4.7z', class: 'cursor' }, svg);
    var click = node('circle', { cx: 0, cy: 0, r: 10, class: 'tap' }, svg);
    var frames = 36, pad = [], screen = [], ripple = [];
    for (var i = 0; i <= frames; i++) {
      var t = i / frames * Math.PI * 2, x = Math.sin(t), y = Math.sin(2 * t) * .55;
      pad.push({ transform: 'translate(' + (74 + x * 44) + 'px,' + (55 + y * 42) + 'px)' });
      screen.push({ transform: 'translate(' + (238 + x * 38) + 'px,' + (44 + y * 30) + 'px)' });
    }
    [trail, finger].forEach(function (f) { play(host, f, pad, { duration: 4200, iterations: Infinity }); });
    play(host, cursor, screen, { duration: 4200, iterations: Infinity });
    // Um “clique” pressionando o touchpad no ponto mais à direita do trajeto.
    ripple = [{ opacity: 0, transform: 'translate(118px,55px) scale(.6)' }, { opacity: 0, transform: 'translate(118px,55px) scale(.6)', offset: .24 }, { opacity: .9, transform: 'translate(118px,55px) scale(.8)', offset: .25 }, { opacity: 0, transform: 'translate(118px,55px) scale(2)', offset: .4 }, { opacity: 0, transform: 'translate(118px,55px) scale(2)' }];
    play(host, click, ripple, { duration: 4200, iterations: Infinity, easing: 'ease-out' });
    if (reduce) { finger.setAttribute('transform', 'translate(74 55)'); trail.setAttribute('transform', 'translate(74 55)'); cursor.setAttribute('transform', 'translate(238 44)'); }
  }
  window.LGControllerArt = { render: render, family: family, touchpadDemo: touchpadDemo };
}());
