(() => {
  'use strict';
  let channel = '', initialized = false, currentState = null;
  const subscribers = new Set(), pending = new Map();
  const post = payload => parent.postMessage({channel, ...payload}, '*');
  addEventListener('message', event => {
    if (event.source !== parent || !event.data || typeof event.data !== 'object') return;
    const message = event.data;
    if (message.type === 'skin.init' && !initialized) {
      initialized = true; channel = message.channel; currentState = message.state;
      const style = document.createElement('style'); style.textContent = message.css || ''; document.head.append(style);
      document.body.innerHTML = message.html;
      window.ToolboxSkin = Object.freeze({
        version: 1,
        getState: () => structuredClone(currentState),
        onState: callback => { subscribers.add(callback); callback(structuredClone(currentState)); return () => subscribers.delete(callback); },
        invoke: (action, args = {}) => new Promise((resolve, reject) => {
          if (pending.size >= 16) { reject(new Error('待处理操作过多')); return; }
          const id = crypto.randomUUID();
          const timeout = setTimeout(() => { pending.delete(id); reject(new Error('操作超时')); }, 5000);
          pending.set(id, {resolve, reject, timeout}); post({type: 'skin.invoke', id, action, args});
        })
      });
      const source = new Blob([message.script + '\n//# sourceURL=fax-skin-extension.js'], {type: 'text/javascript'});
      const url = URL.createObjectURL(source), script = document.createElement('script');
      script.src = url; script.onload = () => { URL.revokeObjectURL(url); post({type: 'skin.ready'}); };
      script.onerror = () => { URL.revokeObjectURL(url); post({type: 'skin.error', message: '扩展脚本加载失败'}); };
      document.head.append(script);
    } else if (message.channel === channel && message.type === 'skin.state') {
      currentState = message.state;
      for (const subscriber of subscribers) try { subscriber(structuredClone(currentState)); } catch (error) { post({type: 'skin.error', message: String(error.message)}); }
    } else if (message.channel === channel && message.type === 'skin.result') {
      const request = pending.get(message.id); if (!request) return;
      clearTimeout(request.timeout); pending.delete(message.id);
      if (message.error) request.reject(new Error(message.error)); else request.resolve(message.result);
    }
  });
  addEventListener('error', event => post({type: 'skin.error', message: event.message}));
  addEventListener('unhandledrejection', event => post({type: 'skin.error', message: String(event.reason?.message || event.reason)}));
})();
