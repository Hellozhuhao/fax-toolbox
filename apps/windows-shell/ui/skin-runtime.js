(() => {
  'use strict';
  const Core = window.FaxSkinCore, $ = id => document.getElementById(id);
  const native = !!window.chrome?.webview;
  const scopes = new Map(), actions = new Map(), frames = new Map();
  let state, bindingsController, busy = false, writeTimer, initialTimer, view = document.body.dataset.view || 'home';
  const builtinIds = new Set();
  let undoSettings, pendingFeedback, pendingFocus;
  const send = (command, extra = {}) => window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra}));
  const feedback = (message, error = false) => { $('skin-feedback').textContent = message; $('skin-feedback').classList.toggle('skin-error', error); };
  const defaultSettings = () => ({globalId: Core.DEFAULT, overrides: {}});
  const moduleScope = () => scopes.has(view) ? view : 'shell';
  function editable(next) {
    return {settings: next.settings, imported: next.skins.filter(skin => next.importedIds.includes(skin.id))};
  }
  function tokens(element, skin) {
    element.dataset.skinId = skin.id; element.dataset.skinAppearance = skin.appearance;
    const aliases = {background: 'bg', panel: 'panel', surface: 'surface', text: 'text', muted: 'subtle', accent: 'accent', border: 'border', navigation: 'nav'};
    for (const [name, value] of Object.entries(skin.tokens)) {
      element.style.setProperty('--skin-' + name.replace(/[A-Z]/g, letter => '-' + letter.toLowerCase()), name === 'radius' ? value + 'px' : value);
      if (aliases[name]) element.style.setProperty('--' + aliases[name], value);
    }
    element.style.setProperty('--skin-color-scheme', skin.appearance);
    element.style.setProperty('--skin-font-family', skin.tokens.fontFamily || '"Fax Sans", "Microsoft YaHei UI", sans-serif');
    const rgb=skin.tokens.accent.slice(1).match(/../g).map(value=>{const n=parseInt(value,16)/255;return n<=.04045?n/12.92:((n+.055)/1.055)**2.4;});
    const light=rgb[0]*.2126+rgb[1]*.7152+rgb[2]*.0722;
    element.style.setProperty('--on-accent', (light+.05)/.05 > 1.05/(light+.05) ? '#101820' : '#ffffff');
    const brightness=color=>{const channels=color.slice(1).match(/../g).map(value=>{const n=parseInt(value,16)/255;return n<=.04045?n/12.92:((n+.055)/1.055)**2.4;});return channels[0]*.2126+channels[1]*.7152+channels[2]*.0722;};
    const contrast=(a,b)=>{const x=brightness(a),y=brightness(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
    element.style.setProperty('--error',['#b32939','#ffa0ad',skin.tokens.text].find(color=>[skin.tokens.panel,skin.tokens.background].every(background=>contrast(color,background)>=4.5))||skin.tokens.text);
    element.style.setProperty('--skin-card-padding', skin.layout?.density === 'compact' ? '14px' : '20px');
    element.dataset.skinDensity = skin.layout?.density || 'comfortable';
    element.dataset.skinMotion = skin.layout?.motion || 'system';
  }
  function scopedCss(css, selector) {
    if (!css) return '';
    Core.validateCss(css);
    const sheet = new CSSStyleSheet(); sheet.replaceSync(css);
    function compile(rules) {
      return Array.from(rules).map(rule => {
        if (rule.type === CSSRule.STYLE_RULE) {
          for (const property of Array.from(rule.style)) rule.style.setProperty(property, rule.style.getPropertyValue(property), 'important');
          const original = rule.selectorText.replace(/:scope\b/g, selector);
          const boundary = selector === '.app' ? ':not([data-skin-scope],[data-skin-scope] *)' : '';
          return `:is(${selector}) :is(${original})${boundary},:is(${selector}):is(${original})${boundary} {${rule.style.cssText}}`;
        }
        if (rule.type === CSSRule.MEDIA_RULE) return `@media ${rule.conditionText} {${compile(rule.cssRules)}}`;
        if (rule.type === CSSRule.SUPPORTS_RULE) return `@supports ${rule.conditionText} {${compile(rule.cssRules)}}`;
        throw new Error('皮肤 CSS 只支持普通选择器、@media 和 @supports');
      }).join('\n');
    }
    return compile(sheet.cssRules);
  }
  function invoke(name, args, scope) {
    const action = actions.get(name);
    if (!action || (!action.scopes.includes('*') && !action.scopes.includes(scope))) throw new Error('当前范围未注册操作：' + name);
    if (!args || typeof args !== 'object' || Array.isArray(args) || JSON.stringify(args).length > 4096) throw new Error('操作参数不正确');
    for (const key of Object.keys(args)) if (['__proto__', 'prototype', 'constructor'].includes(key)) throw new Error('不支持的操作参数');
    if (action.validate && !action.validate(args)) throw new Error('操作参数不符合规范：' + name);
    return action.run(args);
  }
  function stateFor(scope) {
    return {version: 1, scope, view, skinId: Core.resolve(state.skins, state.settings, scope).id,
      ...window.FaxToolboxUi.skinSnapshot(scope)};
  }
  function postState() {
    for (const [scope, entry] of frames) entry.frame.contentWindow?.postMessage({channel: entry.channel, type: 'skin.state', state: stateFor(scope)}, '*');
  }
  function recoverExtension(scope, reason) {
    const entry = frames.get(scope); if (!entry) return;
    clearTimeout(entry.timer); entry.frame.remove(); frames.delete(scope);
    entry.surface.removeAttribute('data-skin-extension');
    updateView(); feedback('扩展已停用，原界面已恢复：' + reason, true);
  }
  function extension(scope, skin, surface) {
    const config = skin.extension; if (!config) return;
    for (const name of config.actions) if (!actions.has(name)) throw new Error('扩展引用未注册操作：' + name);
    const frame = document.createElement('iframe');
    frame.className = 'skin-extension'; frame.title = config.title || skin.name + '自定义界面';
    frame.setAttribute('sandbox', 'allow-scripts'); frame.setAttribute('referrerpolicy', 'no-referrer');
    frame.src = 'skin-frame.html';
    const channel = crypto.randomUUID();
    const entry = {frame, channel, surface, config, ready: false, calls: [], timer: null};
    frames.set(scope, entry);
    frame.addEventListener('load', () => {
      if (entry.ready) { recoverExtension(scope, '扩展发生了意外导航'); return; }
      frame.contentWindow.postMessage({channel, type: 'skin.init', html: config.html,
        css: `:root{color-scheme:${skin.appearance}} body{font-family:${skin.tokens.fontFamily || 'sans-serif'}}\n` + (config.css || ''),
        script: config.script, state: stateFor(scope)}, '*');
    });
    frame.addEventListener('error', () => recoverExtension(scope, '无法加载扩展'));
    entry.timer = setTimeout(() => recoverExtension(scope, '扩展启动超时'), 5000);
    if (scope === 'shell') { frame.classList.add('skin-shell-frame'); document.body.append(frame); }
    else surface.append(frame);
    // The original interface is hidden only after the extension signals readiness.
  }
  addEventListener('message', async event => {
    const message = event.data;
    if (!message || typeof message !== 'object') return;
    const pair = [...frames].find(([, entry]) => entry.frame.contentWindow === event.source && entry.channel === message.channel);
    if (!pair) return;
    const [scope, entry] = pair;
    if (message.type === 'skin.ready') {
      clearTimeout(entry.timer); entry.ready = true;
      entry.surface.dataset.skinExtension = entry.config.mode; updateView(); return;
    }
    if (message.type === 'skin.error') { recoverExtension(scope, String(message.message || '扩展出错').slice(0, 200)); return; }
    if (message.type !== 'skin.invoke' || typeof message.id !== 'string' || message.id.length > 80) return;
    let result = null, error;
    try {
      if (!entry.ready || (scope === 'shell' ? !['home', 'modules', 'workspace'].includes(view) : moduleScope() !== scope)) throw new Error('此皮肤界面当前未激活');
      const now = Date.now(); entry.calls = entry.calls.filter(time => now - time < 1000);
      if (entry.calls.length >= 16) throw new Error('操作频率过高'); entry.calls.push(now);
      if (!entry.config.actions.includes(message.action)) throw new Error('此操作未在皮肤中声明');
      result = await invoke(message.action, message.args || {}, scope);
      const serialized = JSON.stringify(result === undefined ? null : result);
      if (serialized.length > 65536) throw new Error('操作返回结果过大');
      result = JSON.parse(serialized);
    } catch (failure) { error = failure.message; }
    entry.frame.contentWindow?.postMessage({channel: entry.channel, type: 'skin.result', id: message.id, result, error}, '*');
  });
  function matchesKey(event, value) {
    const parts = value.toLowerCase().split('+'), key = parts.pop();
    return event.key.toLowerCase() === key && event.ctrlKey === parts.includes('ctrl') &&
      event.altKey === parts.includes('alt') && event.shiftKey === parts.includes('shift') && event.metaKey === parts.includes('meta');
  }
  function bind(scope, skin, surface, signal) {
    for (const binding of skin.bindings || []) {
      if (!actions.has(binding.action)) throw new Error('未注册的皮肤操作：' + binding.action);
      if (binding.selector !== ':scope') surface.querySelector(binding.selector); // Validate selectors before enabling bindings.
      let invoking = false;
      const listenerTarget=binding.event==='keydown'?document:surface;
      listenerTarget.addEventListener(binding.event, event => {
        if (invoking || moduleScope() !== scope) return;
        if (binding.event === 'keydown' && (!matchesKey(event, binding.key) || event.repeat)) return;
        const element = binding.event==='keydown'&&binding.selector===':scope'?surface:event.target instanceof Element ? event.target.closest(binding.selector === ':scope' ? '*' : binding.selector) : null;
        if (!element || !surface.contains(element) || element.closest(':disabled,[aria-disabled="true"]')) return;
        event.preventDefault(); event.stopImmediatePropagation(); invoking = true;
        try { Promise.resolve(invoke(binding.action, binding.args || {}, scope)).catch(error => feedback(error.message, true)); }
        catch (error) { feedback(error.message, true); }
        finally { invoking = false; }
      }, {capture: true, signal});
    }
  }
  function updateView() {
    if (!state) return;
    document.documentElement.dataset.theme = Core.resolve(state.skins, state.settings, moduleScope()).appearance;
    const globalFrame = frames.get('shell');
    const replace = globalFrame?.ready && globalFrame.config.mode === 'replace' && ['home', 'modules', 'workspace'].includes(view);
    document.body.classList.toggle('skin-shell-replaced', !!replace);
    if (globalFrame) globalFrame.frame.hidden = !['home', 'modules', 'workspace'].includes(view);
    $('skin-recovery').hidden = !state.importedIds.includes(Core.resolve(state.skins, state.settings, moduleScope()).id) && ![...frames.values()].some(entry => entry.ready && entry.config.mode === 'replace');
    postState();
  }
  function apply(next) {
    const skins = next.skins.map(Core.validatePackage);
    if (new Set(skins.map(skin => skin.id)).size !== skins.length) throw new Error('皮肤 ID 重复');
    const settings = Core.normalizeSettings(next.settings, skins);
    const app = document.querySelector('.app');
    let css = scopedCss(Core.resolve(skins, settings, 'shell').css, '.app');
    for (const [scope, descriptor] of scopes) css += scopedCss(Core.resolve(skins, settings, scope).css, `[data-skin-scope="${scope}"]`);
    // Validate bindings before altering the live interface.
    for (const [scope, skin] of [['shell', Core.resolve(skins, settings, 'shell')], ...[...scopes].map(([id]) => [id, Core.resolve(skins, settings, id)])]) {
      for (const binding of skin.bindings || []) {
        if (!actions.has(binding.action)) throw new Error('未注册操作：' + binding.action);
        document.createElement('div').querySelector(binding.selector);
      }
      for (const action of skin.extension?.actions || []) if (!actions.has(action)) throw new Error('未注册操作：' + action);
    }
    state = {...next, skins, settings};
    bindingsController?.abort(); bindingsController = new AbortController();
    for (const entry of frames.values()) { clearTimeout(entry.timer); entry.frame.remove(); entry.surface.removeAttribute('data-skin-extension'); }
    frames.clear();
    $('skin-custom-styles').textContent = css;
    const global = Core.resolve(skins, settings, 'shell');
    tokens(document.documentElement, global); tokens(app, global);
    app.dataset.skinNavigation = global.layout?.navigation || 'left';
    bind('shell', global, app, bindingsController.signal); extension('shell', global, app);
    for (const [scope, {surface}] of scopes) {
      const skin = Core.resolve(skins, settings, scope); tokens(surface, skin);
      bind(scope, skin, surface, bindingsController.signal); if (scope !== 'android') extension(scope, skin, surface);
    }
    try { localStorage.setItem('fax-toolbox-theme', global.appearance); } catch (_) {}
    updateView(); renderManager();
    window.dispatchEvent(new Event('fax:skin-applied'));
  }
  function option(skin) { const node = document.createElement('option'); node.value = skin.id; node.textContent = skin.name + (builtinIds.has(skin.id) ? ' · 内置' : ' · 导入'); return node; }
  function renderManager() {
    const active=document.activeElement;
    const focused=pendingFocus || (active?.matches('input[name="skin-choice"]') ? {radio:active.value} : null);
    pendingFocus=null;
    const global = $('skin-global'); global.replaceChildren(...state.skins.filter(skin => Core.supports(skin, 'shell')).map(option)); global.value = state.settings.globalId;
    const selected = state.skins.find(skin => skin.id === global.value);
    const gallery=$('skin-gallery'); gallery.replaceChildren();
    for(const skin of state.skins.filter(skin=>Core.supports(skin,'shell'))) {
      const choice=document.createElement('label');choice.className='skin-choice';
      const radio=document.createElement('input');radio.type='radio';radio.name='skin-choice';radio.value=skin.id;radio.checked=skin.id===selected.id;radio.setAttribute('aria-label',skin.name);
      radio.addEventListener('change',()=>{if(radio.checked&&!busy){global.value=skin.id;global.dispatchEvent(new Event('change',{bubbles:true}));}});
      const preview=document.createElement('span');preview.className='skin-mini';preview.setAttribute('aria-hidden','true');
      for(const [role,token] of Object.entries({bg:'background',panel:'panel',surface:'surface',nav:'navigation',border:'border',text:'text',muted:'muted',accent:'accent'}))preview.style.setProperty('--preview-'+role,skin.tokens[token]);
      preview.innerHTML='<span class="skin-mini-nav"><i></i><i></i><i></i></span><span class="skin-mini-body"><strong></strong><i></i><span class="skin-mini-paper"><i></i><i></i></span></span>';
      const caption=document.createElement('span');caption.className='skin-choice-caption';caption.textContent=skin.name;caption.insertAdjacentHTML('beforeend',window.FaxIcons.svg('check'));
      const note=document.createElement('small');note.textContent=builtinIds.has(skin.id)?'内置皮肤':'已导入';
      choice.append(radio,preview,caption,note);gallery.append(choice);
    }
    $('home-skin-name').textContent=selected.name;
    $('skin-description').textContent = [selected.description, selected.author && '作者：' + selected.author].filter(Boolean).join(' · ');
    const swatches = $('skin-swatches'); swatches.replaceChildren();
    for (const name of ['background', 'panel', 'accent', 'text']) { const swatch = document.createElement('span'); swatch.style.background = selected.tokens[name]; swatch.title = name + ' ' + selected.tokens[name]; swatches.append(swatch); }
    const rows = $('skin-module-list'); rows.replaceChildren();
    for (const [scope, descriptor] of scopes) {
      const label = document.createElement('label'); label.className = 'skin-module-row';
      const name = document.createElement('span'); name.className='skin-module-name';
      name.innerHTML=window.FaxIcons.svg(scope==='ai'?'agent':scope);
      const text=document.createElement('span');text.append(document.createTextNode(descriptor.label));
      const hint=document.createElement('small');hint.textContent=state.settings.overrides[scope]?'独立选择':'继承全局';text.append(hint);name.append(text);
      const select = document.createElement('select'); select.dataset.skinModule = scope; select.setAttribute('aria-label', descriptor.label + '的皮肤');
      const inherit = document.createElement('option'); inherit.value = ''; inherit.textContent = '跟随全局'; select.append(inherit);
      select.append(...state.skins.filter(skin => Core.supports(skin, scope)).map(option)); select.value = state.settings.overrides[scope] || '';
      select.addEventListener('change', () => {
        const settings = structuredClone(state.settings);
        if (select.value) settings.overrides[scope] = select.value; else delete settings.overrides[scope];
        save({...state, settings}, '模块皮肤已保存');
      });
      label.hidden=scope==='demo'&&!$('developer-mode').checked;
      label.append(name, select); rows.append(label);
    }
    const manage = $('skin-manage'); manage.replaceChildren(...state.skins.map(option)); manage.value = selected.id;
    setBusy(false); updateManage();
    $('skin-undo').hidden=!undoSettings;
    window.FaxUi?.refresh();
    if(focused && $('skin-manager').checkVisibility() && (document.activeElement===document.body || $('skin-manager').contains(document.activeElement))) {
      const target=focused.radio ? [...gallery.querySelectorAll('input')].find(input=>input.value===focused.radio) : [...rows.querySelectorAll('select')].find(select=>select.dataset.skinModule===focused.module)?.parentElement.querySelector('[role="combobox"]');
      if(target&&!target.disabled)target.focus({preventScroll:true});
    }
  }
  function updateManage() { $('skin-remove').disabled = busy || !state?.importedIds.includes($('skin-manage').value); $('skin-unify').disabled=busy||!Object.keys(state?.settings.overrides||{}).length; }
  function setBusy(value) {
    busy = value;
    for (const element of $('skin-manager').querySelectorAll('button,select,input')) element.disabled = value;
    if (!value && state) updateManage();
  }
  function save(next, message, allowUndo = true) {
    if (busy) return;
    try {
      const focused=document.activeElement;
      const module=focused?.closest('.skin-module-row')?.querySelector('select')?.dataset.skinModule;
      pendingFocus=focused?.matches('input[name="skin-choice"]') ? {radio:focused.value} : module ? {module} : null;
      next.settings = Core.normalizeSettings(next.settings, next.skins);
      const stored = editable(next);
      if(allowUndo && JSON.stringify(state.settings)!==JSON.stringify(next.settings)) undoSettings=structuredClone(state.settings);
      pendingFeedback=message;
      if (new TextEncoder().encode(JSON.stringify(stored)).length > 4 * 1024 * 1024) throw new Error('皮肤库不能超过 4 MiB');
      if (native) {
        setBusy(true); feedback('正在保存…'); send('skins.update', {state: stored});
        writeTimer = setTimeout(() => { setBusy(false); feedback('保存尚未确认，请重新读取皮肤配置', true); send('skins.get'); }, 10000);
      } else {
        localStorage.setItem('fax-toolbox-skins-v1', JSON.stringify(stored)); apply(next); feedback(message); $('skin-undo').hidden=!undoSettings;
      }
    } catch (error) { feedback(error.message, true); if (state) renderManager(); pendingFocus=null; }
  }
  function recover() {
    if (native) { setBusy(true); send('skins.reset'); }
    else if (state) save({...state, settings: defaultSettings()}, '已恢复默认皮肤');
  }
  function registerScope(id, label, surface) {
    if (!/^[a-z][a-z0-9.-]{0,79}$/.test(id) || id === 'shell' || scopes.has(id)) throw new Error('模块皮肤范围重复或无效');
    const element = typeof surface === 'string' ? document.querySelector(surface) : surface;
    if (!(element instanceof Element)) throw new Error('找不到模块界面：' + id);
    element.dataset.skinScope = id; scopes.set(id, {label, surface: element});
    if (state) apply(state);
    return () => { scopes.delete(id); element.removeAttribute('data-skin-scope'); if (state) apply(state); };
  }
  for (const [name, action] of Object.entries(window.FaxToolboxUi.skinActions)) actions.set(name, action);
  for (const [id, label] of [['ai', 'Agent'], ['course', '学习通'], ['homework', '作业校对'], ['proxy', '网络管家'], ['demo', '页面事件演示']]) registerScope(id, label, '#view-' + id);
  // Android has a native WPF surface and uses the same persisted scope selection.
  const androidMarker = document.createElement('span'); androidMarker.hidden = true; document.body.append(androidMarker); registerScope('android', '安卓投屏', androidMarker);
  const customStyle = document.createElement('style'); customStyle.id = 'skin-custom-styles'; document.head.append(customStyle);
  $('developer-mode').addEventListener('change',()=>{if(state&&!busy)renderManager();});
  $('skin-global').addEventListener('change', event => save({...state, settings: {...state.settings, globalId: event.target.value}}, '全局皮肤已保存'));
  $('skin-unify').addEventListener('click', () => save({...state, settings: {globalId: state.settings.globalId, overrides: {}}}, '所有模块已跟随全局皮肤'));
  $('skin-reset').addEventListener('click', async () => {if(await window.FaxToolboxUi.confirm({title:'恢复默认外观？',message:'全局与各模块将恢复默认选择，已导入的皮肤会保留，当前任务不会结束。',confirmLabel:'恢复默认'}))recover();}); $('skin-recovery-reset').addEventListener('click', recover);
  $('skin-recovery-open').addEventListener('click', () => { window.FaxToolboxUi.showActivity(); $('skin-manager').scrollIntoView({block: 'start'}); });
  $('settings-open-skins').addEventListener('click', () => { window.FaxToolboxUi.showActivity(); $('skin-manager').scrollIntoView({block: 'start'}); });
  $('skin-undo').addEventListener('click',()=>{if(!undoSettings||busy)return;const settings=Core.normalizeSettings(undoSettings,state.skins);undoSettings=null;save({...state,settings},'已撤销皮肤选择',false);});
  $('skin-import').addEventListener('click', () => $('skin-file').click());
  async function importFile(file) {
    if(!file||busy)return;
    try {
      if (file.size > Core.MAX_PACKAGE_BYTES) throw new Error('单个皮肤不能超过 1 MiB');
      const skin = Core.validatePackage(await file.text());
      if (builtinIds.has(skin.id)) throw new Error('内置皮肤的 ID 不能被覆盖');
      const existing = state.skins.find(item => item.id === skin.id);
      if (!existing && state.importedIds.length >= 32) throw new Error('最多导入 32 个皮肤');
      // Validate CSS and action compatibility now, before persisting the package.
      scopedCss(skin.css, '.app');
      for (const binding of skin.bindings || []) { document.createElement('div').querySelector(binding.selector); if (!actions.has(binding.action)) throw new Error('未注册操作：' + binding.action); }
      for (const action of skin.extension?.actions || []) if (!actions.has(action)) throw new Error('未注册操作：' + action);
      const next = {...state, skins: [...state.skins.filter(item => item.id !== skin.id), skin], importedIds: [...new Set([...state.importedIds, skin.id])]};
      if (existing) window.FaxToolboxUi.confirmSkinReplace(skin.name, () => save(next, '皮肤已更新')); else save(next, '皮肤已导入，请在列表中选择');
    } catch (error) { feedback('导入失败：' + error.message, true); }
    }
  $('skin-file').addEventListener('change',event=>{const file=event.target.files[0];event.target.value='';importFile(file);});
  $('skin-manager').addEventListener('dragover',event=>{if([...event.dataTransfer.types].includes('Files')){event.preventDefault();$('skin-manager').classList.add('drag-over');}});
  $('skin-manager').addEventListener('dragleave',event=>{if(!$('skin-manager').contains(event.relatedTarget))$('skin-manager').classList.remove('drag-over');});
  $('skin-manager').addEventListener('drop',event=>{event.preventDefault();$('skin-manager').classList.remove('drag-over');if(event.dataTransfer.files.length!==1){feedback('请一次导入一个皮肤文件',true);return;}importFile(event.dataTransfer.files[0]);});
  $('skin-manage').addEventListener('change', updateManage);
  $('skin-export').addEventListener('click', () => {
    const skin = state.skins.find(item => item.id === $('skin-manage').value);
    const url = URL.createObjectURL(new Blob([JSON.stringify(skin, null, 2) + '\n'], {type: 'application/json'}));
    const link = document.createElement('a'); link.href = url; link.download = skin.id + '.faxskin'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  $('skin-remove').addEventListener('click', async () => {
    const id = $('skin-manage').value; if (!state.importedIds.includes(id)) return;
    const affected=[...(state.settings.globalId===id?['全局外观']:[]),...Object.entries(state.settings.overrides).filter(([,value])=>value===id).map(([scope])=>scopes.get(scope)?.label||scope)];
    const name=state.skins.find(skin=>skin.id===id).name;
    if(!await window.FaxToolboxUi.confirm({title:'移除皮肤“'+name+'”？',message:affected.length?'正在使用的范围：'+affected.join('、')+'。移除后将恢复默认或继承全局，当前任务会保留。':'该皮肤文件将从本机皮肤库移除。',confirmLabel:'移除皮肤',destructive:true}))return;
    undoSettings=null;
    const settings = structuredClone(state.settings);
    if (settings.globalId === id) settings.globalId = Core.DEFAULT;
    for (const scope of Object.keys(settings.overrides)) if (settings.overrides[scope] === id) delete settings.overrides[scope];
    save({...state, settings, skins: state.skins.filter(skin => skin.id !== id), importedIds: state.importedIds.filter(value => value !== id)}, '皮肤已移除',false);
  });
  let stateFrame = 0;
  const scheduleState = () => { if (!stateFrame) stateFrame = requestAnimationFrame(() => { stateFrame = 0; if (state) postState(); }); };
  addEventListener('fax:ui-state-changed', scheduleState);
  document.addEventListener('input', event => { if (event.target.id === 'agent-prompt') scheduleState(); });
  addEventListener('fax:view-changed', event => { view = event.detail.view; updateView(); });
  addEventListener('fax:skin-state', event => {
    clearTimeout(writeTimer); clearTimeout(initialTimer);
    try {
      for (const skin of event.detail.skins) if (!event.detail.importedIds.includes(skin.id)) builtinIds.add(skin.id);
      apply(event.detail); feedback(event.detail.warning || pendingFeedback || '皮肤设置已同步', !!event.detail.warning);pendingFeedback=null;
    } catch (error) {
      setBusy(false); document.body.classList.remove('skin-shell-replaced');
      $('skin-recovery').hidden = false; feedback('皮肤无法应用，请恢复默认：' + error.message, true);
    }
  });
  addEventListener('fax:skin-error', event => { clearTimeout(writeTimer); clearTimeout(initialTimer); setBusy(!state); if (state) renderManager(); feedback(event.detail, true); });
  addEventListener('keydown', event => { if (event.ctrlKey && event.shiftKey && event.key === 'F12') { event.preventDefault(); recover(); } }, true);
  window.FaxToolboxSkins = Object.freeze({version: 1, registerScope,
    registerAction: (name, descriptor) => {
      if (!/^[a-z][a-z0-9.-]{0,99}$/.test(name) || actions.has(name) || typeof descriptor.run !== 'function' || !Array.isArray(descriptor.scopes)) throw new Error('操作注册不正确或重复');
      actions.set(name, descriptor); return () => actions.delete(name);
    },
    applyThemeFromHost: () => { if (state) updateView(); },
    getSelection: () => state ? structuredClone(state.settings) : null,
    reset: recover});
  setBusy(true);
  if (native) {
    initialTimer = setTimeout(() => feedback('皮肤接口未响应，请更新工具箱后重新打开。', true), 10000);
    send('skins.get');
  }
  else fetch('skins/builtin.json').then(response => { if (!response.ok) throw new Error('无法加载内置皮肤'); return response.json(); }).then(builtins => {
    builtins.forEach(skin => builtinIds.add(skin.id));
    let stored;
    try { stored = JSON.parse(localStorage.getItem('fax-toolbox-skins-v1') || 'null'); } catch (_) {}
    try {
      apply({skins: [...builtins, ...(stored?.imported || [])], settings: stored?.settings || defaultSettings(), importedIds: (stored?.imported || []).map(skin => skin.id)});
    } catch (error) { apply({skins: builtins, settings: defaultSettings(), importedIds: []}); feedback('已恢复默认界面：' + error.message, true); }
  }).catch(error => { setBusy(!state); feedback(error.message, true); });
})();
