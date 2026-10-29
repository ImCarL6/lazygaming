/* Platform effects shared by the Tizen package and the Windows webview.
   No pairing keys, process/window identifiers or commands cross this adapter. */
var LGPlatform = (function () {
  'use strict';
  var tv = {
    networkIp: function () { try { return webapis.network.getIp(); } catch (_) { return ''; } },
    sources: function () {
      return new Promise(function (resolve, reject) {
        if (typeof tizen === 'undefined' || !tizen.tvwindow) return reject(Error('API de entrada HDMI indisponível.'));
        tizen.systeminfo.getPropertyValue('VIDEOSOURCE', function (sources) {
          resolve(sources.connected.filter(function (s) { return s.type === 'HDMI'; }));
        }, reject);
      });
    },
    currentSource: function () { return tizen.tvwindow.getSource('MAIN'); },
    selectSource: function (source) {
      return new Promise(function (resolve, reject) { tizen.tvwindow.setSource(source, resolve, reject, 'MAIN'); });
    },
    openViewer: function () {
      return new Promise(function (resolve, reject) {
        var timer = setTimeout(function () { reject(Error('A TV não confirmou a abertura da entrada HDMI.')); }, 5000);
        try { tizen.application.launch('org.tizen.tv-viewer', function () { clearTimeout(timer); resolve(); }, function (e) { clearTimeout(timer); reject(e); }); }
        catch (e) { clearTimeout(timer); reject(e); }
      });
    },
    appInstalled: function (id) { try { return tizen.application.getAppInfo(id).id === id; } catch (_) { return false; } },
    exit: function () { tizen.application.getCurrentApplication().exit(); },
    streamLimits: function () {
      try { if (webapis.productinfo.isUdPanelSupported()) return { width: 3840, height: 2160 }; } catch (_) { }
      return { width: 1920, height: 1080 };
    },
    background: function (root) { return new TVLiveBackground(tizen, root); }
  };
  var pc = {
    background: function (root, image, client) { return new DesktopLiveBackground(root, image, client); }
  };
  return { tv: tv, desktop: pc };
}());
