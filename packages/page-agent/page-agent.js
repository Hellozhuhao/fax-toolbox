(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ToolboxPageAgent = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const PROTOCOL = 1;
  const MAX_MESSAGE_LENGTH = 16384;

  function resolveHostTransport(hostWindow) {
    if (hostWindow && hostWindow.chrome && hostWindow.chrome.webview &&
        typeof hostWindow.chrome.webview.postMessage === 'function') {
      return { postMessage: value => hostWindow.chrome.webview.postMessage(value) };
    }
    if (hostWindow && hostWindow.ToolboxAndroid &&
        typeof hostWindow.ToolboxAndroid.postMessage === 'function') {
      return { postMessage: value => hostWindow.ToolboxAndroid.postMessage(value) };
    }
    throw new TypeError('supported WebView host bridge is missing');
  }

  function createAgent(options) {
    if (!options || !options.document || !options.transport ||
        typeof options.transport.postMessage !== 'function') {
      throw new TypeError('document and transport.postMessage are required');
    }
    const doc = options.document;
    const transport = options.transport;
    const sessionId = String(options.sessionId || 'smoke-session');
    const moduleId = String(options.moduleId || 'toolbox.smoke');
    const frameId = String(options.frameId || 'main');
    const generation = Number.isSafeInteger(options.generation) ? options.generation : 1;
    let seq = 0;
    let started = false;
    let stopped = false;
    let observer = null;
    const seen = new WeakSet();
    const endedHandlers = new Map();

    function emit(type, payload) {
      if (stopped) return;
      const message = {
        protocol: PROTOCOL, moduleId, sessionId, frameId, generation,
        seq: ++seq, type, payload: payload || {}
      };
      const encoded = JSON.stringify(message);
      if (encoded.length > MAX_MESSAGE_LENGTH) throw new RangeError('message too large');
      transport.postMessage(encoded);
    }

    function scan() {
      if (stopped) return;
      const videos = doc.querySelectorAll('video');
      for (const video of videos) {
        if (seen.has(video)) continue;
        seen.add(video);
        const id = String(video.id || 'unnamed');
        emit('VIDEO_FOUND', { id });
        const onEnded = function () { emit('VIDEO_ENDED', { id }); };
        video.addEventListener('ended', onEnded);
        endedHandlers.set(video, onEnded);
      }
    }

    function start() {
      if (started) return;
      started = true;
      emit('HELLO', { readyState: String(doc.readyState || 'unknown') });
      if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', scan, { once: true });
      else scan();
      const Observer = options.MutationObserver ||
        (typeof MutationObserver !== 'undefined' ? MutationObserver : null);
      if (Observer) {
        observer = new Observer(scan);
        observer.observe(doc.documentElement || doc, { childList: true, subtree: true });
      }
    }

    function stop() {
      if (stopped) return;
      stopped = true;
      if (observer) observer.disconnect();
      for (const [video, handler] of endedHandlers) video.removeEventListener('ended', handler);
      endedHandlers.clear();
    }

    return { start, stop, scan };
  }

  return { PROTOCOL, createAgent, resolveHostTransport };
});
