const test = require('node:test');
const assert = require('node:assert/strict');
const renderer = require('../apps/windows-shell/ui/agent-markdown.js');

class Node {
  constructor(tag, text = '') {
    this.tag = tag; this.children = []; this._text = text; this.className = '';
    this.style = {values: {}, setProperty: (key, value) => { this.style.values[key] = value; }};
  }
  append(...children) { this.children.push(...children); }
  set textContent(value) { this.children = []; this._text = value; }
  get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
}
global.document = {
  createElement: tag => new Node(tag),
  createTextNode: text => new Node('#text', text)
};
const descendants = (node, tag) => node.children.flatMap(child =>
  [...(child.tag === tag ? [child] : []), ...descendants(child, tag)]);

test('Markdown table becomes native table with safe inline links', () => {
  const root = new Node('div');
  renderer.render(root, '| 操作 | 作用 |\n|---|---|\n| `course.start` | [学习通](https://example.com/start) |');
  assert.equal(descendants(root, 'table').length, 1);
  assert.equal(descendants(root, 'th').length, 2);
  assert.equal(descendants(root, 'td').length, 2);
  assert.equal(descendants(root, 'a')[0].href, 'https://example.com/start');
  assert.equal(descendants(root, 'a')[0].rel, 'noopener noreferrer');
});

test('unsafe URLs remain text and cannot create active elements', () => {
  const root = new Node('div');
  renderer.render(root, '[危险](javascript:alert) ![x](data:text/html,evil)');
  assert.equal(descendants(root, 'a').length, 0);
  assert.equal(descendants(root, 'img').length, 0);
  assert.match(root.textContent, /javascript:alert/);
});

test('animated graphic is declarative and bounded', () => {
  const root = new Node('div');
  renderer.render(root, '```animation\n{"title":"进度","bars":[{"label":"任务","value":65}]}\n```');
  assert.equal(descendants(root, 'figure').length, 1);
  assert.equal(descendants(root, 'figure')[0].className, 'agent-animation');
  assert.equal(descendants(root, 'figure')[0].textContent, '进度任务65%');
  assert.equal(descendants(root, 'span').find(node => node.style.values['--agent-bar-width'])
    .style.values['--agent-bar-width'], '65%');
});
