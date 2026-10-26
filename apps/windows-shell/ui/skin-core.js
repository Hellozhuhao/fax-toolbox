(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.FaxSkinCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const FORMAT = 'fax-toolbox.skin', VERSION = 1, DEFAULT = 'fax.classic';
  const MAX_PACKAGE_BYTES = 1024 * 1024;
  const idPattern = /^[a-z][a-z0-9.-]{0,79}$/;
  const colorPattern = /^#[0-9a-f]{6}$/i;
  const colors = ['background', 'panel', 'surface', 'text', 'muted', 'accent', 'border', 'navigation'];
  function fail(message) { throw new Error(message); }
  function object(value, name) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) fail(name + ' 必须是对象');
    for (const key of Object.keys(value)) if (['__proto__', 'prototype', 'constructor'].includes(key)) fail('不支持的属性：' + key);
    return value;
  }
  function keys(value, allowed, name) {
    object(value, name);
    for (const key of Object.keys(value)) if (!allowed.includes(key)) fail(name + ' 包含未知属性：' + key);
  }
  function text(value, max, name) {
    if (typeof value !== 'string' || !value.trim() || value.length > max) fail(name + ' 不正确');
    return value;
  }
  function validateCss(css) {
    if (typeof css !== 'string' || css.length > 128 * 1024) fail('CSS 超过限制');
    // No escaped URLs, network imports, font downloads, or document-global rules.
    if (/\\|@import|@font-face|@namespace|@page|@property|@keyframes|url\s*\(|image-set\s*\(|(?:https?|file|blob|data)\s*:|src\s*\(|expression\s*\(|-moz-binding|behavior\s*:/i.test(css))
      fail('皮肤 CSS 不支持外部资源、转义序列或全局规则；图片可放在扩展模板的 data URI 中');
    return css;
  }
  function validatePackage(input) {
    const json = typeof input === 'string' ? input : JSON.stringify(input);
    if (new TextEncoder().encode(json).length > MAX_PACKAGE_BYTES) fail('单个皮肤不能超过 1 MiB');
    const skin = JSON.parse(json);
    keys(skin, ['format', 'version', 'id', 'name', 'author', 'description', 'appearance', 'targets', 'tokens', 'layout', 'css', 'bindings', 'extension'], '皮肤');
    if (skin.format !== FORMAT || skin.version !== VERSION) fail('需要 fax-toolbox.skin 第 1 版格式');
    if (!idPattern.test(skin.id || '')) fail('皮肤 id 需要小写字母、数字、点或短横线');
    text(skin.name, 60, '皮肤名称');
    if (skin.author !== undefined) text(skin.author, 100, '作者');
    if (skin.description !== undefined) text(skin.description, 500, '说明');
    if (!['light', 'dark'].includes(skin.appearance)) fail('appearance 需要是 light 或 dark');
    if (!Array.isArray(skin.targets) || !skin.targets.length || skin.targets.length > 64 ||
        skin.targets.some(id => id !== '*' && !idPattern.test(id))) fail('targets 不正确');
    keys(skin.tokens, [...colors, 'radius', 'fontFamily'], 'tokens');
    for (const key of colors) if (!colorPattern.test(skin.tokens[key] || '')) fail('tokens.' + key + ' 需要 #RRGGBB 颜色');
    if (!Number.isInteger(skin.tokens.radius) || skin.tokens.radius < 0 || skin.tokens.radius > 32) fail('radius 需要是 0–32 的整数');
    if (skin.tokens.fontFamily !== undefined && !/^[\p{L}\p{N} ,"'-]{1,160}$/u.test(skin.tokens.fontFamily)) fail('fontFamily 不正确');
    if (skin.layout !== undefined) {
      keys(skin.layout, ['navigation', 'density', 'motion'], 'layout');
      if (skin.layout.navigation !== undefined && !['left', 'top', 'right'].includes(skin.layout.navigation)) fail('navigation 不正确');
      if (skin.layout.density !== undefined && !['comfortable', 'compact'].includes(skin.layout.density)) fail('density 不正确');
      if (skin.layout.motion !== undefined && !['system', 'reduced'].includes(skin.layout.motion)) fail('motion 不正确');
    }
    if (skin.css !== undefined) validateCss(skin.css);
    if (skin.bindings !== undefined) {
      if (!Array.isArray(skin.bindings) || skin.bindings.length > 64) fail('bindings 不能超过 64 项');
      for (const binding of skin.bindings) {
        keys(binding, ['event', 'selector', 'key', 'action', 'args'], 'binding');
        if (!['click', 'keydown'].includes(binding.event)) fail('binding.event 不正确');
        text(binding.selector, 200, 'binding.selector'); text(binding.action, 100, 'binding.action');
        if (binding.event === 'keydown') text(binding.key, 40, 'binding.key');
        if (binding.args !== undefined) object(binding.args, 'binding.args');
      }
    }
    if (skin.extension !== undefined) {
      keys(skin.extension, ['html', 'css', 'script', 'actions', 'mode', 'title'], 'extension');
      text(skin.extension.html, 128 * 1024, 'extension.html');
      if (typeof skin.extension.script !== 'string' || skin.extension.script.length > 128 * 1024) fail('extension.script 不正确');
      if (skin.extension.css !== undefined) validateCss(skin.extension.css);
      if (!['replace', 'append'].includes(skin.extension.mode)) fail('extension.mode 不正确');
      if (skin.extension.title !== undefined) text(skin.extension.title, 80, 'extension.title');
      if (!Array.isArray(skin.extension.actions) || skin.extension.actions.length > 64 ||
          skin.extension.actions.some(action => typeof action !== 'string' || !/^[a-z][a-z0-9.-]{0,99}$/.test(action))) fail('extension.actions 不正确');
    }
    return skin;
  }
  function supports(skin, scope) { return skin.targets.includes('*') || skin.targets.includes(scope); }
  function normalizeSettings(input, skins) {
    keys(input, ['globalId', 'overrides'], 'settings');
    const map = new Map(skins.map(skin => [skin.id, skin]));
    const global = map.get(input.globalId);
    if (!global || !supports(global, 'shell')) fail('全局皮肤不存在或不支持工具箱');
    object(input.overrides, 'overrides');
    if (Object.keys(input.overrides).length > 64) fail('模块覆盖不能超过 64 项');
    const overrides = {};
    for (const [scope, id] of Object.entries(input.overrides)) {
      if (scope === 'shell' || !idPattern.test(scope)) fail('模块 id 不正确');
      if (!map.has(id) || !supports(map.get(id), scope)) fail('皮肤不支持模块：' + scope);
      overrides[scope] = id;
    }
    return {globalId: input.globalId, overrides};
  }
  function resolve(skins, settings, scope) {
    const candidate = skins.find(skin => skin.id === (settings.overrides[scope] || settings.globalId));
    return candidate && supports(candidate, scope) ? candidate :
      skins.find(skin => skin.id === settings.globalId && supports(skin, scope)) || skins.find(skin => skin.id === DEFAULT);
  }
  return Object.freeze({FORMAT, VERSION, DEFAULT, MAX_PACKAGE_BYTES, validatePackage, validateCss, normalizeSettings, supports, resolve});
});
