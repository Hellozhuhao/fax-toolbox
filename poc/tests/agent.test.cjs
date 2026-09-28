const test = require('node:test');
const assert = require('node:assert/strict');
const { createAgent, resolveHostTransport, PROTOCOL } = require('../shared/page-agent.js');

function fakeVideo(id) {
  const handlers = new Map();
  return {
    id,
    addEventListener(type, handler) { handlers.set(type, handler); },
    removeEventListener(type) { handlers.delete(type); },
    fire(type) { handlers.get(type)?.(); }
  };
}

test('one page agent works with two host transport implementations', () => {
  for (const host of ['windows', 'android']) {
    const video = fakeVideo('v1');
    const messages = [];
    const doc = {
      readyState: 'complete', documentElement: {},
      querySelectorAll(selector) { assert.equal(selector, 'video'); return [video]; }
    };
    const transport = { postMessage(json) { messages.push(JSON.parse(json)); } };
    const agent = createAgent({ document: doc, transport, sessionId: host, generation: 3 });
    agent.start();
    agent.scan();
    video.fire('ended');
    assert.deepEqual(messages.map(m => m.type), ['HELLO', 'VIDEO_FOUND', 'VIDEO_ENDED']);
    assert.deepEqual(messages.map(m => m.seq), [1, 2, 3]);
    assert(messages.every(m => m.protocol === PROTOCOL && m.sessionId === host && m.generation === 3));
    agent.stop();
    video.fire('ended');
    assert.equal(messages.length, 3);
  }
});

test('agent rejects missing host bridge', () => {
  assert.throws(() => createAgent({ document: {} }), /transport.postMessage/);
  assert.throws(() => resolveHostTransport({}), /host bridge is missing/);
});

test('platform bridge resolution uses the expected WebView entry point', () => {
  for (const name of ['windows', 'android']) {
    const received = [];
    const host = name === 'windows'
      ? { chrome: { webview: { postMessage(value) { received.push(value); } } } }
      : { ToolboxAndroid: { postMessage(value) { received.push(value); } } };
    resolveHostTransport(host).postMessage('probe');
    assert.deepEqual(received, ['probe']);
  }
});
