(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let config = null, state = null, dirty = false, busy = false, pending = null, awaitingSave = false;
  const send = (command, extra = {}) => window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra}));
  const lines = id => $(id).value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
  const feedback = (text, error = false) => { $('proxy-feedback').textContent = text; $('proxy-feedback').classList.toggle('error', error); };
  function setBusy(value) {
    busy = value;
    for (const id of ['proxy-start', 'proxy-stop', 'proxy-on', 'proxy-off', 'proxy-startup', 'proxy-refresh', 'proxy-validate']) $(id).disabled = value;
    if (!value && state) {
      $('proxy-start').disabled = state.running;
      $('proxy-on').disabled = $('proxy-off').disabled = state.running;
    }
    $('proxy-fields').disabled = value || !!state?.running;
  }
  function fill(value) {
    config = value;
    $('proxy-url').value = value.proxyUrl || '';
    $('proxy-core').value = value.xrayPath || '';
    $('proxy-core-config').value = value.xrayConfig || '';
    for (const [id, key] of [['proxy-work','workProcesses'], ['proxy-games','gameProcesses'], ['proxy-game-paths','gamePathHints'], ['proxy-excludes','gamePathExcludes']]) $(id).value = (value[key] || []).join('\n');
    $('proxy-priority').value = value.priority || 'game'; $('proxy-idle').value = value.idleBehavior || 'keep';
    $('proxy-poll').value = value.pollSeconds || 3; $('proxy-stable').value = value.stableSeconds ?? 6;
    $('proxy-system-check').checked = value.setSystemProxy !== false;
    $('proxy-env-check').checked = value.setEnvironmentProxy !== false;
    dirty = false;
  }
  function read() {
    return {...config, proxyUrl: $('proxy-url').value.trim(), xrayPath: $('proxy-core').value.trim(), xrayConfig: $('proxy-core-config').value.trim(),
      workProcesses: lines('proxy-work'), gameProcesses: lines('proxy-games'), gamePathHints: lines('proxy-game-paths'), gamePathExcludes: lines('proxy-excludes'),
      priority: $('proxy-priority').value, idleBehavior: $('proxy-idle').value, pollSeconds: Number($('proxy-poll').value), stableSeconds: Number($('proxy-stable').value),
      setSystemProxy: $('proxy-system-check').checked, setEnvironmentProxy: $('proxy-env-check').checked};
  }
  function render(data) {
    setBusy(false);
    if (!data.ok) { awaitingSave = false; feedback(data.error || '操作失败，请检查日志。', true); return; }
    if (data.config && (!config || awaitingSave || !dirty)) fill(data.config);
    awaitingSave = false;
    if (data.detail) feedback(data.detail);
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
    $('proxy-matches').textContent = state.running ? [...(state.state?.work || []), ...(state.state?.games || [])].join('、') || '暂无' : '—';
    $('proxy-legacy').hidden = !state.legacyRunning;
    $('proxy-startup').textContent = state.startup ? '关闭开机启动' : '启用开机启动';
    $('proxy-config-note').textContent = state.running ? '停止后可修改配置' : dirty ? '有未保存的更改' : '配置自动保存在本机';
    const logText = (state.logs || []).join('\n') || '暂无记录';
    if ($('proxy-log').textContent !== logText) $('proxy-log').textContent = logText;
    if (state.running && state.state?.error) feedback(state.state.error, true);
    setBusy(false);
  }
  function confirm(command, title, description) {
    if (busy || !config) return;
    if (dirty && ['proxy.start','proxy.on','proxy.off','proxy.startup.on'].includes(command)) { feedback('请先保存配置。', true); return; }
    pending = command;
    $('proxy-confirm-title').textContent = title;
    $('proxy-confirm-text').textContent = description;
    const legacy = ['proxy.start','proxy.startup.on'].includes(command) && !!state?.legacyRunning;
    $('proxy-takeover-row').hidden = !legacy; $('proxy-takeover').checked = legacy;
    $('proxy-confirm').showModal();
  }
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
    if (message.kind === 'proxy.status') render(message.data || {});
    if (message.kind === 'proxy.picked') { const id = message.field === 'xrayPath' ? 'proxy-core' : 'proxy-core-config'; $(id).value = message.path || ''; dirty = true; $('proxy-config-note').textContent = '有未保存的更改'; }
  });
  setInterval(() => { if (!$('view-proxy').hidden && !busy && !$('proxy-confirm').open) send('proxy.get'); }, 10000);
})();
