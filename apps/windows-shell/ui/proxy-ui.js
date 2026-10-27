(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let config = null, state = null, dirty = false, busy = false, pending = null, awaitingSave = false;
  let applications = [], detectedOnce = false, detecting = false;
  let savedDraft = '', validationAttempted = false, formMessage = '', formError = false;
  let savingConfig = null;
  const fieldErrors = new Map();
  const send = (command, extra = {}) => window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra}));
  function setupQuickSettings() {
    const form = $('proxy-config'), fields = $('proxy-fields'), advanced = document.createElement('details');
    advanced.className = 'network-advanced'; advanced.innerHTML = '<summary>高级设置 <span>路径、进程规则与检查间隔</span></summary>';
    const engine = fields.querySelector('.network-core-choice'), grid = fields.querySelector('.network-fields');
    fields.prepend(engine);
    advanced.append(grid, fields.querySelector('.network-checks'), fields.querySelector(':scope > .network-note'));
    fields.insertBefore(advanced, fields.querySelector(':scope > .action-row'));
    const quick = document.createElement('section'); quick.className = 'network-simple';
    quick.innerHTML = '<div class="network-setup-heading"><div><h3>选择连接</h3><p class="muted">检测本机内核和配置，点击一项即可填好连接。</p></div><button id="proxy-detect" type="button" class="secondary">重新检测</button></div><p id="proxy-detect-status" class="muted" role="status"></p><div id="proxy-connections" class="network-connections"></div><div class="network-setup-heading"><div><h3>选择应用</h3><p class="muted">按应用运行状态切换系统连接。先打开目标应用再检测，或手动添加。</p></div><button id="proxy-add-app" class="secondary" type="button">添加应用</button></div><div id="proxy-apps" class="network-apps"></div>';
    const priority = $('proxy-priority').closest('label');
    priority.firstChild.textContent = '代理和直连应用同时运行';
    priority.classList.add('network-priority');
    quick.append(priority);
    const address = $('proxy-url').closest('label');
    address.classList.add('network-address');
    const hint = document.createElement('small'); hint.id = 'proxy-url-hint';
    hint.textContent = '检测后可自动填入，也可以手动输入，例如 http://127.0.0.1:10809。';
    address.append(hint); $('proxy-url').setAttribute('aria-describedby', hint.id);
    $('proxy-url').setAttribute('autocomplete', 'off'); $('proxy-url').spellcheck = false;
    quick.querySelector('#proxy-connections').after(address);
    fields.insertBefore(quick, advanced);
    form.noValidate = true;
    const errors = document.createElement('div'); errors.id = 'proxy-validation-summary';
    errors.className = 'validation-summary'; errors.hidden = true; errors.role = 'alert';
    form.querySelector('.network-heading').after(errors);
    const actions = fields.querySelector(':scope > .action-row'); actions.classList.add('network-savebar');
    actions.querySelector('[type="submit"]').id = 'proxy-save';
    const discard = document.createElement('button'); discard.id = 'proxy-discard';
    discard.type = 'button'; discard.className = 'secondary'; discard.textContent = '放弃修改'; discard.disabled = true;
    actions.insertBefore(discard, $('proxy-validate'));
    const saved = document.createElement('p'); saved.id = 'proxy-save-state'; saved.role = 'status';
    saved.className = 'network-save-state'; saved.textContent = '正在读取已保存配置…'; actions.after(saved);
    $('proxy-detect').addEventListener('click', detect);
    $('proxy-add-app').addEventListener('click', () => send('proxy.pick', {field: 'application'}));
  }
  function detect() {
    if (detecting) return;
    detecting = true; $('proxy-detect').disabled = true; $('proxy-detect-status').textContent = '正在检测本机连接和运行中的应用…';
    send('proxy.detect');
  }
  function syncFormState() {
    $('proxy-config').setAttribute('aria-busy', String(busy));
    $('proxy-save').textContent = awaitingSave ? '保存中…' : '保存配置';
    $('proxy-discard').disabled = !dirty || busy || !config || !!state?.running || !!state?.coreRunning;
    $('proxy-config-note').textContent = state?.running || state?.coreRunning ? '停止后可修改配置' : dirty ? '有未保存的更改' : '配置保存在本机';
    $('proxy-save-state').textContent = awaitingSave ? '正在保存，请稍候…' : formMessage || (dirty ? '更改尚未保存，保存后才能启用新配置。' : config ? '当前显示的是已保存配置。' : '正在读取已保存配置…');
    $('proxy-save-state').classList.toggle('error', formError && !awaitingSave);
  }
  function markDirty() {
    dirty = JSON.stringify(read()) !== savedDraft;
    syncConnectionChoices();
    formMessage = ''; formError = false;
    if (validationAttempted) validateFields(false);
    syncFormState();
  }
  function revealField(field) {
    for (let parent = field.parentElement; parent; parent = parent.parentElement)
      if (parent.tagName === 'DETAILS') parent.open = true;
    field.focus({preventScroll: true});
    field.scrollIntoView({block: 'center'});
  }
  function clearFieldErrors() {
    for (const field of fieldErrors.keys()) {
      field.removeAttribute('aria-invalid');
      const id = field.id + '-error';
      const descriptions = (field.getAttribute('aria-describedby') || '').split(/\s+/).filter(value => value && value !== id);
      if (descriptions.length) field.setAttribute('aria-describedby', descriptions.join(' '));
      else field.removeAttribute('aria-describedby');
      $(id)?.remove();
    }
    fieldErrors.clear(); $('proxy-validation-summary').hidden = true;
  }
  function validateFields(focus = true) {
    validationAttempted = true;
    clearFieldErrors();
    const invalid = (id, message) => fieldErrors.set($(id), message);
    try {
      const endpoint = new URL($('proxy-url').value.trim());
      if (endpoint.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(endpoint.hostname) ||
          endpoint.port === '0' || endpoint.username || endpoint.password || endpoint.pathname !== '/' || endpoint.search || endpoint.hash)
        throw new Error();
    } catch (_) { invalid('proxy-url', '请输入本机 HTTP 代理地址，例如 http://127.0.0.1:10809。'); }
    for (const [id, label, min, max] of [['proxy-poll', '检查间隔', 2, 30], ['proxy-stable', '稳定等待', 0, 60]]) {
      const field = $(id), value = field.value.trim(), number = Number(value);
      if (!value || !Number.isInteger(number) || number < min || number > max)
        invalid(id, `${label}应为 ${min}–${max} 秒的整数。`);
    }
    if (!$('proxy-system-check').checked && !$('proxy-env-check').checked)
      invalid('proxy-system-check', '至少选择管理系统代理或管理用户代理环境变量中的一项。');
    for (const id of ['proxy-core', 'proxy-core-config'])
      if ($(id).value.includes('"')) invalid(id, '路径不需要引号，请移除引号或用“选择”重新选取文件。');
    for (const id of ['proxy-work', 'proxy-games', 'proxy-game-paths', 'proxy-excludes']) {
      const entries = lines(id);
      if (entries.length > 100 || entries.some(value => value.length > 260))
        invalid(id, '每类规则最多 100 条，每条不超过 260 个字符。');
    }
    const summary = $('proxy-validation-summary'); summary.replaceChildren();
    if (!fieldErrors.size) return true;
    const title = document.createElement('strong'); title.textContent = `有 ${fieldErrors.size} 项设置需要修改`;
    const list = document.createElement('ul');
    for (const [field, message] of fieldErrors) {
      field.setAttribute('aria-invalid', 'true');
      const error = document.createElement('p'); error.id = field.id + '-error';
      error.className = 'ui-field-error'; error.textContent = message;
      (field.closest('.network-checks') || field.closest('label')).append(error);
      field.setAttribute('aria-describedby', [field.getAttribute('aria-describedby'), error.id].filter(Boolean).join(' '));
      const item = document.createElement('li'), link = document.createElement('a');
      link.href = '#' + field.id; link.textContent = message;
      link.addEventListener('click', event => { event.preventDefault(); revealField(field); });
      item.append(link); list.append(item);
    }
    summary.append(title, list); summary.hidden = false;
    if (focus) revealField(fieldErrors.keys().next().value);
    return false;
  }
  function renderApplications() {
    const holder = $('proxy-apps'); holder.replaceChildren();
    const work = lines('proxy-work').map(value => value.replace(/\.exe$/i, '').toLowerCase());
    const games = lines('proxy-games').map(value => value.replace(/\.exe$/i, '').toLowerCase());
    for (const app of applications) {
      const row = document.createElement('div'); row.className = 'network-app-row';
      const description = document.createElement('div'), name = document.createElement('strong'), hint = document.createElement('small');
      name.textContent = app.name; hint.textContent = (app.running ? '运行中' : '已保存 / 待运行') + (app.suggested === 'work' ? ' · 建议代理' : app.suggested === 'game' ? ' · 建议直连' : '');
      description.append(name, hint);
      const select = document.createElement('select'); select.setAttribute('aria-label', app.name + '连接方式');
      for (const [value, label] of [['ignore', '不加入规则'], ['work', '使用代理'], ['game', '使用直连']]) { const option = document.createElement('option'); option.value = value; option.textContent = label; select.append(option); }
      select.value = work.includes(app.processName.toLowerCase()) ? 'work' : games.includes(app.processName.toLowerCase()) ? 'game' : 'ignore';
      select.addEventListener('change', () => {
        for (const [id, category] of [['proxy-work','work'], ['proxy-games','game']]) {
          const entries = lines(id).filter(value => value.replace(/\.exe$/i, '').toLowerCase() !== app.processName.toLowerCase());
          if (select.value === category) entries.push(app.processName);
          $(id).value = entries.join('\n');
        }
        markDirty();
      });
      row.append(description, select); holder.append(row);
    }
    if (!applications.length) { const empty = document.createElement('p'); empty.className = 'muted'; empty.textContent = '暂无候选应用。打开目标应用后重新检测，或手动添加。'; holder.append(empty); }
  }
  function syncConnectionChoices() {
    for (const button of $('proxy-connections').querySelectorAll('button')) {
      const connection = button._connection;
      const selected = connection && [['proxy-core-type', 'coreType'], ['proxy-url', 'proxyUrl'], ['proxy-core', 'xrayPath'], ['proxy-core-config', 'xrayConfig']]
        .every(([id, key]) => $(id).value === (connection[key] || ''));
      button.classList.toggle('selected', !!selected);
      button.setAttribute('aria-pressed', String(!!selected));
    }
  }
  function renderDetection(data) {
    detecting = false; $('proxy-detect').disabled = false;
    if (data.error) { $('proxy-detect-status').textContent = data.error; return; }
    const holder = $('proxy-connections'); holder.replaceChildren();
    const connections = data.connections || [];
    for (const connection of connections) {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'network-connection-choice secondary';
      button._connection = connection;
      const name = document.createElement('strong'), detail = document.createElement('small'); name.textContent = connection.name;
      detail.textContent = connection.proxyUrl + (connection.ready ? ' · 端口就绪' : ' · 启用时启动'); button.append(name, detail);
      button.title = connection.xrayConfig; button.classList.toggle('selected', connection.xrayConfig === $('proxy-core-config').value);
      button.addEventListener('click', () => {
        $('proxy-core-type').value = connection.coreType; $('proxy-core').value = connection.xrayPath || ''; $('proxy-core-config').value = connection.xrayConfig || ''; $('proxy-url').value = connection.proxyUrl;
        window.FaxUi?.refresh($('proxy-core-type'));
        markDirty();
      }); holder.append(button);
    }
    syncConnectionChoices();
    applications = data.applications || []; renderApplications();
    $('proxy-detect-status').textContent = `检测到 ${connections.length} 个连接、${applications.length} 个候选应用。选择后点击保存配置。`;
  }
  setupQuickSettings();
  const lines = id => $(id).value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
  const feedback = (text, error = false) => { $('proxy-feedback').textContent = text; $('proxy-feedback').classList.toggle('error', error); };
  function setBusy(value) {
    busy = value;
    for (const id of ['proxy-start', 'proxy-stop', 'proxy-on', 'proxy-off', 'proxy-startup', 'proxy-refresh', 'proxy-validate', 'proxy-install', 'proxy-restore-xray']) $(id).disabled = value;
    if (!value && state) {
      $('proxy-start').disabled = state.running;
      $('proxy-on').disabled = $('proxy-off').disabled = state.running;
    }
    $('proxy-fields').disabled = value || !config || !!state?.running || !!state?.coreRunning;
    syncFormState();
  }
  function fill(value) {
    config = value;
    $('proxy-url').value = value.proxyUrl || '';
    $('proxy-core-type').value = value.coreType || 'xray';
    $('proxy-install').textContent = value.coreType === 'mihomo' ? '检查已安装的 Mihomo' : '安装并导入现有节点';
    $('proxy-core').value = value.xrayPath || '';
    $('proxy-core-config').value = value.xrayConfig || '';
    for (const [id, key] of [['proxy-work','workProcesses'], ['proxy-games','gameProcesses'], ['proxy-game-paths','gamePathHints'], ['proxy-excludes','gamePathExcludes']]) $(id).value = (value[key] || []).join('\n');
    $('proxy-priority').value = value.priority || 'game'; $('proxy-idle').value = value.idleBehavior || 'keep';
    for (const id of ['proxy-core-type', 'proxy-priority', 'proxy-idle']) window.FaxUi?.refresh($(id));
    $('proxy-poll').value = value.pollSeconds || 3; $('proxy-stable').value = value.stableSeconds ?? 6;
    $('proxy-system-check').checked = value.setSystemProxy !== false;
    $('proxy-env-check').checked = value.setEnvironmentProxy !== false;
    dirty = false;
    savedDraft = JSON.stringify(read()); validationAttempted = false; clearFieldErrors();
    syncConnectionChoices();
    renderApplications();
    if (!detectedOnce) { detectedOnce = true; detect(); }
  }
  function read() {
    return {...config, coreType: $('proxy-core-type').value, proxyUrl: $('proxy-url').value.trim(), xrayPath: $('proxy-core').value.trim(), xrayConfig: $('proxy-core-config').value.trim(),
      workProcesses: lines('proxy-work'), gameProcesses: lines('proxy-games'), gamePathHints: lines('proxy-game-paths'), gamePathExcludes: lines('proxy-excludes'),
      priority: $('proxy-priority').value, idleBehavior: $('proxy-idle').value, pollSeconds: Number($('proxy-poll').value), stableSeconds: Number($('proxy-stable').value),
      setSystemProxy: $('proxy-system-check').checked, setEnvironmentProxy: $('proxy-env-check').checked};
  }
  function render(data) {
    // A background refresh may finish after Save was clicked. Only the host's
    // echo of this submitted draft can acknowledge it; keep waiting otherwise.
    if (awaitingSave && data.ok && (!data.config || !Object.entries(savingConfig || {}).every(
      ([key, value]) => JSON.stringify(data.config[key]) === JSON.stringify(value)))) return;
    setBusy(false);
    if (!data.ok) {
      if (awaitingSave) { formMessage = (data.error || '保存失败。') + ' 输入已保留，可以修改后重试。'; formError = true; }
      awaitingSave = false; savingConfig = null; feedback(data.error || '操作失败，请检查日志。', true); syncFormState(); return;
    }
    const saved = awaitingSave;
    if (data.config && (!config || awaitingSave || !dirty)) fill(data.config);
    awaitingSave = false;
    savingConfig = null;
    if (saved) { formMessage = '配置已保存。'; formError = false; }
    syncFormState();
    if (data.detail) feedback(data.detail);
    else if (saved) feedback('配置已保存。');
    if (!data.status) { setBusy(false); return; }
    state = data.status;
    $('proxy-running').textContent = state.running ? '自动切换运行中' : '自动切换已停止';
    $('proxy-indicator').classList.toggle('running', state.running);
    $('proxy-startup-state').textContent = state.startup ? '开机启动已启用' : '开机启动关闭';
    $('proxy-mode').textContent = state.proxyEnabled ? '当前使用系统代理' : '当前使用直连';
    $('proxy-reason').textContent = state.running ? (state.state?.waiting ? '等待应用状态稳定后切换…' : state.state?.reason || '等待识别应用…') : '按配置规则自动选择连接，停止时还原启用前的设置。';
    $('proxy-system').textContent = state.proxyEnabled ? state.proxyServer || '未设置地址' : '已关闭';
    $('proxy-port').textContent = state.portReady ? '已就绪' : '未监听';
    $('proxy-port').classList.toggle('error', !state.portReady);
    $('proxy-install-note').textContent = config?.coreType === 'mihomo' ? '已准备 Mihomo 配置 · 启用代理后生效 · 可恢复 Xray' : '安装只准备内核与配置；点击启用代理后生效。';
    $('proxy-matches').textContent = state.running ? [...(state.state?.work || []), ...(state.state?.games || [])].join('、') || '暂无' : '—';
    $('proxy-legacy').hidden = !state.legacyRunning;
    $('proxy-startup').textContent = state.startup ? '关闭开机启动' : '启用开机启动';
    const logText = (state.logs || []).join('\n') || '暂无记录';
    if ($('proxy-log').textContent !== logText) $('proxy-log').textContent = logText;
    if (state.running && state.state?.error) feedback(state.state.error, true);
    setBusy(false);
  }
  function confirm(command, title, description) {
    if (busy || !config) return;
    if (dirty && ['proxy.start','proxy.on','proxy.off','proxy.startup.on', 'proxy.install', 'proxy.restore-xray'].includes(command)) { feedback('请先保存配置。', true); return; }
    pending = command;
    $('proxy-confirm-title').textContent = title;
    $('proxy-confirm-text').textContent = description;
    const legacy = ['proxy.start','proxy.startup.on'].includes(command) && !!state?.legacyRunning;
    $('proxy-takeover-row').hidden = !legacy; $('proxy-takeover').checked = legacy;
    window.FaxUi?.inherit($('view-proxy'),$('proxy-confirm'));
    $('proxy-confirm-ok').textContent=title;
    $('proxy-confirm').showModal();
    $('proxy-confirm-cancel').focus();
  }
  $('proxy-install').addEventListener('click', () => confirm('proxy.install', '安装 Mihomo 并导入节点', '校验官方 Windows 内核，转换当前 Xray 节点与分流规则，并保留回退配置。使用独立的本机端口 10819。安装不修改当前系统代理；点击启用代理后生效。'));
  $('proxy-restore-xray').addEventListener('click', () => confirm('proxy.restore-xray', '恢复 Xray 配置', '恢复导入前的 Xray 路径、节点与端口配置。当前系统代理保持原状。'));
  $('proxy-start').addEventListener('click', () => confirm('proxy.start', '启动自动切换', '根据工作与游戏规则更改系统代理和用户代理环境变量。后台监控会在关闭工具箱后继续运行。停止时还原原设置。'));
  $('proxy-stop').addEventListener('click', () => confirm('proxy.stop', '停止并还原', '停止网络管家，恢复启用前的代理设置。只停止由本模块启动的代理内核；其他程序启动的内核保持运行。'));
  $('proxy-on').addEventListener('click', () => confirm('proxy.on', '启用代理', '验证本机监听端口，必要时启动所选内核，再按配置启用代理。'));
  $('proxy-off').addEventListener('click', () => confirm('proxy.off', '切换直连', '按配置关闭系统代理、清除用户代理环境变量，并停止本模块启动的内核。可能中断正在使用代理的连接。'));
  $('proxy-startup').addEventListener('click', () => confirm(state?.startup ? 'proxy.startup.off' : 'proxy.startup.on', state?.startup ? '关闭开机启动' : '启用开机启动', state?.startup ? '移除网络管家的专用启动项；当前监控继续运行。' : '添加网络管家的专用启动项。下次登录 Windows 后后台监控自动运行；如旧版自动切换仍存在，将接管并备份撤下它的启动项。'));
  $('proxy-confirm-ok').addEventListener('click', () => {
    if (!pending) return;
    if (!$('proxy-takeover-row').hidden && !$('proxy-takeover').checked) { feedback('请确认接管旧脚本，或先停止旧版自动切换。', true); return; }
    const command = pending; pending = null;
    $('proxy-confirm').close(); setBusy(true); feedback('正在处理…'); send(command, {takeOverLegacy: $('proxy-takeover').checked});
  });
  $('proxy-confirm-cancel').addEventListener('click', () => { pending = null; $('proxy-confirm').close(); });
  $('proxy-confirm').addEventListener('cancel', () => { pending = null; });
  $('proxy-config').addEventListener('change',markDirty);
  $('proxy-config').addEventListener('input', markDirty);
  $('proxy-config').addEventListener('submit', event => {
    event.preventDefault(); if (busy || !config || state?.running || state?.coreRunning) return;
    if (!validateFields()) { formMessage = '请修改标出的设置后再保存。'; formError = true; syncFormState(); return; }
    savingConfig = read(); awaitingSave = true; setBusy(true); feedback('正在保存…'); send('proxy.save', {config: savingConfig});
  });
  $('proxy-discard').addEventListener('click', async () => {
    if (!dirty || busy || !config) return;
    if (!await window.FaxToolboxUi.confirm({title: '放弃未保存的修改？', message: '连接地址、应用规则和高级设置将恢复到上次保存的内容。', confirmLabel: '放弃修改'})) return;
    if (busy) return;
    fill(config); formMessage = '已恢复上次保存的配置。'; formError = false; syncFormState();
  });
  $('proxy-validate').addEventListener('click', () => { if (dirty) { feedback('请先保存配置再检查连接。', true); return; } setBusy(true); feedback('正在检查…'); send('proxy.validate'); });
  $('proxy-refresh').addEventListener('click', () => { setBusy(true); send('proxy.get'); });
  for (const button of document.querySelectorAll('[data-proxy-pick]')) button.addEventListener('click', () => send('proxy.pick', {field: button.dataset.proxyPick}));
  window.chrome?.webview?.addEventListener('message', event => {
    const message = event.data;
    if (message.kind === 'proxy.detected') renderDetection(message);
    if (message.kind === 'proxy.status') render(message.data || {});
    if (message.kind === 'proxy.picked' && message.field === 'application') {
      const name = (message.path || '').split(/[\\/]/).pop().replace(/\.exe$/i, '');
      if (!applications.some(app => app.processName.toLowerCase() === name.toLowerCase())) applications.push({name, processName: name, suggested: 'ignore', running: false});
      renderApplications(); return;
    }
    if (message.kind === 'proxy.picked') { const id = message.field === 'xrayPath' ? 'proxy-core' : 'proxy-core-config'; $(id).value = message.path || ''; markDirty(); }
  });
  setBusy(false);
  setInterval(() => { if (!$('view-proxy').hidden && !busy && !$('proxy-confirm').open) send('proxy.get'); }, 10000);
})();
