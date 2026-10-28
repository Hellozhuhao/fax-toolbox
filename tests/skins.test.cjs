const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Core = require('../apps/windows-shell/ui/skin-core.js');
const builtins = require('../apps/windows-shell/ui/skins/builtin.json');
const copy = value => JSON.parse(JSON.stringify(value));

test('all built-in skins and distributable examples use the same versioned format', () => {
  for (const skin of builtins) assert.equal(Core.validatePackage(skin).id, skin.id);
  for (const file of fs.readdirSync(path.join(__dirname, '../examples/skins'))) {
    const skin = Core.validatePackage(fs.readFileSync(path.join(__dirname, '../examples/skins', file), 'utf8'));
    assert.equal(skin.version, 1);
  }
});
test('global selection and per-module overrides resolve independently', () => {
  const settings = Core.normalizeSettings({globalId: 'fax.classic', overrides: {ai: 'fax.midnight'}}, builtins);
  assert.equal(Core.resolve(builtins, settings, 'shell').id, 'fax.classic');
  assert.equal(Core.resolve(builtins, settings, 'ai').id, 'fax.midnight');
  assert.equal(Core.resolve(builtins, settings, 'course').id, 'fax.classic');
  assert.equal(Core.resolve(builtins, {...settings, globalId: 'fax.ocean'}, 'ai').id, 'fax.midnight');
  assert.equal(Core.resolve(builtins, {globalId: 'fax.ocean', overrides: {}}, 'ai').id, 'fax.ocean');
});
test('shell-only packages fall back for modules and cannot be assigned to them', () => {
  const special = {...copy(builtins[0]), id: 'custom.shell', targets: ['shell']};
  const all = [...builtins, special];
  assert.equal(Core.resolve(all, {globalId: special.id, overrides: {}}, 'course').id, Core.DEFAULT);
  assert.throws(() => Core.normalizeSettings({globalId: Core.DEFAULT, overrides: {course: special.id}}, all), /不支持模块/);
});
test('unsupported versions, malformed design tokens and unknown fields fail before applying', () => {
  for (const mutate of [s => s.version = 2, s => s.tokens.accent = 'red; color:red', s => s.tokens.radius = 100,
    s => s.extra = true, s => s.layout = {navigation: 'unknown'}, s => s.targets = [], s => s.id = '../skin']) {
    const skin = copy(builtins[0]); mutate(skin); assert.throws(() => Core.validatePackage(skin));
  }
});
test('network CSS and escaped URL attempts are rejected', () => {
  for (const css of ['@import "https://example.com";', '.x{background:url(https://example.com)}', '.x{background:u\\72l(x)}', '@font-face{font-family:x;src:url(x)}', '@property --x{syntax:"*"}', '.x{background:image-set("https://example.com" 1x)}'])
    assert.throws(() => Core.validatePackage({...builtins[0], css}), /CSS/);
});
test('bounded packages and overrides prevent unbounded persistence', () => {
  assert.throws(() => Core.validatePackage({...builtins[0], css: 'a'.repeat(Core.MAX_PACKAGE_BYTES)}));
  assert.throws(() => Core.normalizeSettings({globalId: Core.DEFAULT, overrides: Object.fromEntries(Array.from({length: 65}, (_, i) => ['scope' + i, Core.DEFAULT]))}, builtins));
});
test('prototype keys cannot become registry entries or settings', () => {
  assert.throws(() => Core.normalizeSettings(JSON.parse('{"globalId":"fax.classic","overrides":{"__proto__":"fax.midnight"}}'), builtins));
  const skin = copy(builtins[0]); skin.bindings = [{event: 'click', selector: 'button', action: 'navigation.home', args: JSON.parse('{"__proto__":{}}')}];
  assert.throws(() => Core.validatePackage(skin));
});
