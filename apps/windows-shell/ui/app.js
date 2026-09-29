(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let courseState = 'WAITING_LOGIN';
  let courseReady = false;
  let courseOpened = false;
  let catalogEntries = [];
  let catalogLive = false;
  let currentChapterId = '';
  let currentChapterName = '';
  let suggestedChapterId = '';
  let activeModule = null;
  const activity = [];
  const stateNames = {STARTING: '启动中', READY: '就绪', CREATED: '正在创建', OBSERVING: '观察中', RUNNING: '运行中', PAUSED: '已暂停', STOPPED: '已停止', COMPLETE: '已完成', CANCELLED: '已取消', WAITING_LOGIN: '等待登录', CHOOSING_COURSE: '选择课程', NEEDS_INPUT: '等待人工操作', ERROR: '发生错误'};
  const stateName = state => stateNames[state] || state || '未知';
  function send(command, extra = {}) { window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra})); }
  function show(view) {
    for (const name of ['home', 'modules', 'workspace', 'demo', 'course', 'activity', 'settings', 'vault']) $('view-' + name).hidden = name !== view;
    const navigation = ['course', 'demo'].includes(view) ? 'workspace' : view === 'vault' ? 'settings' : view;
    for (const item of document.querySelectorAll('.sidebar [data-view]')) item.classList.toggle('active', item.dataset.view === navigation);
    send('ui.layout', {mode: ['course', 'demo'].includes(view) ? 'module' : 'home'});
    if (view === 'vault') send('vault.list');
  }
  function showWorkspace() { show(activeModule || 'workspace'); }
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
  function openCourse() {
    activeModule = 'course'; $('workspace-module').textContent = '学习通刷课';
    $('workspace-detail').textContent = '点击左侧工作台可随时回到课程控制页。';
    show('course');
    if (!courseOpened) { courseOpened = true; send('course.open'); }
  }
  function openDemo() {
    activeModule = 'demo'; $('workspace-module').textContent = '页面事件演示';
    $('workspace-detail').textContent = '点击左侧工作台可随时回到演示控制页。';
    show('demo');
  }
  function setTheme(theme) {
    const value = theme === 'light' ? 'light' : 'dark';
    document.documentElement.dataset.theme = value;
    for (const item of document.querySelectorAll('[data-theme]')) item.classList.toggle('selected', item.dataset.theme === value);
    try { localStorage.setItem('fax-toolbox-theme', value); } catch (_) { /* UI remains usable without storage. */ }
    send('ui.theme', {theme: value});
  }
  function renderCourse(message) {
    courseState = message.state || courseState; courseReady = !!message.ready;
    if (courseState === 'ERROR') courseOpened = false;
    $('course-state').textContent = stateName(courseState); $('course-detail').textContent = message.detail || '';
    $('home-course').textContent = stateName(courseState);
    logActivity('学习通刷课', stateName(courseState), message.detail || '状态已更新');
    if (message.url) $('course-url').textContent = message.url;
    if (message.chapter !== undefined && message.chapter !== currentChapterId) {
      currentChapterId = message.chapter || ''; currentChapterName = '';
      updateChapter();
    }
    $('course-start').disabled = !courseReady || !['READY', 'PAUSED', 'NEEDS_INPUT'].includes(courseState);
    $('course-start').textContent = ['PAUSED', 'NEEDS_INPUT'].includes(courseState) ? '继续运行' : '开始自动完成';
    $('course-pause').disabled = courseState !== 'RUNNING';
    $('course-stop').disabled = !['RUNNING', 'PAUSED', 'NEEDS_INPUT', 'ERROR'].includes(courseState);
  }
  function updateChapter() {
    const match = catalogEntries.find(entry => entry.id === currentChapterId);
    $('course-chapter').textContent = currentChapterName || match?.name || '章节名称待识别';
    $('course-chapter-id').textContent = currentChapterId ? '章节 ID：' + currentChapterId : '';
    const field = $('answers-chapter');
    if (!field.value.trim() || field.value === suggestedChapterId) field.value = currentChapterId;
    suggestedChapterId = currentChapterId;
    if (currentChapterId && catalogEntries.some(entry => entry.id === currentChapterId))
      $('course-chapters').value = currentChapterId;
  }
  function renderCatalog(message) {
    catalogEntries = Array.isArray(message.entries) ? message.entries : [];
    catalogLive = !!message.live;
    const previousChapterId = currentChapterId;
    currentChapterId = message.currentId || '';
    currentChapterName = message.currentName || '';
    const selected = $('course-chapters').value;
    const menu = $('course-chapters'); menu.replaceChildren();
    const prompt = document.createElement('option'); prompt.value = '';
    prompt.textContent = catalogEntries.length ? '选择一个章节' : '当前页面没有可读取的章节目录';
    menu.append(prompt);
    for (const entry of catalogEntries) {
      const option = document.createElement('option'); option.value = entry.id; option.textContent = entry.name;
      menu.append(option);
    }
    const currentEntry = catalogEntries.find(entry => entry.id === currentChapterId || entry.name === currentChapterName);
    menu.value = currentChapterId !== previousChapterId && catalogEntries.some(entry => entry.id === currentChapterId) ? currentChapterId :
      catalogEntries.some(entry => entry.id === selected) ? selected :
      currentEntry?.id || '';
    $('course-jump').disabled = !catalogLive || !menu.value;
    $('course-catalog-note').textContent = message.note || (catalogLive ?
      `已读取 ${catalogEntries.length} 个章节并保存到本机。` :
      catalogEntries.length ? '已载入本机目录；等待网页目录出现后可跳转。' : '尚未识别到章节目录。');
    updateChapter();
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
      $('home-backend').textContent = stateName(message.state);
      $('home-detail').textContent = message.detail || '';
      $('demo-state').textContent = stateName(message.state);
      $('demo-detail').textContent = message.detail || '';
      logActivity('工具箱', stateName(message.state), message.detail || '');
    }
    if (message.kind === 'course.status') renderCourse(message);
    if (message.kind === 'course.catalog') renderCatalog(message);
    if (message.kind === 'vault.list') renderVault(message.entries || []);
    if (message.kind === 'answers.status') $('answers-result').textContent = message.detail || '';
  });
  for (const item of document.querySelectorAll('[data-view]')) item.addEventListener('click', () => item.dataset.view === 'workspace' ? showWorkspace() : show(item.dataset.view));
  $('open-course-card').addEventListener('click', openCourse);
  for (const item of document.querySelectorAll('[data-open]')) item.addEventListener('click', () => item.dataset.open === 'course' ? openCourse() : openDemo());
  $('module-course').addEventListener('click', openCourse);
  $('module-demo').addEventListener('click', openDemo);
  $('workspace-modules').addEventListener('click', () => show('modules'));
  $('start').addEventListener('click', () => { openDemo(); send('session.start'); });
  $('cancel').addEventListener('click', () => send('session.cancel'));
  $('course-open').addEventListener('click', () => { courseOpened = true; send('course.open'); });
  $('course-save-vault').addEventListener('click', () => send('vault.save'));
  $('course-start').addEventListener('click', () => send('course.start'));
  $('course-chapters').addEventListener('change', () => { $('course-jump').disabled = !catalogLive || !$('course-chapters').value; });
  $('course-jump').addEventListener('click', () => send('course.jump', {chapterId: $('course-chapters').value}));
  $('course-pause').addEventListener('click', () => send('course.pause'));
  $('course-stop').addEventListener('click', () => send('course.stop'));
  $('answers-save').addEventListener('click', () => send('answers.save', {chapter: $('answers-chapter').value.trim(), answers: $('answers-text').value}));
  $('vault-add').addEventListener('click', () => send('vault.save'));
  $('settings-vault').addEventListener('click', () => show('vault'));
  for (const item of document.querySelectorAll('[data-theme]')) item.addEventListener('click', () => setTheme(item.dataset.theme));
  let savedTheme = 'light'; try { savedTheme = localStorage.getItem('fax-toolbox-theme') || 'light'; } catch (_) { /* Use light default. */ }
  setTheme(savedTheme);
  show('home');
})();
