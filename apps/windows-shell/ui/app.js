(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let courseState = 'WAITING_LOGIN';
  let courseReady = false;
  function send(command, extra = {}) { window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra})); }
  function show(view) {
    for (const name of ['home', 'course', 'vault']) $('view-' + name).hidden = name !== view;
    for (const item of document.querySelectorAll('[data-view]')) item.classList.toggle('active', item.dataset.view === view);
    if (view === 'vault') send('vault.list');
  }
  function renderCourse(message) {
    courseState = message.state || courseState; courseReady = !!message.ready;
    $('course-state').textContent = courseState; $('course-detail').textContent = message.detail || '';
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
    if (message.kind === 'status') { $('state').textContent = message.state || ''; $('detail').textContent = message.detail || ''; }
    if (message.kind === 'course.status') renderCourse(message);
    if (message.kind === 'vault.list') renderVault(message.entries || []);
    if (message.kind === 'answers.status') $('answers-result').textContent = message.detail || '';
  });
  for (const item of document.querySelectorAll('[data-view]')) item.addEventListener('click', () => show(item.dataset.view));
  $('open-course-card').addEventListener('click', () => { show('course'); send('course.open'); });
  $('start').addEventListener('click', () => send('session.start'));
  $('cancel').addEventListener('click', () => send('session.cancel'));
  $('course-open').addEventListener('click', () => send('course.open'));
  $('course-save-vault').addEventListener('click', () => send('vault.save'));
  $('course-start').addEventListener('click', () => send('course.start'));
  $('course-pause').addEventListener('click', () => send('course.pause'));
  $('course-stop').addEventListener('click', () => send('course.stop'));
  $('answers-save').addEventListener('click', () => send('answers.save', {chapter: $('answers-chapter').value.trim(), answers: $('answers-text').value}));
  $('vault-add').addEventListener('click', () => send('vault.save'));
  show('home');
})();
