/* Settings are bounded by the source and the decoder, never the Wi-Fi link rate. */
(function (root) {
  'use strict';
  function profile(source, settings, limits) {
    settings = settings || {}; limits = limits || { width: 3840, height: 2160 };
    if (!source || !(source.width > 0 && source.height > 0)) throw Error('Tela de captura não identificada.');
    var requested = settings.resolution && settings.resolution !== 'auto' ? settings.resolution.split('x').map(Number) : [source.width, source.height];
    if (requested.length !== 2 || requested.some(function (n) { return !isFinite(n) || n < 240; })) throw Error('Resolução inválida.');
    var scale = Math.min(1, source.width / requested[0], source.height / requested[1], limits.width / requested[0], limits.height / requested[1]);
    var width = Math.floor(requested[0] * scale / 2) * 2, height = Math.floor(requested[1] * scale / 2) * 2;
    var fps = settings.fps === 30 ? 30 : 60;
    var bitrate = Number(settings.bitrate) || (height > 1440 ? 25 : height > 1080 ? 20 : 12);
    if (!isFinite(bitrate) || bitrate < 5 || bitrate > 80) throw Error('Use bitrate entre 5 e 80 Mbps.');
    return { width: width, height: height, fps: fps, bitrate: bitrate, codec: settings.codec === 'H264' ? 'H264' : 'HEVC' };
  }
  function adaptive(settings) { return settings.adaptive !== false && !Number(settings.bitrate); }
  function reduce(current) {
    var next = Object.assign({}, current);
    if (next.bitrate > 20) next.bitrate = 20;
    else if (next.bitrate > 15) next.bitrate = 15;
    else if (next.bitrate > 10) next.bitrate = 10;
    else if (next.height > 1080) {
      var height = next.height > 1440 ? 1440 : 1080;
      next.width = Math.floor(next.width * height / next.height / 2) * 2;
      next.height = height; next.bitrate = height > 1080 ? 15 : 10;
    } else if (next.bitrate > 6) next.bitrate = 6;
    else if (next.fps > 30) next.fps = 30;
    else return null;
    return next;
  }
  function Guard() { this.reset(0); }
  Guard.prototype.reset = function (now) { this.since = null; this.last = now; };
  Guard.prototype.warning = function (text, now) {
    if (/slow connection|poor connection|reduce.*bitrate/i.test(text)) { if (this.since === null) this.since = now; }
    else this.since = null;
  };
  Guard.prototype.next = function (current, now) {
    if (this.since === null || now - this.since < 6000 || now - this.last < 30000) return null;
    var next = reduce(current); this.reset(now); return next;
  };
  root.LGStreamPolicy = { profile: profile, adaptive: adaptive, reduce: reduce, Guard: Guard };
}(typeof window === 'undefined' ? globalThis : window));
