(() => {
  'use strict';
  const state = document.getElementById('state');
  const detail = document.getElementById('detail');
  const start = document.getElementById('start');
  const cancel = document.getElementById('cancel');
  let currentState = 'STARTING';

  function send(command) {
    if (!window.chrome?.webview?.postMessage) return;
    window.chrome.webview.postMessage(JSON.stringify({
      v: 1, id: crypto.randomUUID(), command
    }));
  }

  function render(message) {
    if (!message || message.v !== 1 || message.kind !== 'status') return;
    currentState = String(message.state || 'UNKNOWN');
    state.textContent = currentState;
    detail.textContent = String(message.detail || '');
    start.disabled = currentState === 'STARTING' || currentState === 'CREATED';
    cancel.disabled = !['CREATED', 'READY', 'OBSERVING'].includes(currentState);
  }

  start.addEventListener('click', () => send('session.start'));
  cancel.addEventListener('click', () => send('session.cancel'));
  window.chrome?.webview?.addEventListener('message', event => render(event.data));
  render({ v: 1, kind: 'status', state: currentState, detail: '正在连接后端…' });
})();
