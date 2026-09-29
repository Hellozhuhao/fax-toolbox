(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let courseState = 'WAITING_LOGIN';
  let courseReady = false;
  let courseOpened = false;
  const activity = [];
  function send(command, extra = {}) { window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra})); }
  function show(view) {
    for (const name of ['home', 'modules', 'demo', 'course', 'activity', 'settings', 'vault']) $('view-' + name).hidden = name !== view;
    for (const item of document.querySelectorAll('[data-view]')) item.classList.toggle('active', item.dataset.view === view);
    send('ui.layout', {mode: ['course', 'demo'].includes(view) ? 'module' : 'home'});
    if (view === 'vault') send('vault.list');
  }
  function logActivity(source, title, detail) {
    const time = new Date().toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit'});
    if (activity[0]?.title === title && activity[0]?.detail === detail) return;
    activity.unshift({source, title, detail, time}); activity.length = Math.min(activity.length, 20);
    const list = $('activity-list'); list.replaceChildren();
    for (const item of activity) {
      const row = document.createElement('div'); row.className = 'activity-row';
      const heading = document.createElement('strong'); heading.textContent = item.title;
      const meta = document.createElement('span'); meta.textContent = item.source + ' · ' + item.time;
      const note = document.createElement('p'); note.textContent = item.detail;
      row.append(heading, meta, note); list.append(row);
    }
  }
  function openCourse() { show('course'); if (!courseOpened) { courseOpened = true; send('course.open'); } }
  function openDemo() { show('demo'); send('session.start'); }
  function setTheme(theme) {
    const value = theme === 'light' ? 'light' : 'dark';
    document.documentElement.dataset.theme = value;
    for (const item of document.querySelectorAll('[data-theme]')) item.classList.toggle('selected', item.dataset.theme === value);
    try { localStorage.setItem('fax-toolbox-theme', value); } catch (_) { /* UI remains usable without storage. */ }
  }
  function renderCourse(message) {
    courseState = message.state || courseState; courseReady = !!message.ready;
    $('course-state').textContent = courseState; $('course-detail').textContent = message.detail || '';
    $('home-course').textContent = courseState;
    logActivity('学习通刷课', courseState, message.detail || '状态已更新');
    if (message.url) $('course-url').textContent = message.url;
    if (message.chapter) { $('course-chapter').textContent = message.chapter; if (!$('answers-chapter').value) $('answers-chapter').value = message.chapter; }
    $('course-start').disabled = !courseReady || !['READY', 'PAUSED', 'NEEDS_INPUT'].includes(courseState);
    $('course-pause').disabled = courseState !== 'RUNNING';
    $('course-stop').disabled = !['RUNNING', 'PAUSED', 'NEEDS_INPUT'].includes(courseState);
  }
  function renderVault(entries) {
    const list = $('vault-list'); list.replaceChildren();
    if (!entries.length) { list.textContent = '暂无保存的账号'; return; }
    for (const entry of entries) {
      const row = document.createElement('div'); row.className = 'vault-row';
      const label = document.createElement('span'); label.textContent = entry.username;
      const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = '删除';
      remove.addEventListener('click', () => { if (confirm('删除这个账号？')) send('vault.delete', {entryId: entry.id}); });
      row.append(label, remove); list.append(row);
    }
  }
  window.chrome?.webview?.addEventListener('message', event => {
    const message = event.data; if (!message || message.v !== 1) return;
    if (message.kind === 'status') {
      $('state').textContent = message.state || ''; $('detail').textContent = message.detail || '';
      $('home-backend').textContent = message.state === 'READY' ? '已连接' : (message.state || '未知');
      $('home-detail').textContent = message.detail || '';
      logActivity('工具箱', message.state || '状态更新', message.detail || '');
    }
    if (message.kind === 'course.status') renderCourse(message);
    if (message.kind === 'vault.list') renderVault(message.entries || []);
    if (message.kind === 'answers.status') $('answers-result').textContent = message.detail || '';
  });
  for (const item of document.querySelectorAll('[data-view]')) item.addEventListener('click', () => item.dataset.view === 'course' ? openCourse() : show(item.dataset.view));
  $('open-course-card').addEventListener('click', openCourse);
  for (const item of document.querySelectorAll('[data-open]')) item.addEventListener('click', () => item.dataset.open === 'course' ? openCourse() : openDemo());
  $('module-course').addEventListener('click', openCourse);
  $('module-demo').addEventListener('click', openDemo);
  $('start').addEventListener('click', () => { show('demo'); send('session.start'); });
  $('cancel').addEventListener('click', () => send('session.cancel'));
  $('course-open').addEventListener('click', () => { courseOpened = true; send('course.open'); });
  $('course-save-vault').addEventListener('click', () => send('vault.save'));
  $('course-start').addEventListener('click', () => send('course.start'));
  $('course-pause').addEventListener('click', () => send('course.pause'));
  $('course-stop').addEventListener('click', () => send('course.stop'));
  $('answers-save').addEventListener('click', () => send('answers.save', {chapter: $('answers-chapter').value.trim(), answers: $('answers-text').value}));
  $('vault-add').addEventListener('click', () => send('vault.save'));
  $('settings-vault').addEventListener('click', () => show('vault'));
  for (const item of document.querySelectorAll('[data-theme]')) item.addEventListener('click', () => setTheme(item.dataset.theme));
  let savedTheme = 'dark'; try { savedTheme = localStorage.getItem('fax-toolbox-theme') || 'dark'; } catch (_) { /* Use dark default. */ }
  setTheme(savedTheme);
  show('home');
})();
