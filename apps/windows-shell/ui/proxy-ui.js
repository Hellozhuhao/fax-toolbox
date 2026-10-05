(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let config = null, state = null, dirty = false, busy = false, pending = null, awaitingSave = false;
  let applications = [], detectedOnce = false, detecting = false;
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
    fields.insertBefore(quick, advanced);
    $('proxy-detect').addEventListener('click', detect);
    $('proxy-add-app').addEventListener('click', () => send('proxy.pick', {field: 'application'}));
  }
  function detect() {
    if (detecting) return;
    detecting = true; $('proxy-detect').disabled = true; $('proxy-detect-status').textContent = '正在检测本机连接和运行中的应用…';
    send('proxy.detect');
  }
  function markDirty() { dirty = true; $('proxy-config-note').textContent = '有未保存的更改'; }
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
  function renderDetection(data) {
    detecting = false; $('proxy-detect').disabled = false;
    if (data.error) { $('proxy-detect-status').textContent = data.error; return; }
    const holder = $('proxy-connections'); holder.replaceChildren();
    const connections = data.connections || [];
    for (const connection of connections) {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'network-connection-choice secondary';
      const name = document.createElement('strong'), detail = document.createElement('small'); name.textContent = connection.name;
      detail.textContent = connection.proxyUrl + (connection.ready ? ' · 端口就绪' : ' · 启用时启动'); button.append(name, detail);
      button.title = connection.xrayConfig; button.classList.toggle('selected', connection.xrayConfig === $('proxy-core-config').value);
      button.addEventListener('click', () => {
        $('proxy-core-type').value = connection.coreType; $('proxy-core').value = connection.xrayPath; $('proxy-core-config').value = connection.xrayConfig; $('proxy-url').value = connection.proxyUrl;
        holder.querySelectorAll('button').forEach(item => item.classList.toggle('selected', item === button)); markDirty();
      }); holder.append(button);
    }
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
    $('proxy-fields').disabled = value || !!state?.running || !!state?.coreRunning;
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
    $('proxy-poll').value = value.pollSeconds || 3; $('proxy-stable').value = value.stableSeconds ?? 6;
    $('proxy-system-check').checked = value.setSystemProxy !== false;
    $('proxy-env-check').checked = value.setEnvironmentProxy !== false;
    dirty = false;
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
    setBusy(false);
    if (!data.ok) { awaitingSave = false; feedback(data.error || '操作失败，请检查日志。', true); return; }
    const saved = awaitingSave;
    if (data.config && (!config || awaitingSave || !dirty)) fill(data.config);
    awaitingSave = false;
    if (data.detail) feedback(data.detail);
    else if (saved) feedback('配置已保存。');
    if (!data.status) return;
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
    $('proxy-config-note').textContent = state.running || state.coreRunning ? '停止后可修改配置' : dirty ? '有未保存的更改' : '配置保存在本机';
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
    $('proxy-confirm').showModal();
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
  $('proxy-config').addEventListener('input', () => { dirty = true; $('proxy-config-note').textContent = '有未保存的更改'; });
  $('proxy-config').addEventListener('submit', event => { event.preventDefault(); if (busy) return; awaitingSave = true; setBusy(true); feedback('正在保存…'); send('proxy.save', {config: read()}); });
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
    if (message.kind === 'proxy.picked') { const id = message.field === 'xrayPath' ? 'proxy-core' : 'proxy-core-config'; $(id).value = message.path || ''; dirty = true; $('proxy-config-note').textContent = '有未保存的更改'; }
  });
  setInterval(() => { if (!$('view-proxy').hidden && !busy && !$('proxy-confirm').open) send('proxy.get'); }, 10000);
})();
