/* SHA-256/HMAC local: funciona também no HTTP da LAN, onde SubtleCrypto pode não existir. */
(function (root) {
  'use strict';
  var constants = [], initial = [], candidate = 2;
  function prime(n) { for (var d = 2; d * d <= n; d++) if (n % d === 0) return false; return true; }
  while (constants.length < 64) {
    if (prime(candidate)) {
      if (initial.length < 8) initial.push((Math.sqrt(candidate) % 1 * 4294967296) | 0);
      constants.push((Math.pow(candidate, 1 / 3) % 1 * 4294967296) | 0);
    }
    candidate++;
  }
  function rotate(x, n) { return (x >>> n) | (x << (32 - n)); }
  function utf8(s) { return Array.from(new TextEncoder().encode(s)); }
  function sha(bytes) {
    var data = bytes.slice(), bitLength = data.length * 8, h = initial.slice();
    data.push(128); while (data.length % 64 !== 56) data.push(0);
    for (var p = 7; p >= 0; p--) data.push(p >= 4 ? 0 : (bitLength >>> (p * 8)) & 255);
    for (var offset = 0; offset < data.length; offset += 64) {
      var w = [];
      for (var j = 0; j < 16; j++) w[j] = (data[offset + j * 4] << 24) | (data[offset + j * 4 + 1] << 16) | (data[offset + j * 4 + 2] << 8) | data[offset + j * 4 + 3];
      for (j = 16; j < 64; j++) {
        var x = w[j - 15], y = w[j - 2];
        w[j] = (w[j - 16] + (rotate(x, 7) ^ rotate(x, 18) ^ (x >>> 3)) + w[j - 7] + (rotate(y, 17) ^ rotate(y, 19) ^ (y >>> 10))) | 0;
      }
      var a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], v = h[7];
      for (j = 0; j < 64; j++) {
        var t1 = (v + (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25)) + ((e & f) ^ (~e & g)) + constants[j] + w[j]) | 0;
        var t2 = ((rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
        v = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
      }
      [a, b, c, d, e, f, g, v].forEach(function (n, i) { h[i] = (h[i] + n) | 0; });
    }
    var out = []; h.forEach(function (n) { for (var s = 24; s >= 0; s -= 8) out.push((n >>> s) & 255); }); return out;
  }
  function hex(bytes) { return bytes.map(function (x) { return x.toString(16).padStart(2, '0'); }).join(''); }
  function hmac(secret, message) {
    var key = secret.match(/../g).map(function (x) { return parseInt(x, 16); });
    if (key.length > 64) key = sha(key); while (key.length < 64) key.push(0);
    return hex(sha(key.map(function (n) { return n ^ 92; }).concat(sha(key.map(function (n) { return n ^ 54; }).concat(utf8(message))))));
  }
  function nonce() { var bytes = new Uint8Array(16); crypto.getRandomValues(bytes); return hex(Array.from(bytes)); }
  root.LGCrypto = { hmac: hmac, nonce: nonce, sha: function (text) { return hex(sha(utf8(text))); } };
})(typeof window !== 'undefined' ? window : globalThis);
