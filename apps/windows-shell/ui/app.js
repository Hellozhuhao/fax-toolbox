(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  let courseState = 'WAITING_LOGIN';
  let courseReady = false;
  let catalogEntries = [];
  let catalogLive = false;
  let currentChapterId = '';
  let currentChapterName = '';
  let suggestedChapterId = '';
  let activeModule = null;
  let homeworkBrowsing = false;
  let balanceAvailable = false;
  let balanceLoading = false;
  const viewHistory = [];
  let currentView = '';
  const activity = [];
  let activityFilter = 'all';
  let developerMode = false;
  const stateNames = {STARTING: '启动中', READY: '就绪', CREATED: '正在创建', OBSERVING: '观察中', RUNNING: '运行中', PAUSED: '已暂停', STOPPED: '已停止', COMPLETE: '已完成', CANCELLED: '已取消', WAITING_LOGIN: '等待登录', CHOOSING_COURSE: '选择课程', NEEDS_INPUT: '等待人工操作', ERROR: '发生错误'};
  const viewNames = {home: '首页', modules: '工具', workspace: '工作台', demo: '页面事件演示', course: '学习通', homework: '作业校对', ai: 'AI 服务', activity: '状态', settings: '设置', vault: '密码箱'};
  const stateName = state => stateNames[state] || state || '未知';
  function send(command, extra = {}) { window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra})); }
  function show(view, record = true) {
    if (record && currentView && currentView !== view) viewHistory.push(currentView);
    currentView = view;
    for (const name of ['home', 'modules', 'workspace', 'demo', 'course', 'homework', 'ai', 'activity', 'settings', 'vault']) $('view-' + name).hidden = name !== view;
    const navigation = ['course', 'homework', 'demo', 'ai', 'workspace'].includes(view) ? 'modules' : view === 'vault' ? 'settings' : view;
    for (const item of document.querySelectorAll('.sidebar .nav-item[data-view]')) item.classList.toggle('active', item.dataset.view === navigation);
    for (const item of document.querySelectorAll('.nav-flyout [data-view]')) item.classList.toggle('active', item.dataset.view === view);
    $('location-name').textContent = viewNames[view];
    send('ui.layout', {mode: view === 'homework' ?
      (homeworkBrowsing ? 'homework-browser' : 'homework-results') :
      ['course', 'demo'].includes(view) ? 'module' : 'home', view, canGoBack: viewHistory.length > 0});
    if (view === 'vault') send('vault.list');
    if (view === 'activity') renderActivity();
  }
  function showWorkspace() {
    if (activeModule === 'course') openCourse();
    else if (activeModule === 'homework') openHomework();
    else if (activeModule === 'demo') openDemo();
    else if (activeModule === 'ai') openAi();
    else show('workspace');
  }
  function logActivity(source, title, detail) {
    const time = new Date().toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit', second: '2-digit'});
    if (activity[0]?.title === title && activity[0]?.detail === detail) return;
    activity.unshift({source, title, detail, time}); activity.length = Math.min(activity.length, 200);
    if (currentView === 'activity') renderActivity();
  }
  function activityCategory(source) {
    if (/学习|课程/.test(source)) return 'course';
    if (/作业/.test(source)) return 'homework';
    if (/AI|模型/.test(source)) return 'ai';
    return 'system';
  }
  function renderActivity() {
    $('activity-total').textContent = String(activity.length);
    $('activity-source').textContent = activity[0]?.source || '—';
    $('activity-updated').textContent = activity[0]?.time || '暂无记录';
    $('activity-running').textContent = String((courseState === 'RUNNING' ? 1 : 0) + (homeworkBrowsing ? 1 : 0));
    const list = $('activity-list'); list.replaceChildren();
    const entries = activity.filter(item => activityFilter === 'all' || activityCategory(item.source) === activityFilter);
    if (!entries.length) { const empty = document.createElement('p'); empty.className = 'activity-empty'; empty.textContent = '当前筛选条件下没有日志。'; list.append(empty); return; }
    for (const item of entries) {
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
    send('course.activate');
  }
  function openHomework() {
    activeModule = 'homework'; $('workspace-module').textContent = '作业校对';
    $('workspace-detail').textContent = '点击左侧工作台可随时查看作业校对结果。';
    show('homework'); send('homework.activate');
  }
  function openAi() {
    activeModule = 'ai'; $('workspace-module').textContent = 'AI 服务';
    $('workspace-detail').textContent = '模型连接和请求状态由 AI 服务统一管理。';
    show('ai'); send('ai.activate'); send('ai.get-config');
  }
  function goBack() {
    if (!viewHistory.length) return false;
    const previous = viewHistory.pop();
    show(previous, false);
    if (previous === 'course') send('course.activate');
    else if (previous === 'homework') send('homework.activate');
    else if (previous === 'demo') send('demo.activate');
    else if (previous === 'ai') send('ai.activate');
    return true;
  }
  function setHomeworkBrowsing(browsing) {
    homeworkBrowsing = browsing;
    $('homework-show-results').hidden = !browsing;
    $('homework-open').textContent = browsing ? '正在浏览学习通' : '浏览学习通作业';
  }
  function openDemo() {
    activeModule = 'demo'; $('workspace-module').textContent = '页面事件演示';
    $('workspace-detail').textContent = '点击左侧工作台可随时回到演示控制页。';
    show('demo');
    send('demo.activate');
  }
  function setTheme(theme, notifyHost = true) {
    const value = theme === 'light' ? 'light' : 'dark';
    document.documentElement.dataset.theme = value;
    for (const item of document.querySelectorAll('[data-theme]')) item.classList.toggle('selected', item.dataset.theme === value);
    try { localStorage.setItem('fax-toolbox-theme', value); } catch (_) { /* UI remains usable without storage. */ }
    if (notifyHost) send('ui.theme', {theme: value});
  }
  function renderCourse(message) {
    courseState = message.state || courseState; courseReady = !!message.ready;
    $('course-state').textContent = stateName(courseState); $('course-detail').textContent = message.detail || '';
    $('home-course').textContent = stateName(courseState);
    logActivity('学习通刷课', stateName(courseState), message.detail || '状态已更新');
    if (message.url) $('course-url').textContent = message.url;
    $('course-open').textContent = message.url ? '返回学习通页面' : '打开学习通首页';
    if (message.chapter && message.chapter !== currentChapterId) {
      currentChapterId = message.chapter; currentChapterName = '';
      updateChapter();
    }
    $('course-start').disabled = !courseReady || !['READY', 'PAUSED', 'NEEDS_INPUT', 'STOPPED', 'ERROR'].includes(courseState);
    $('course-start').textContent = courseState === 'STOPPED' || courseState === 'ERROR' ? '重新开始' :
      ['PAUSED', 'NEEDS_INPUT'].includes(courseState) ? '继续任务' : '开始自动完成';
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
    if (message.currentId) currentChapterId = message.currentId;
    if (message.currentName) currentChapterName = message.currentName;
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
    const sessions = $('vault-session-list'); sessions.replaceChildren();
    const accounts = $('vault-list'); accounts.replaceChildren();
    for (const entry of entries.filter(item => item.kind === 'session')) {
      const row = document.createElement('div'); row.className = 'vault-row';
      const label = document.createElement('span'); label.textContent = entry.username;
      const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'secondary';
      remove.textContent = '删除保存副本'; remove.title = '删除密码箱中的登录状态；当前网页仍保持登录';
      remove.addEventListener('click', () => { if (confirm('删除密码箱中的学习通登录状态？当前网页仍保持登录。')) send('vault.delete', {entryId: entry.id}); });
      row.append(label, remove); sessions.append(row);
    }
    if (!sessions.childElementCount) sessions.textContent = '暂无保存的登录状态';
    for (const entry of entries.filter(item => item.kind !== 'session')) {
      const row = document.createElement('div'); row.className = 'vault-row';
      const label = document.createElement('span'); label.textContent = entry.username;
      const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = '删除';
      remove.addEventListener('click', () => { if (confirm('删除这个账号？')) send('vault.delete', {entryId: entry.id}); });
      row.append(label, remove); accounts.append(row);
    }
    if (!accounts.childElementCount) accounts.textContent = '暂无保存的账号';
  }
  function renderAiConfig(message) {
    const detail = message.error || (!message.configured ? '尚未配置 AI 服务。请选择服务商并保存 API Key。' :
      `服务商：${message.provider}  ·  模型：${message.model}\n接口：${message.endpoint}\nAPI Key：${message.hasKey ? '已加密保存' : '未填写'}`);
    $('ai-config-summary').textContent = detail;
    balanceAvailable = !!(message.configured && message.hasKey && message.balanceSupported);
    $('course-ai-summary').textContent = message.configured ? `${message.provider} · ${message.model}` : detail;
    $('homework-ai-summary').textContent = message.configured ? `${message.provider} · ${message.model}` : detail;
    $('ai-balance-refresh').disabled = !balanceAvailable || balanceLoading;
  }
  function renderAiBalance(message) {
    const detail = message.detail || 'API 余额暂不可用。';
    $('ai-balance').textContent = detail;
    balanceLoading = message.state === 'loading';
    $('ai-balance-refresh').disabled = !balanceAvailable || balanceLoading;
  }
  function renderAiTest(message) {
    const detail = message.detail || '';
    $('ai-test-result').textContent = detail;
    $('ai-test').disabled = message.state === 'testing';
  }
  function renderHomeworkStatus(message) {
    $('homework-page').textContent = message.url || '尚未打开';
    $('homework-status').textContent = message.detail || '';
    $('homework-start').disabled = !message.ready || !!message.busy;
    $('ai-homework-state').textContent = message.busy ? '正在处理作业请求' : (message.detail || '等待请求');
    logActivity('作业校对', message.busy ? '校对中' : '页面状态', message.detail || '状态已更新');
  }
  function renderHomeworkProgress(message) {
    const bar = $('homework-progress');
    if (Number.isFinite(message.percent)) bar.value = Math.max(0, Math.min(100, message.percent));
    else bar.removeAttribute('value');
    $('homework-progress-label').textContent = message.detail || '正在处理…';
    if (message.detail) $('ai-homework-state').textContent = message.detail;
  }
  function appendHomeworkMath(target, value) {
    const source = String(value || '');
    // Keep the source as text unless a complete TeX delimiter or matrix environment is present.
    const pattern = /\\\[([\s\S]*?)\\\]|\\\(([\s\S]*?)\\\)|\$\$([\s\S]*?)\$\$|(?<!\$)\$([^$\n]+?)\$(?!\$)|\\begin\{(p|b|v|V|B)matrix\}[\s\S]*?\\end\{\5matrix\}/g;
    let cursor = 0;
    for (const match of source.matchAll(pattern)) {
      if (match.index > cursor) target.append(document.createTextNode(source.slice(cursor, match.index)));
      const formula = match[1] ?? match[2] ?? match[3] ?? match[4] ?? match[0];
      const displayMode = match[1] !== undefined || match[3] !== undefined;
      const span = document.createElement('span');
      span.className = 'homework-math' + (displayMode ? ' display' : '');
      try {
        if (!window.katex) throw new Error('KaTeX unavailable');
        window.katex.render(formula, span, {
          displayMode, throwOnError: true, strict: 'ignore', trust: false,
          output: 'htmlAndMathml', maxExpand: 100, maxSize: 10
        });
      } catch (_) {
        span.className += ' homework-math-error';
        span.textContent = match[0];
      }
      target.append(span);
      cursor = match.index + match[0].length;
    }
    if (cursor < source.length) target.append(document.createTextNode(source.slice(cursor)));
  }
  function renderHomeworkResults(message) {
    const list = $('homework-results'); list.replaceChildren();
    const items = Array.isArray(message.items) ? message.items : [];
    $('homework-count').textContent = items.length ? `${items.length} 道题` : '';
    if (!items.length) { const empty = document.createElement('p'); empty.className = 'muted'; empty.textContent = '当前没有校对结果。'; list.append(empty); return; }
    setHomeworkBrowsing(false);
    send('homework.results');
    for (const item of items) {
      const card = document.createElement('article'); card.className = 'homework-item';
      const heading = document.createElement('div'); heading.className = 'homework-item-heading';
      const number = document.createElement('span'); number.className = 'homework-number'; number.textContent = `第 ${item.number} 题`;
      const type = document.createElement('span'); type.className = 'muted'; type.textContent = item.type === 'multi' ? '多选 · 仅题干' : item.type === 'single' ? '单选 / 判断 · 仅题干' : item.type === 'essay' ? '解答题' : '填空题';
      heading.append(number, type);
      const question = document.createElement('p'); question.className = 'homework-question'; appendHomeworkMath(question, item.question || '');
      const answer = document.createElement('p'); answer.className = 'homework-answer'; appendHomeworkMath(answer, `答案：${item.answer || '需要人工核对'}`);
      const explanation = document.createElement('details'); explanation.className = 'homework-explanation';
      const toggle = document.createElement('summary'); toggle.textContent = '查看详细解答 ↗';
      toggle.title = '在右侧画板中阅读详解；双击画板标题可移到独立窗口';
      toggle.addEventListener('click', event => { event.preventDefault(); send('homework.explain', {number: item.number}); });
      const body = document.createElement('p'); appendHomeworkMath(body, item.explanation || '模型未提供解析。');
      explanation.append(toggle, body); card.append(heading, question, answer, explanation); list.append(card);
    }
  }
  window.chrome?.webview?.addEventListener('message', event => {
    const message = event.data; if (!message || message.v !== 1) return;
    if (message.kind === 'status') {
      $('state').textContent = message.state || '';
      $('home-backend').textContent = stateName(message.state);
      $('home-detail').textContent = message.detail || '';
      $('demo-state').textContent = stateName(message.state);
      $('demo-detail').textContent = message.detail || '';
      logActivity('工具箱', stateName(message.state), message.detail || '');
    }
    if (message.kind === 'course.status') renderCourse(message);
    if (message.kind === 'course.web-health') {
      const note = $('course-web-health'); note.textContent = message.detail || '';
      note.hidden = !message.detail;
    }
    if (message.kind === 'homework.status') renderHomeworkStatus(message);
    if (message.kind === 'homework.progress') renderHomeworkProgress(message);
    if (message.kind === 'homework.result') renderHomeworkResults(message);
    if (message.kind === 'course.catalog') renderCatalog(message);
    if (message.kind === 'course.session') { $('course-save-session').textContent = message.detail?.startsWith('已') ? '登录状态已保存' : '保存当前登录状态'; logActivity('学习通', '登录状态', message.detail || ''); }
    if (message.kind === 'vault.list') renderVault(message.entries || []);
    if (message.kind === 'answers.status') $('answers-result').textContent = message.detail || '';
    if (message.kind === 'ai.status') { $('ai-result').textContent = message.detail || ''; $('course-ai-result').textContent = message.detail || ''; $('ai-course-state').textContent = message.detail || '等待请求'; logActivity('AI 服务', '学习通答题', message.detail || '状态已更新'); }
    if (message.kind === 'diagnostics.event') logActivity(message.source || '系统', message.title || '日志', message.detail || '');
    if (message.kind === 'diagnostics.snapshot') { activity.length = 0; for (const entry of (message.entries || []).slice().reverse()) logActivity(entry.source, entry.title, entry.detail); }
    if (message.kind === 'diagnostics.endpoint') $('diagnostics-endpoint').textContent = message.enabled ? `本机诊断端点：${message.pipeName}` : '';
    if (message.kind === 'ai.config') renderAiConfig(message);
    if (message.kind === 'ai.balance') renderAiBalance(message);
    if (message.kind === 'ai.test.status') renderAiTest(message);
  });
  for (const item of document.querySelectorAll('[data-view]')) item.addEventListener('click', () => {
    if (item.dataset.view === 'workspace') showWorkspace();
    else if (item.dataset.view === 'course') openCourse();
    else if (item.dataset.view === 'homework') openHomework();
    else if (item.dataset.view === 'ai') openAi();
    else show(item.dataset.view);
  });
  $('open-course-card').addEventListener('click', openCourse);
  for (const item of document.querySelectorAll('[data-open]')) item.addEventListener('click', () => item.dataset.open === 'course' ? openCourse() : item.dataset.open === 'homework' ? openHomework() : item.dataset.open === 'ai' ? openAi() : openDemo());
  $('module-course').addEventListener('click', openCourse);
  $('module-homework').addEventListener('click', openHomework);
  $('module-ai').addEventListener('click', openAi);
  $('module-demo').addEventListener('click', openDemo);
  window.FaxToolboxUi = Object.freeze({openCourse, openHomework, openDemo, openAi, showHome: () => show('home'),
    goBack, applyThemeFromHost: theme => setTheme(theme, false),
    applyDeveloperModeFromHost: enabled => setDeveloperMode(enabled, false, false),
    refresh: () => { show(currentView || 'home', false); send('ai.get-config'); }});
  const palette = $('command-palette');
  function togglePalette(open) { palette.hidden = !open; if (open) palette.querySelector('button')?.focus(); }
  palette.addEventListener('click', event => { if (event.target === palette) togglePalette(false); });
  for (const item of palette.querySelectorAll('[data-action]')) item.addEventListener('click', () => {
    togglePalette(false);
    const action = item.dataset.action;
    if (action === 'course') openCourse();
    else if (action === 'homework') openHomework();
    else if (action === 'demo') openDemo();
    else if (action === 'ai') openAi();
    else show(action);
  });
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') { event.preventDefault(); togglePalette(palette.hidden); }
    if (event.key === 'Escape' && !palette.hidden) togglePalette(false);
  });
  $('workspace-modules').addEventListener('click', () => show('modules'));
  $('start').addEventListener('click', () => { openDemo(); send('session.start'); });
  $('cancel').addEventListener('click', () => send('session.cancel'));
  $('course-open').addEventListener('click', () => send('course.open'));
  $('homework-open').addEventListener('click', () => { setHomeworkBrowsing(true); show('homework'); send('homework.open'); });
  $('homework-show-results').addEventListener('click', () => { setHomeworkBrowsing(false); show('homework'); send('homework.results'); });
  $('homework-start').addEventListener('click', () => send('homework.start'));
  $('homework-ai-open').addEventListener('click', openAi);
  $('course-ai-open').addEventListener('click', openAi);
  $('course-save-session').addEventListener('click', () => send('course.session.save'));
  $('course-analyze').addEventListener('click', () => send('course.analyze'));
  $('course-start').addEventListener('click', () => send('course.start'));
  $('course-chapters').addEventListener('change', () => { $('course-jump').disabled = !catalogLive || !$('course-chapters').value; });
  $('course-jump').addEventListener('click', () => send('course.jump', {chapterId: $('course-chapters').value}));
  $('course-pause').addEventListener('click', () => send('course.pause'));
  $('course-stop').addEventListener('click', () => send('course.stop'));
  $('answers-save').addEventListener('click', () => send('answers.save', {chapter: $('answers-chapter').value.trim(), answers: $('answers-text').value}));
  $('ai-configure').addEventListener('click', () => send('ai.configure'));
  $('ai-test').addEventListener('click', () => send('ai.test'));
  $('ai-balance-refresh').addEventListener('click', () => send('ai.balance.refresh'));
  $('vault-add').addEventListener('click', () => send('vault.save'));
  $('settings-vault').addEventListener('click', () => show('vault'));
  $('settings-open-ai').addEventListener('click', openAi);
  for (const item of document.querySelectorAll('[data-settings-panel]')) item.addEventListener('click', () => {
    for (const button of document.querySelectorAll('[data-settings-panel]')) button.classList.toggle('selected', button === item);
    for (const panel of document.querySelectorAll('[data-panel]')) panel.hidden = panel.dataset.panel !== item.dataset.settingsPanel;
  });
  for (const item of document.querySelectorAll('[data-activity-filter]')) item.addEventListener('click', () => {
    activityFilter = item.dataset.activityFilter;
    for (const button of document.querySelectorAll('[data-activity-filter]')) button.classList.toggle('selected', button === item);
    renderActivity();
  });
  $('diagnostics-inspect').addEventListener('click', () => send('diagnostics.inspect'));
  $('diagnostics-devtools').addEventListener('click', () => send('diagnostics.devtools'));
  $('diagnostics-export').addEventListener('click', () => send('diagnostics.export'));
  let developerToastTimer;
  function setDeveloperMode(enabled, notify = false, notifyHost = true) {
    developerMode = !!enabled;
    $('developer-mode').checked = developerMode;
    for (const item of document.querySelectorAll('.developer-only')) item.hidden = !developerMode;
    for (const item of document.querySelectorAll('[data-developer-tool]')) item.hidden = !developerMode;
    document.querySelector('.nav-flyout [data-view="demo"]').hidden = !developerMode;
    document.querySelector('.nav-flyout p:last-of-type').hidden = !developerMode;
    document.querySelector('#command-palette [data-action="demo"]').hidden = !developerMode;
    try { localStorage.setItem('fax-toolbox-developer-mode', developerMode ? 'on' : 'off'); } catch (_) {}
    if (notifyHost) send('developer.mode', {enabled: developerMode});
    if (notify) {
      const toast = $('developer-toast');
      $('developer-toast-text').textContent = developerMode ? '开发者模式已开启 · 调试工具现在可用' : '开发者模式已关闭';
      toast.hidden = false;
      toast.style.animation = 'none';
      void toast.offsetWidth;
      toast.style.animation = '';
      clearTimeout(developerToastTimer);
      developerToastTimer = setTimeout(() => { toast.hidden = true; }, 2600);
    }
  }
  $('developer-mode').addEventListener('change', event => setDeveloperMode(event.target.checked, true));
  let savedDeveloperMode = false;
  try { savedDeveloperMode = localStorage.getItem('fax-toolbox-developer-mode') === 'on'; } catch (_) {}
  setDeveloperMode(savedDeveloperMode);
  const toolGroup = document.querySelector('.nav-group');
  let flyoutOpenTimer, flyoutCloseTimer;
  const openFlyout = () => { clearTimeout(flyoutCloseTimer); clearTimeout(flyoutOpenTimer);
    flyoutOpenTimer = setTimeout(() => toolGroup.classList.add('flyout-open'), 380); };
  const closeFlyout = () => { clearTimeout(flyoutOpenTimer); clearTimeout(flyoutCloseTimer);
    flyoutCloseTimer = setTimeout(() => toolGroup.classList.remove('flyout-open'), 180); };
  toolGroup.addEventListener('pointerenter', openFlyout);
  toolGroup.addEventListener('pointerleave', closeFlyout);
  toolGroup.addEventListener('focusin', () => { clearTimeout(flyoutCloseTimer); toolGroup.classList.add('flyout-open'); });
  toolGroup.addEventListener('focusout', () => setTimeout(() => {
    if (!toolGroup.contains(document.activeElement)) toolGroup.classList.remove('flyout-open');
  }, 0));
  toolGroup.querySelectorAll('.nav-flyout button').forEach(button => button.addEventListener('click', () => toolGroup.classList.remove('flyout-open')));
  for (const item of document.querySelectorAll('[data-theme]')) item.addEventListener('click', () => setTheme(item.dataset.theme));
  let savedTheme = 'light'; try { savedTheme = localStorage.getItem('fax-toolbox-theme') || 'light'; } catch (_) { /* Use light default. */ }
  setTheme(savedTheme);
  const initialModule = new URLSearchParams(location.search).get('module');
  if (initialModule === 'course') openCourse();
  else if (initialModule === 'homework') openHomework();
  else if (initialModule === 'demo') openDemo();
  else if (initialModule === 'ai') openAi();
  else show('home');
  send('ai.get-config');
})();
