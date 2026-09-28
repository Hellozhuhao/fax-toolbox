const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createAgent, resolveHostTransport } = require('../packages/page-agent/page-agent.js');

const root = path.resolve(__dirname, '..');

test('module manifest points to an existing page and declares both platforms', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'modules/smoke/module.json'), 'utf8'));
  assert.equal(manifest.id, 'toolbox.smoke');
  assert.equal(manifest.apiVersion, 1);
  assert.deepEqual(manifest.platforms, ['windows', 'android']);
  assert(fs.existsSync(path.join(root, 'modules/smoke', manifest.entry)));
});

test('one page agent emits the same ordered events for both hosts', () => {
  for (const platform of ['windows', 'android']) {
    const listeners = new Map();
    const video = {
      id: 'sample-video',
      addEventListener(type, listener) { listeners.set(type, listener); },
      removeEventListener(type) { listeners.delete(type); }
    };
    const doc = {
      readyState: 'complete', documentElement: {},
      querySelectorAll() { return [video]; }
    };
    const received = [];
    const postMessage = value => received.push(JSON.parse(value));
    const host = platform === 'windows'
      ? { chrome: { webview: { postMessage } } }
      : { ToolboxAndroid: { postMessage } };
    const agent = createAgent({
      document: doc, transport: resolveHostTransport(host),
      moduleId: 'toolbox.smoke', sessionId: 'starter', generation: 1
    });
    agent.start();
    listeners.get('ended')();
    assert.deepEqual(received.map(event => event.type), ['HELLO', 'VIDEO_FOUND', 'VIDEO_ENDED']);
    assert.deepEqual(received.map(event => event.seq), [1, 2, 3]);
    assert(received.every(event => event.protocol === 1 && event.moduleId === 'toolbox.smoke'));
    agent.stop();
  }
});
