(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  function uiIcon(name) {
    const paths = {close: '<path d="m6 6 12 12M18 6 6 18"/>', compose: '<path d="M13 5H6a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-7M15 3l6 6M10 14l1-5 8-8 4 4-8 8z"/>', arrow: '<path d="M5 12h14m-5-5 5 5-5 5"/>'};
    return `<svg class="button-icon" viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.arrow}</svg>`;
  }
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
  const viewScrollPositions = new Map();
  let currentView = '';
  let restoringAgentSnapshot = false;
  let agentMode = 'chat', agentEffort = 'normal', agentProvider = '', agentModel = '';
  const motionReduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const activity = [];
  let activityFilter = 'all';
  let developerMode = false;
  const stateNames = {STARTING: '启动中', READY: '就绪', CREATED: '正在创建', OBSERVING: '观察中', RUNNING: '运行中', PAUSED: '已暂停', STOPPED: '已停止', COMPLETE: '已完成', CANCELLED: '已取消', WAITING_LOGIN: '等待登录', CHOOSING_COURSE: '选择课程', NEEDS_INPUT: '等待人工操作', ERROR: '发生错误'};
  const viewNames = {home: '首页', modules: '工具', workspace: '工作台', demo: '页面事件演示', course: '学习通', homework: '作业校对', ai: 'Agent', activity: '状态', settings: '设置', vault: '密码箱', proxy: '网络管家'};
  const confirmation = $('toolbox-confirm');
  let confirmationAction = null;
  let confirmationTrigger = null;
  function confirmAction(title, message, action, options = {}) {
    if (confirmation.open) confirmation.close();
    confirmationAction = action;
    confirmationTrigger = document.activeElement;
    $('toolbox-confirm-title').textContent = title;
    $('toolbox-confirm-message').textContent = message;
    const input = $('toolbox-confirm-input'); input.hidden = !options.input; input.value = '';
    input.placeholder = options.placeholder || '';
    const accept = $('toolbox-confirm-accept'); accept.textContent = options.confirmLabel || '删除';
    accept.classList.toggle('destructive', !options.input);
    confirmation.showModal();
    (options.input ? input : $('toolbox-confirm-cancel')).focus();
  }
  $('toolbox-confirm-cancel').addEventListener('click', () => confirmation.close());
  $('toolbox-confirm-accept').addEventListener('click', () => {
    const input = $('toolbox-confirm-input');
    if (!input.hidden && !input.value.trim()) { input.focus(); return; }
    const action = confirmationAction;
    confirmationAction = null;
    confirmation.close();
    action?.(input.value.trim());
  });
  $('toolbox-confirm-input').addEventListener('keydown', event => {
    if (event.key === 'Enter') { event.preventDefault(); $('toolbox-confirm-accept').click(); }
  });
  confirmation.addEventListener('close', () => {
    if (confirmation.open) return;
    confirmationAction = null;
    if (confirmationTrigger?.isConnected) confirmationTrigger.focus();
    confirmationTrigger = null;
  });
  confirmation.addEventListener('pointerdown', event => {
    if (event.target !== confirmation) return;
    const rect = confirmation.getBoundingClientRect();
    if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) confirmation.close();
  });
  function focusedMenu(side, entries, initial) {
    const menu = document.createElement('nav'); menu.className = 'focus-menu'; menu.setAttribute('aria-label', '内容分类');
    const panels = document.createElement('div'); panels.className = 'focus-panels';
    const buttons = new Map();
    function select(key) {
      for (const [name, button] of buttons) button.classList.toggle('selected', name === key);
      for (const panel of panels.children) panel.hidden = panel.dataset.focusPanel !== key;
      side.dataset.activePanel = key;
    }
    for (const {key, label, nodes} of entries) {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'focus-menu-item'; button.dataset.focusKey = key;
      button.textContent = label; button.addEventListener('click', () => select(key)); menu.append(button); buttons.set(key, button);
      const panel = document.createElement('div'); panel.className = 'focus-panel'; panel.dataset.focusPanel = key;
      for (const node of nodes) if (node) { if (node.tagName === 'DETAILS') node.open = true; panel.append(node); }
      panels.append(panel);
    }
    side.append(menu, panels); select(initial);
    return select;
  }
  function setupFocusedViews() {
    const overview = document.querySelector('#view-home .overview-grid');
    const heading = overview.previousElementSibling;
    $('view-activity').insertBefore(heading, document.querySelector('#view-activity .activity-overview'));
    heading.querySelector('.eyebrow').textContent = '应用';
    heading.querySelector('h2').textContent = '运行概览';
    heading.after(overview);

    const agentSide = document.querySelector('.agent-side');
    const agentSettings = agentSide.querySelector('.agent-sidebar-settings');
    const memoryPanel = document.createElement('section'); memoryPanel.className = 'agent-text-panel';
    memoryPanel.innerHTML = '<label for="agent-background">会话背景</label><textarea id="agent-background" maxlength="1500" rows="6" placeholder="这段对话需要长期记住的背景"></textarea><label for="agent-memory-pool">记忆池 <small id="agent-memory-count"></small></label><textarea id="agent-memory-pool" maxlength="2200" rows="10" placeholder="对话接近上下文上限时自动整理"></textarea>';
    const promptPanel = document.createElement('section'); promptPanel.className = 'agent-text-panel';
    promptPanel.innerHTML = '<label for="agent-system-prompt">自定义系统提示词</label><small id="agent-prompt-scope"></small><textarea id="agent-system-prompt" maxlength="3000" rows="12" placeholder="留空使用当前模型与模式的默认提示词"></textarea>';
    const inspector = $('agent-inspector');
    const inspectorDetails = [...inspector.querySelectorAll('.agent-detail')];
    const selectAgentPanel = focusedMenu(agentSide, [
      {key: 'sessions', label: '会话', nodes: [agentSide.querySelector('.agent-sessions')]},
      {key: 'model', label: '模型连接', nodes: [agentSettings.querySelector('.agent-config-panel')]},
      {key: 'prompts', label: '提示词', nodes: [promptPanel]},
      {key: 'memory', label: '记忆池', nodes: [memoryPanel]},
      {key: 'modules', label: '模块配置', nodes: [agentSettings.querySelector('.agent-configuration')]},
      {key: 'usage', label: '用量', nodes: [inspectorDetails[0]]},
      {key: 'tools', label: '可用工具', nodes: [inspectorDetails[1]]},
      {key: 'responses', label: '模块响应', nodes: [agentSettings.querySelector('.ai-service-log')]}
    ], 'sessions');
    agentSettings.remove(); inspector.remove();
    const agentMenu = agentSide.querySelector(':scope > .focus-menu');
    const agentPanels = agentSide.querySelector(':scope > .focus-panels');
    const agentHeader = agentSide.querySelector('.agent-side-head');
    const agentTitle = agentHeader.querySelector('strong');
    agentHeader.append($('agent-search-toggle'));
    agentSide.querySelector('.agent-list-heading').remove();
    const newChat = $('agent-clear'); agentSide.insertBefore(newChat, $('agent-folder'));
    newChat.innerHTML = uiIcon('compose') + '<span>新对话</span>'; newChat.classList.add('agent-new-chat');
    const projectIcon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    projectIcon.setAttribute('viewBox', '0 0 24 24'); projectIcon.setAttribute('aria-hidden', 'true');
    const folderPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    folderPath.setAttribute('d', 'M3 7a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z');
    projectIcon.append(folderPath); $('agent-folder').insertBefore(projectIcon, $('agent-root'));
    const rail = document.createElement('nav'); rail.className = 'agent-section-rail'; rail.setAttribute('aria-label', 'Agent 分类');
    const railItems = [
      ['sessions', '会话', '<path d="M4 5h16v12H8l-4 3z"/>'],
      ['model', '模型', '<path d="m12 2 2.2 6.8L21 11l-6.8 2.2L12 20l-2.2-6.8L3 11l6.8-2.2z"/>'],
      ['prompts', '提示词', '<path d="M5 4h14v16H5zM8 8h8M8 12h8M8 16h5"/>'],
      ['memory', '记忆池', '<path d="M5 4h14v16H5zM9 4v16M12 9h4M12 13h4"/>'],
      ['modules', '模块配置', '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>'],
      ['usage', '用量', '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>'],
      ['tools', '可用工具', '<path d="M14 5a5 5 0 0 0-6 6L3 16l5 5 5-5a5 5 0 0 0 6-6l-3 3-4-4z"/>'],
      ['responses', '模块响应', '<path d="M5 3h10l4 4v14H5z"/><path d="M15 3v5h4M8 12h8M8 16h6"/>']
    ];
    for (const [key, label, paths] of railItems) {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'agent-rail-item';
      button.dataset.agentSection = key; button.title = label; button.setAttribute('aria-label', label);
      button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${paths}</svg>`;
      rail.append(button);
    }
    const sideContent = document.createElement('div'); sideContent.className = 'agent-side-content';
    sideContent.append(agentHeader, newChat, $('agent-folder'), agentPanels);
    agentSide.replaceChildren(rail, sideContent);
    const composerActions = $('agent-form').lastElementChild;
    composerActions.classList.add('agent-composer-actions');
    const runSettings = document.createElement('div'); runSettings.className = 'agent-run-settings';
    runSettings.innerHTML = '<button id="agent-run-toggle" type="button" aria-expanded="false">Chat · 标准</button><div id="agent-run-panel" class="agent-run-panel" hidden><div class="agent-run-modes" role="radiogroup" aria-label="Agent 模式"><label><input type="radio" name="agent-mode" value="chat" checked><span>Chat</span></label><label><input type="radio" name="agent-mode" value="code"><span>Code</span></label></div><div class="agent-run-label">模型</div><div id="agent-model-list" class="agent-model-list" aria-label="模型选择"></div><label for="agent-effort">思考深度 <strong id="agent-effort-label">标准</strong></label><div class="agent-effort-slider"><input id="agent-effort" type="range" min="0" max="2" step="1" value="1" aria-label="思考深度"></div><span id="agent-model-error" class="muted"></span></div>';
    composerActions.insertBefore(runSettings, $('agent-send'));
    const selectAgentCategory = key => {
      selectAgentPanel(key);
      const label = railItems.find(item => item[0] === key)?.[1];
      agentTitle.textContent = key === 'sessions' ? 'Agent' : label || 'Agent';
      for (const button of rail.querySelectorAll('button')) {
        const active = button.dataset.agentSection === key;
        button.classList.toggle('active', active); button.setAttribute('aria-pressed', String(active));
      }
    };
    rail.querySelectorAll('button').forEach(button => button.addEventListener('click', () => selectAgentCategory(button.dataset.agentSection)));
    agentMenu.remove();
    selectAgentCategory('sessions');

    function studyLayout(id, primary, secondary, initial) {
      const view = $('view-' + id);
      const layout = document.createElement('div'); layout.className = 'study-layout';
      const side = document.createElement('aside'); side.className = 'study-side'; side.setAttribute('aria-label', viewNames[id] + '分类');
      const main = document.createElement('div'); main.className = 'study-main';
      const toggle = document.createElement('button'); toggle.type = 'button'; toggle.className = 'study-toggle secondary';
      toggle.textContent = '☰  分类'; toggle.setAttribute('aria-label', '打开' + viewNames[id] + '分类'); toggle.setAttribute('aria-expanded', 'false');
      toggle.addEventListener('click', () => { layout.classList.toggle('side-open'); toggle.setAttribute('aria-expanded', String(layout.classList.contains('side-open'))); });
      layout.addEventListener('pointerdown', event => { if (layout.classList.contains('side-open') && !side.contains(event.target) && event.target !== toggle) { layout.classList.remove('side-open'); toggle.setAttribute('aria-expanded', 'false'); } });
      layout.addEventListener('keydown', event => { if (event.key === 'Escape') { layout.classList.remove('side-open'); toggle.setAttribute('aria-expanded', 'false'); toggle.focus(); } });
      for (const node of primary) main.append(node);
      main.prepend(toggle);
      focusedMenu(side, secondary, initial);
      layout.append(side, main); view.append(layout);
    }
    const course = $('view-course');
    const courseActions = course.querySelector('.course-actions');
    const session = document.createElement('div'); session.className = 'study-session';
    session.append($('course-chapter-id'), $('course-url'), $('course-save-session'), $('course-analyze'));
    studyLayout('course', [courseActions, course.querySelector('.course-current'), course.querySelector('.course-control')], [
      {key: 'catalog', label: '章节目录', nodes: [course.querySelector('.course-catalog')]},
      {key: 'session', label: '登录与页面', nodes: [session]},
      {key: 'ai', label: 'AI 答题', nodes: [course.querySelector('.ai-card')]},
      {key: 'answers', label: '答案库', nodes: [course.querySelector('.advanced')]}
    ], 'catalog');
    const homework = $('view-homework');
    studyLayout('homework', [homework.querySelector('.course-actions'), homework.querySelector('.homework-results')], [
      {key: 'page', label: '当前页面', nodes: [homework.querySelector('.course-current')]},
      {key: 'progress', label: '校对进度', nodes: [homework.querySelector('.homework-progress-card')]},
      {key: 'ai', label: 'AI 校对', nodes: [homework.querySelector('.ai-card')]}
    ], 'page');
  }
  setupFocusedViews();
  for (const id of ['course-ai-open', 'homework-ai-open']) $(id).innerHTML = '<span>模型设置</span>' + uiIcon('arrow');
  $('home-tool-count').textContent = String(document.querySelectorAll('#view-home .module-card:not([hidden])').length);
  const stateName = state => stateNames[state] || state || '未知';
  function send(command, extra = {}) { window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra})); }
  function animateNew(element) {
    if (motionReduced.matches) return;
    element.classList.add('motion-new');
    element.addEventListener('animationend', () => element.classList.remove('motion-new'), {once: true});
  }
  function show(view, record = true) {
    if (record && currentView && currentView !== view) viewHistory.push(currentView);
    const previousView = currentView;
    currentView = view;
    if (view !== 'ai') { document.querySelector('.agent-side')?.classList.remove('mobile-open'); $('agent-sidebar-toggle')?.setAttribute('aria-expanded', 'false'); }
    if (previousView !== view) for (const layout of document.querySelectorAll('.study-layout.side-open')) { layout.classList.remove('side-open'); layout.querySelector('.study-toggle')?.setAttribute('aria-expanded', 'false'); }
    // Touch only the outgoing and incoming surfaces so cached views do not
    // repeatedly invalidate layout while WebView2 is painting another tab.
    if (previousView !== view) {
      if (previousView) viewScrollPositions.set(previousView, document.scrollingElement.scrollTop);
      // A new module tab starts without a previous view. Hide the default home
      // surface too; otherwise both sections remain visible in the narrow pane.
      if (!previousView) for (const surface of document.querySelectorAll('.content > .view')) surface.hidden = true;
      if (previousView) {
        const outgoing = $('view-' + previousView);
        outgoing.hidden = true;
        outgoing.classList.remove('motion-enter');
        if (previousView === 'ai') outgoing.querySelectorAll('.motion-new').forEach(item => item.classList.remove('motion-new'));
      }
      const incoming = $('view-' + view);
      incoming.hidden = false;
      if (previousView && !motionReduced.matches) incoming.classList.add('motion-enter');
      document.scrollingElement.scrollTop = viewScrollPositions.get(view) || 0;
    }
    const navigation = ['course', 'homework', 'demo', 'ai', 'proxy', 'workspace'].includes(view) ? 'modules' : view === 'vault' ? 'settings' : view;
    for (const item of document.querySelectorAll('.sidebar .nav-item[data-view]')) item.classList.toggle('active', item.dataset.view === navigation);
    for (const item of document.querySelectorAll('.nav-flyout [data-view]')) item.classList.toggle('active', item.dataset.view === view);
    $('location-name').textContent = viewNames[view];
    document.body.dataset.view = view;
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
    else if (activeModule === 'proxy') openProxy();
    else show('workspace');
  }
  function logActivity(source, title, detail) {
    const time = new Date().toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit', second: '2-digit'});
    if (activity[0]?.title === title && activity[0]?.detail === detail) return;
    const entry = {source, title, detail, time};
    activity.unshift(entry);
    const evicted = activity.length > 200 ? activity.pop() : null;
    if (currentView !== 'activity') return;
    updateActivitySummary();
    const matches = item => activityFilter === 'all' || activityCategory(item.source) === activityFilter;
    const list = $('activity-list');
    if (evicted && matches(evicted)) list.lastElementChild?.remove();
    if (matches(entry)) {
      list.querySelector('.activity-empty')?.remove();
      const row = createActivityRow(entry);
      animateNew(row);
      list.prepend(row);
    }
    if (!list.childElementCount) appendActivityEmpty(list);
  }
  function activityCategory(source) {
    if (/学习|课程/.test(source)) return 'course';
    if (/作业/.test(source)) return 'homework';
    if (/AI|模型/.test(source)) return 'ai';
    return 'system';
  }
  function updateActivitySummary() {
    $('activity-total').textContent = String(activity.length);
    $('activity-source').textContent = activity[0]?.source || '—';
    $('activity-updated').textContent = activity[0]?.time || '暂无记录';
    $('activity-running').textContent = String((courseState === 'RUNNING' ? 1 : 0) + (homeworkBrowsing ? 1 : 0));
  }
  function appendActivityEmpty(list) {
    const empty = document.createElement('p'); empty.className = 'activity-empty'; empty.textContent = '当前筛选条件下没有日志。'; list.append(empty);
  }
  function createActivityRow(item) {
    const row = document.createElement('div'); row.className = 'activity-row';
    const heading = document.createElement('strong'); heading.textContent = item.title;
    const meta = document.createElement('span'); meta.textContent = item.source + ' · ' + item.time;
    const note = document.createElement('p'); note.textContent = item.detail;
    row.append(heading, meta, note);
    return row;
  }
  function renderActivity() {
    updateActivitySummary();
    const list = $('activity-list');
    const entries = activity.filter(item => activityFilter === 'all' || activityCategory(item.source) === activityFilter);
    if (!entries.length) { list.replaceChildren(); appendActivityEmpty(list); return; }
    const fragment = document.createDocumentFragment();
    for (const item of entries) fragment.append(createActivityRow(item));
    list.replaceChildren(fragment);
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
    activeModule = 'ai'; $('workspace-module').textContent = 'Agent';
    $('workspace-detail').textContent = '处理项目任务，并管理各模块的模型连接。';
    show('ai'); send('ai.activate'); send('ai.get-config');
  }
  function openProxy() {
    activeModule = 'proxy'; $('workspace-module').textContent = '网络管家';
    show('proxy'); send('proxy.activate'); send('proxy.get');
  }
  function goBack() {
    if (!viewHistory.length) return false;
    const previous = viewHistory.pop();
    show(previous, false);
    if (previous === 'course') send('course.activate');
    else if (previous === 'homework') send('homework.activate');
    else if (previous === 'demo') send('demo.activate');
    else if (previous === 'ai') send('ai.activate');
    else if (previous === 'proxy') send('proxy.activate');
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
      remove.addEventListener('click', () => confirmAction('删除登录状态', '只删除密码箱中的副本；当前网页保持登录。', () => send('vault.delete', {entryId: entry.id})));
      row.append(label, remove); sessions.append(row);
    }
    if (!sessions.childElementCount) sessions.textContent = '暂无保存的登录状态';
    for (const entry of entries.filter(item => item.kind !== 'session')) {
      const row = document.createElement('div'); row.className = 'vault-row';
      const label = document.createElement('span'); label.textContent = entry.username;
      const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = '删除';
      remove.addEventListener('click', () => confirmAction('删除账号', `从密码箱中删除「${entry.username}」？`, () => send('vault.delete', {entryId: entry.id})));
      row.append(label, remove); accounts.append(row);
    }
    if (!accounts.childElementCount) accounts.textContent = '暂无保存的账号';
  }
  function renderAiConfig(message) {
    const modelLabel = name => ['qwen3.8-flash', 'qwen3.8-max', 'qwen-plus', 'notrack-uncensored'].includes(name) ? agentModelNames(name)[0] : name;
    const detail = message.error || (!message.configured ? '尚未配置模型' :
      `${message.provider} · ${modelLabel(message.model)}${message.requiresKey ? `\nAPI Key ${message.hasKey ? '已保存' : '未填写'}` : ''}`);
    const consumer = message.consumer || 'course';
    $('ai-config-heading').textContent = consumer === 'agent' ? 'Agent 配置' : consumer === 'homework' ? '作业校对配置' : '学习通答题配置';
    for (const item of document.querySelectorAll('[data-ai-consumer]')) { item.classList.toggle('selected', item.dataset.aiConsumer === consumer); item.setAttribute('aria-pressed', String(item.dataset.aiConsumer === consumer)); }
    for (const item of message.consumers || []) {
      const target = $('ai-' + item.id + '-config');
      if (target) target.textContent = item.configured ? `${item.provider} · ${modelLabel(item.model)}` : '尚未配置模型';
      if (item.id === 'course') $('course-ai-summary').textContent = item.configured ? `${item.provider} · ${modelLabel(item.model)}` : '尚未配置学习通 AI';
      if (item.id === 'homework') $('homework-ai-summary').textContent = item.configured ? `${item.provider} · ${modelLabel(item.model)}` : '尚未配置作业 AI';
      if (item.id === 'agent') {
        if (item.configured) { agentModel = item.model || ''; agentProvider = item.providerId || (item.provider === '本地模型' ? 'ollama' : item.provider === 'NoTrack' ? 'notrack' : item.provider || ''); updateAgentRunOptions(); }
        agentImagesSupported = item.supportsImages !== false;
        $('agent-image-file').disabled = !agentImagesSupported;
        $('agent-image-add').classList.toggle('disabled', !agentImagesSupported);
        $('agent-image-add').title = agentImagesSupported ? '添加图片，也可直接粘贴截图' : '当前模型仅支持文字';
      }
    }
    $('ai-config-summary').textContent = detail;
    balanceAvailable = !!(message.configured && message.hasKey && message.balanceSupported);
    document.querySelector('.agent-side .ai-balance-row').hidden = !balanceAvailable;
    $('ai-balance-refresh').disabled = !balanceAvailable || balanceLoading;
  }
  function renderAiBalance(message) {
    if (message.consumer && message.consumer !== document.querySelector('[data-ai-consumer].selected')?.dataset.aiConsumer) return;
    const detail = message.detail || 'API 余额暂不可用。';
    $('ai-balance').textContent = detail;
    balanceLoading = message.state === 'loading';
    $('ai-balance-refresh').disabled = !balanceAvailable || balanceLoading;
  }
  function renderAiTest(message) {
    if (message.consumer && message.consumer !== document.querySelector('[data-ai-consumer].selected')?.dataset.aiConsumer) return;
    const detail = message.detail || '';
    $('ai-test-result').textContent = detail;
    $('ai-test').disabled = message.state === 'testing';
  }
  let agentInstalledModels = [];
  function agentModelNames(name) {
    if (name === 'fax-toolbox-qwen3:8k') return ['Qwen3 8B · 长上下文', '本地模型 · 8K 上下文'];
    if (name === 'qwen3:8b') return ['Qwen3 8B', '本地模型'];
    if (name === 'notrack-uncensored') return ['NoTrack', '云端 · 自由创作'];
    if (name === 'qwen3.8-flash') return ['Qwen 3.8 Flash', '阿里百炼 · 性价比优先'];
    if (name === 'qwen3.8-max') return ['Qwen 3.8 Max', '阿里百炼 · 质量优先'];
    if (name === 'qwen-plus') return ['Qwen Plus', '阿里百炼 · 旧版配置'];
    const title = name.replace(/^hf\.co\//i, '').split('/').pop().replace(/[-_]/g, ' ');
    return [title || '未配置模型', name];
  }
  function renderAgentModelList() {
    const list = $('agent-model-list');
    list.replaceChildren();
    const heading = label => {
      const group = document.createElement('div'); group.className = 'agent-model-group';
      group.textContent = label; list.append(group);
    };
    if (agentProvider !== 'qwen') {
      heading('国内云端 · 推荐');
      const recommended = document.createElement('button'); recommended.type = 'button';
      recommended.className = 'agent-model-choice agent-model-recommend';
      recommended.dataset.agentProvider = 'qwen'; recommended.dataset.agentModel = 'qwen3.8-flash';
      const copy = document.createElement('span'); copy.className = 'agent-model-copy';
      const title = document.createElement('strong'); title.textContent = 'Qwen 3.8 Flash';
      const detail = document.createElement('small'); detail.textContent = '阿里百炼 · 性价比优先';
      copy.append(title, detail);
      const action = document.createElement('span'); action.className = 'agent-model-size'; action.textContent = '配置';
      recommended.append(copy, action); list.append(recommended);
    }
    const models = agentProvider === 'ollama' ? agentInstalledModels.slice() :
      agentProvider === 'notrack' ? [{name: 'notrack-uncensored'}] :
      agentProvider === 'qwen' ? [{name: 'qwen3.8-flash'}, {name: 'qwen3.8-max'}, {name: 'qwen-plus'}] : [];
    if (agentModel && !models.some(item => item.name === agentModel)) models.unshift({name: agentModel});
    if (models.length) heading(agentProvider === 'qwen' ? '国内云端 · 阿里百炼' :
      agentProvider === 'ollama' ? '本地模型' : agentProvider === 'notrack' ? '海外云端 · NoTrack' : '当前连接');
    for (const model of models) {
      const button = document.createElement('button'); button.type = 'button';
      button.className = 'agent-model-choice'; button.dataset.agentModel = model.name;
      const selected = model.name === agentModel;
      button.classList.toggle('selected', selected); button.setAttribute('aria-pressed', String(selected));
      const [title, detail] = agentModelNames(model.name);
      const copy = document.createElement('span'); copy.className = 'agent-model-copy';
      const heading = document.createElement('strong'); heading.textContent = title;
      const subtitle = document.createElement('small'); subtitle.textContent = detail;
      copy.append(heading, subtitle); button.append(copy);
      if (model.size) {
        const size = document.createElement('span'); size.className = 'agent-model-size';
        size.textContent = `${(model.size / 1024 ** 3).toFixed(1)} GB`;
        button.append(size);
      }
      list.append(button);
    }
    const configure = document.createElement('button'); configure.type = 'button';
    configure.className = 'agent-model-configure'; configure.textContent = '管理模型连接';
    list.append(configure);
  }
  function updateAgentRunOptions() {
    const effortNames = {quick: '快速', normal: '标准', deep: '深入'};
    const shortModel = agentModel ? agentModelNames(agentModel)[0] : '选择模型';
    $('agent-run-toggle').textContent = `${agentMode === 'code' ? 'Code' : 'Chat'} · ${shortModel} · ${effortNames[agentEffort] || '标准'}`;
    $('agent-effort').value = String({quick: 0, normal: 1, deep: 2}[agentEffort] ?? 1);
    $('agent-effort-label').textContent = effortNames[agentEffort] || '标准';
    $('agent-effort').setAttribute('aria-valuetext', effortNames[agentEffort] || '标准');
    for (const radio of document.querySelectorAll('input[name="agent-mode"]')) radio.checked = radio.value === agentMode;
    $('agent-prompt-scope').textContent = `${agentModel || '当前模型'} · ${agentMode === 'code' ? 'Code' : 'Chat'}`;
    $('agent-prompt').placeholder = agentMode === 'code' ? '描述要处理的任务…' : '输入消息…';
    const empty = $('agent-timeline').querySelector('.agent-empty strong');
    if (empty) empty.textContent = agentMode === 'code' ? '描述任务，Agent 会记录执行过程。' : '开始对话';
    renderAgentModelList();
  }
  let agentBusy = false;
  let agentApprovalId = '';
  let agentPreparingImages = false;
  let agentImages = [];
  let agentFiles = [];
  let agentPlanOnly = false;
  let agentImagesSupported = true;
  let activeAgentSessionId = '';
  const agentDrafts = new Map();
  const agentSessionMeta = new Map();
  let agentSessionSearch = '';
  function filterAgentSessions() {
    for (const group of $('agent-session-list').querySelectorAll('.agent-session-group')) {
      let visible = 0;
      for (const row of group.querySelectorAll('.agent-session-row')) {
        row.hidden = !!agentSessionSearch && !row.querySelector('.agent-session-select').textContent.toLocaleLowerCase().includes(agentSessionSearch);
        if (!row.hidden) visible++;
      }
      group.hidden = visible === 0;
    }
    const archive = $('agent-session-list').querySelector('.agent-archive-group');
    if (archive) {
      archive.hidden = archive.querySelector('.agent-session-group')?.hidden ?? true;
      if (agentSessionSearch) archive.open = !archive.hidden;
    }
  }
  function renameAgentSession(row) {
    if (agentBusy || row.querySelector('input')) return;
    const select = row.querySelector('.agent-session-select');
    const original = select.textContent;
    const input = document.createElement('input'); input.type = 'text'; input.maxLength = 100;
    input.className = 'agent-session-rename'; input.value = original;
    select.hidden = true; row.insertBefore(input, select); input.focus(); input.select();
    let finished = false;
    const finish = save => { if (finished) return; finished = true; const title = input.value.trim(); input.remove(); select.hidden = false; if (save && title && title !== original) send('agent.session.rename', {sessionId: row.dataset.sessionId, title}); };
    input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); finish(true); } else if (event.key === 'Escape') { event.preventDefault(); finish(false); } });
    input.addEventListener('blur', () => finish(true));
  }
  let agentSessionListSignature = '';
  function renderAgentSessions(data) {
    // Unchanged metadata must preserve focus, scroll and an in-progress rename.
    const signature = JSON.stringify(data);
    if (signature === agentSessionListSignature) return;
    agentSessionListSignature = signature;
    const list = $('agent-session-list'); list.replaceChildren(); agentSessionMeta.clear();
    const groups = new Map();
    for (const session of data.sessions || []) {
      agentSessionMeta.set(session.id, session);
      const project = session.root || '';
      const groupKey = session.archived ? 'archived' : session.pinned ? 'pinned' : session.section ? 'section:' + session.section : 'project:' + project;
      if (!groups.has(groupKey)) {
        const group = document.createElement('section'); group.className = 'agent-session-group';
        if (session.archived) {
          const fold = document.createElement('details'); fold.className = 'agent-archive-group';
          const summary = document.createElement('summary'); summary.textContent = '归档'; fold.append(summary, group); list.append(fold);
        } else {
          const heading = document.createElement('h4');
          heading.textContent = session.pinned ? '置顶' : session.section || (project ? project.split(/[\\/]/).filter(Boolean).pop() : '最近');
          heading.title = session.section || project; group.append(heading); list.append(group);
        }
        groups.set(groupKey, group);
      }
      const row = document.createElement('div'); row.className = 'agent-session-row'; row.dataset.sessionId = session.id;
      row.classList.toggle('pinned', !!session.pinned); row.classList.toggle('unread', !!session.unread);
      const select = document.createElement('button'); select.type = 'button'; select.className = 'agent-session-select';
      select.textContent = session.title || '新任务'; select.title = select.textContent;
      select.classList.toggle('selected', session.id === data.activeId); select.disabled = agentBusy;
      select.addEventListener('click', () => { if (!agentBusy && session.id !== activeAgentSessionId) send('agent.session.select', {sessionId: session.id}); document.querySelector('.agent-side').classList.remove('mobile-open'); $('agent-sidebar-toggle').setAttribute('aria-expanded', 'false'); });
      const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'agent-session-delete';
      remove.innerHTML = uiIcon('close'); remove.title = '删除会话'; remove.setAttribute('aria-label', '删除会话 ' + select.textContent); remove.disabled = agentBusy;
      remove.addEventListener('click', () => { if (!agentBusy) confirmAction('删除会话', `删除「${session.title || '新任务'}」及其记录？此操作无法撤销。`, () => { if (!agentBusy) send('agent.session.delete', {sessionId: session.id}); }); });
      row.append(select, remove); groups.get(groupKey).append(row);
    }
    filterAgentSessions();
  }
  function renderAgentUsage(data) {
    const input = Number(data.input || 0), output = Number(data.output || 0);
    const rate = input > 0 && data.cached != null ? Math.round(100 * Number(data.cached) / input) + '%' : '—';
    const row = (label, value) => {
      const item = document.createElement('div'); item.className = 'agent-usage-row';
      const name = document.createElement('span'); name.textContent = label;
      const number = document.createElement('strong'); number.textContent = value;
      item.append(name, number); return item;
    };
    $('agent-usage').replaceChildren(row('请求', String(data.calls || 0)), row('输入', input.toLocaleString()), row('输出', output.toLocaleString()));
    $('agent-meter').hidden = !data.calls;
    $('agent-meter').replaceChildren(row('缓存命中', rate));
    $('agent-cost').hidden = data.cost == null;
    $('agent-cost').replaceChildren(...(data.cost == null ? [] : [row('估算', `${data.currency === 'CNY' ? '¥' : '$'}${Number(data.cost).toFixed(6).replace(/0+$/, '').replace(/\.$/, '')}`)]));
  }
  function renderAgentImages(animateFrom = Number.POSITIVE_INFINITY) {
    const tray = $('agent-attachments'); tray.replaceChildren(); tray.hidden = agentImages.length === 0 && agentFiles.length === 0;
    agentImages.forEach((dataUrl, index) => {
      const item = document.createElement('div'); item.className = 'agent-image';
      if (index >= animateFrom) animateNew(item);
      const preview = document.createElement('img'); preview.src = dataUrl; preview.alt = '待发送图片 ' + (index + 1);
      const remove = document.createElement('button'); remove.type = 'button'; remove.innerHTML = uiIcon('close'); remove.title = '移除图片';
      remove.addEventListener('click', () => { agentImages.splice(index, 1); renderAgentImages(); });
      item.append(preview, remove); tray.append(item);
    });
    agentFiles.forEach((file, index) => {
      const item = document.createElement('div'); item.className = 'agent-file'; item.title = file.name;
      const icon = document.createElement('span'); icon.textContent = file.name.includes('/') ? '▧' : '▤';
      const name = document.createElement('span'); name.textContent = file.name;
      const remove = document.createElement('button'); remove.type = 'button'; remove.innerHTML = uiIcon('close'); remove.title = '移除附件';
      remove.addEventListener('click', () => { agentFiles.splice(index, 1); renderAgentImages(); });
      item.append(icon, name, remove); tray.append(item);
    });
  }
  async function addAgentImages(files) {
    if (agentBusy || agentPreparingImages) return;
    if (!agentImagesSupported) { $('agent-state').textContent = '当前模型仅支持文字'; return; }
    if (agentImages.length + files.length > 4) { $('agent-state').textContent = '最多添加 4 张图片'; return; }
    const initialImageCount = agentImages.length;
    agentPreparingImages = true; $('agent-send').disabled = true; $('agent-state').textContent = '正在准备图片…';
    try {
      for (const file of files) {
        if (!/^image\/(jpeg|png|webp|gif)$/.test(file.type) || file.size > 12 * 1024 * 1024) throw new Error('仅支持 12 MB 内的常见图片');
        const bitmap = await createImageBitmap(file);
        let scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height));
        let encoded = '';
        for (let attempt = 0; attempt < 4; attempt++) {
          const canvas = document.createElement('canvas'); canvas.width = Math.max(1, Math.round(bitmap.width * scale)); canvas.height = Math.max(1, Math.round(bitmap.height * scale));
          const context = canvas.getContext('2d'); context.fillStyle = '#fff'; context.fillRect(0, 0, canvas.width, canvas.height); context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
          encoded = canvas.toDataURL('image/jpeg', .84 - attempt * .12);
          if (encoded.length < 2_700_000) break;
          scale *= .75;
        }
        bitmap.close();
        if (encoded.length >= 2_700_000) throw new Error('图片仍然过大，请选择较小的图片');
        agentImages.push(encoded);
      }
      renderAgentImages(initialImageCount); $('agent-state').textContent = '';
    } catch (error) { $('agent-state').textContent = error.message || '图片无法读取'; }
    finally { agentPreparingImages = false; $('agent-send').disabled = agentBusy; $('agent-image-file').value = ''; }
  }
  const allowedAgentDocuments = /\.(txt|md|csv|tsv|json|xml|html?|css|js|ts|py|cs|rs|go|java|ya?ml|toml|sql|log|pdf|docx|pptx|xlsx)$/i;
  const agentDocumentTypes = '.txt,.md,.csv,.tsv,.json,.xml,.html,.htm,.css,.js,.ts,.py,.cs,.rs,.go,.java,.yaml,.yml,.toml,.sql,.log,.pdf,.docx,.pptx,.xlsx';
  async function addAgentFiles(items) {
    if (agentBusy || agentPreparingImages || !items.length) return;
    let accepted = items.filter(item => allowedAgentDocuments.test(item.name) || /^image\/(jpeg|png|webp|gif)$/.test(item.file.type));
    let skipped = items.length - accepted.length;
    if (!agentImagesSupported) {
      const textOnly = accepted.filter(item => !item.file.type.startsWith('image/'));
      skipped += accepted.length - textOnly.length; accepted = textOnly;
    }
    if (!accepted.length) { $('agent-state').textContent = !agentImagesSupported && items.some(item => item.file.type.startsWith('image/')) ? '当前模型不支持图片' : '没有可读取的文档或图片'; return; }
    items = accepted;
    const images = items.map(item => item.file).filter(file => /^image\/(jpeg|png|webp|gif)$/.test(file.type));
    const documents = items.filter(item => !/^image\/(jpeg|png|webp|gif)$/.test(item.file.type));
    if (agentFiles.length + documents.length > 12) { $('agent-state').textContent = '一次最多添加 12 个文档'; return; }
    agentPreparingImages = true; $('agent-send').disabled = true; $('agent-state').textContent = '正在读取附件…';
    try {
      let total = agentFiles.reduce((sum, item) => sum + item.size, 0);
      const loaded = [];
      for (const item of documents) {
        const file = item.file;
        if (!allowedAgentDocuments.test(item.name)) throw new Error('不支持这个文件格式：' + item.name);
        if (file.size > 2_000_000) throw new Error('单个文档不能超过 2 MB');
        total += file.size;
        if (total > 5_000_000) throw new Error('文档总量不能超过 5 MB');
        const dataUrl = await new Promise((resolve, reject) => {
          const reader = new FileReader(); reader.onload = () => resolve(reader.result);
          reader.onerror = () => reject(new Error('无法读取 ' + item.name)); reader.readAsDataURL(file);
        });
        loaded.push({name: item.name.replaceAll('\\', '/'), data: String(dataUrl).split(',')[1], size: file.size});
      }
      agentFiles.push(...loaded);
      renderAgentImages();
      if (images.length) { agentPreparingImages = false; await addAgentImages(images); }
      else $('agent-state').textContent = skipped ? `已跳过 ${skipped} 个不支持的文件` : '';
    } catch (error) { $('agent-state').textContent = error.message || '附件无法读取'; }
    finally { agentPreparingImages = false; $('agent-send').disabled = agentBusy; }
  }
  function setupAgentAddMenu() {
    const form = $('agent-form'), actions = form.lastElementChild;
    const conversation = form.parentElement;
    new ResizeObserver(() => conversation.style.setProperty('--agent-composer-height', `${form.getBoundingClientRect().height}px`)).observe(form);
    const oldAdd = $('agent-image-add'); oldAdd.style.display = 'none';
    const documents = document.createElement('input'); documents.type = 'file'; documents.multiple = true;
    documents.accept = agentDocumentTypes + ',image/png,image/jpeg,image/webp,image/gif'; documents.hidden = true;
    const folders = document.createElement('input'); folders.type = 'file'; folders.multiple = true;
    folders.setAttribute('webkitdirectory', ''); folders.hidden = true;
    form.append(documents, folders);
    const plus = document.createElement('button'); plus.type = 'button'; plus.className = 'agent-plus';
    plus.textContent = '＋'; plus.title = '添加'; plus.setAttribute('aria-label', '添加'); plus.setAttribute('aria-expanded', 'false');
    actions.insertBefore(plus, oldAdd);
    const menu = document.createElement('div'); menu.className = 'agent-add-menu'; menu.hidden = true; form.insertBefore(menu, actions);
    const heading = label => { const title = document.createElement('small'); title.textContent = label; menu.append(title); };
    const option = (icon, label, description, action) => {
      const button = document.createElement('button'); button.type = 'button';
      button.innerHTML = `<span class="agent-add-icon" aria-hidden="true">${icon}</span><span><strong>${label}</strong><em>${description}</em></span>`;
      button.addEventListener('click', () => { menu.hidden = true; plus.setAttribute('aria-expanded', 'false'); action(); });
      menu.append(button);
    };
    heading('添加');
    option('▤', '文件和文件夹', '文档、图片或文件夹', () => documents.click());
    option('▧', '选择文件夹', '添加文件夹中的文档', () => folders.click());
    option('◎', '目标', '设置本次对话的长期背景', () => {
      document.querySelector('[data-agent-section="memory"]')?.click(); $('agent-background').focus();
    });
    option('☷', '计划模式', '下一条消息只制定计划', () => {
      agentPlanOnly = !agentPlanOnly;
      plus.classList.toggle('plan-active', agentPlanOnly);
      plus.title = agentPlanOnly ? '计划模式已开启' : '添加';
      $('agent-state').textContent = agentPlanOnly ? '计划模式：下一条消息只制定计划' : '';
    });
    option('✎', '绘图', '手绘后作为图片发送', openAgentSketch);
    heading('工具箱');
    option('◫', '学习通', '打开学习通模块', openCourse);
    option('✓', '作业校对', '打开作业校对模块', openHomework);
    plus.addEventListener('click', () => { menu.hidden = !menu.hidden; plus.setAttribute('aria-expanded', String(!menu.hidden)); });
    document.addEventListener('click', event => { if (!form.contains(event.target)) { menu.hidden = true; plus.setAttribute('aria-expanded', 'false'); } });
    documents.addEventListener('change', async () => { await addAgentFiles([...documents.files].map(file => ({file, name: file.name}))); documents.value = ''; });
    folders.addEventListener('change', async () => { await addAgentFiles([...folders.files].map(file => ({file, name: file.webkitRelativePath || file.name}))); folders.value = ''; });
    const rate = document.createElement('span'); rate.id = 'agent-token-rate'; rate.hidden = true;
    actions.insertBefore(rate, $('agent-state'));
  }
  function openAgentSketch() {
    if (!agentImagesSupported) { $('agent-state').textContent = '当前模型不支持图片，请切换视觉模型'; return; }
    const dialog = document.createElement('dialog'); dialog.className = 'agent-sketch';
    const title = document.createElement('strong'); title.textContent = '绘图';
    const canvas = document.createElement('canvas'); canvas.width = 900; canvas.height = 520;
    const context = canvas.getContext('2d'); context.fillStyle = '#fff'; context.fillRect(0, 0, canvas.width, canvas.height);
    context.strokeStyle = '#202126'; context.lineWidth = 3; context.lineCap = 'round'; context.lineJoin = 'round';
    let drawing = false;
    const point = event => { const box = canvas.getBoundingClientRect(); return [(event.clientX - box.left) * canvas.width / box.width, (event.clientY - box.top) * canvas.height / box.height]; };
    canvas.addEventListener('pointerdown', event => { drawing = true; canvas.setPointerCapture(event.pointerId); const [x, y] = point(event); context.beginPath(); context.moveTo(x, y); });
    canvas.addEventListener('pointermove', event => { if (!drawing) return; const [x, y] = point(event); context.lineTo(x, y); context.stroke(); });
    canvas.addEventListener('pointerup', () => { drawing = false; });
    const actions = document.createElement('div');
    const clear = document.createElement('button'); clear.type = 'button'; clear.textContent = '清空';
    clear.addEventListener('click', () => { context.fillStyle = '#fff'; context.fillRect(0, 0, canvas.width, canvas.height); });
    const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = '取消'; cancel.addEventListener('click', () => dialog.close());
    const attach = document.createElement('button'); attach.type = 'button'; attach.textContent = '加入对话';
    attach.addEventListener('click', () => { if (agentImages.length >= 4) { $('agent-state').textContent = '最多添加 4 张图片'; return; } agentImages.push(canvas.toDataURL('image/jpeg', .85)); renderAgentImages(); dialog.close(); });
    actions.append(clear, cancel, attach); dialog.append(title, canvas, actions); document.body.append(dialog);
    dialog.addEventListener('close', () => dialog.remove()); dialog.showModal();
  }
  async function agentDroppedItems(transfer) {
    const entries = [...(transfer.items || [])].map(item => item.webkitGetAsEntry?.()).filter(Boolean);
    if (!entries.some(entry => entry.isDirectory)) return [...transfer.files].map(file => ({file, name: file.name}));
    const result = [];
    const visit = async (entry, parent = '', depth = 0) => {
      if (result.length >= 100) throw new Error('文件夹文件过多，请缩小范围');
      if (entry.isFile) {
        const file = await new Promise((resolve, reject) => entry.file(resolve, reject));
        result.push({file, name: parent + file.name}); return;
      }
      if (depth >= 6 || ['.git', 'node_modules', 'dist', 'bin', 'obj'].includes(entry.name)) return;
      const reader = entry.createReader();
      for (;;) {
        const children = await new Promise((resolve, reject) => reader.readEntries(resolve, reject));
        if (!children.length) break;
        for (const child of children) await visit(child, parent + entry.name + '/', depth + 1);
      }
    };
    for (const entry of entries) await visit(entry);
    return result;
  }
  function highlightAgentCode(element, language) {
    const source = element.textContent;
    const keywords = new Set('auto break case catch class const continue def delete do else enum export false finally for from function if import in interface let new null private protected public return static struct switch this throw true try typedef using var void while yield async await sizeof include int char float double bool long short unsigned signed string namespace template typename'.split(' '));
    const types = new Set('int char float double bool long short unsigned signed string String Number Boolean Array Object Dictionary List Task size_t uint32_t uint64_t'.split(' '));
    const tokens = /(\/\/[^\n]*|\/\*[\s\S]*?\*\/|#[^\n]*|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b\d+(?:\.\d+)?\b|\b[A-Za-z_$][\w$]*\b)/g;
    const fragment = document.createDocumentFragment();
    let offset = 0;
    for (const match of source.matchAll(tokens)) {
      if (match.index > offset) fragment.append(document.createTextNode(source.slice(offset, match.index)));
      const value = match[0]; let kind = '';
      if (value.startsWith('//') || value.startsWith('/*') || value.startsWith('#')) kind = value.startsWith('#') && language !== 'python' && language !== 'shell' ? 'directive' : 'comment';
      else if (/^["'`]/.test(value)) kind = 'string';
      else if (/^\d/.test(value)) kind = 'number';
      else if (types.has(value)) kind = 'type';
      else if (keywords.has(value)) kind = 'keyword';
      else if (/^\s*\(/.test(source.slice(match.index + value.length))) kind = 'function';
      if (kind) { const span = document.createElement('span'); span.className = 'agent-code-' + kind; span.textContent = value; fragment.append(span); }
      else fragment.append(document.createTextNode(value));
      offset = match.index + value.length;
    }
    if (offset < source.length) fragment.append(document.createTextNode(source.slice(offset)));
    element.replaceChildren(fragment);
  }
  function renderAgentMarkdown(container, source) {
    window.FaxAgentMarkdown.render(container, source, highlightAgentCode);
  }
  let agentStreamFrame = 0;
  let pendingAgentStream = null;
  function clearPendingAgentStream() {
    if (agentStreamFrame) cancelAnimationFrame(agentStreamFrame);
    agentStreamFrame = 0;
    pendingAgentStream = null;
  }
  function paintAgentStream() {
    agentStreamFrame = 0;
    const detail = pendingAgentStream;
    pendingAgentStream = null;
    if (detail === null) return;
    const timeline = $('agent-timeline');
    const follow = timeline.scrollHeight - timeline.scrollTop - timeline.clientHeight < 80;
    let draft = timeline.querySelector('.agent-stream');
    if (!detail) { draft?.remove(); return; }
    timeline.querySelector('.agent-empty')?.remove();
    if (!draft) {
      draft = document.createElement('div');
      draft.className = 'agent-entry agent-assistant agent-stream';
      const label = document.createElement('small'); label.textContent = 'Agent · 正在生成';
      const body = document.createElement('p'); draft.append(label, body); timeline.append(draft);
    }
    const body = draft.querySelector('p');
    if (body.textContent !== detail) body.textContent = detail;
    if (follow) timeline.scrollTop = timeline.scrollHeight;
  }
  function addAgentEvent(type, detail) {
    const timeline = $('agent-timeline');
    // New tools/status messages must not pull someone away from earlier text.
    // Snapshot replay scrolls once, after all entries have been restored.
    const follow = type === 'user' || timeline.scrollHeight - timeline.scrollTop - timeline.clientHeight < 80;
    const followLatest = () => { if (!restoringAgentSnapshot && follow) timeline.scrollTop = timeline.scrollHeight; };
    if (type === 'assistant' || type === 'error' || type === 'step') {
      clearPendingAgentStream();
      timeline.querySelector('.agent-stream')?.remove();
    }
    timeline.querySelector('.agent-empty')?.remove();
    if (type === 'step') {
      const [toolName, ...noteLines] = String(detail || '').split('\n');
      const known = ['plan', 'module_status', 'open_module', 'project_overview', 'list_files', 'read_file', 'search_files', 'create_file', 'replace_text', 'run_command', 'module_action'].includes(toolName);
      const operation = document.createElement('details'); operation.className = 'agent-operation'; operation.dataset.awaitingOutput = 'true';
      if (!restoringAgentSnapshot) animateNew(operation);
      const summary = document.createElement('summary');
      const icon = document.createElement('span'); icon.className = 'agent-operation-icon'; icon.textContent = ['create_file', 'replace_text'].includes(toolName) ? '✎' : toolName === 'open_module' ? '↗' : ['search_files', 'read_file'].includes(toolName) ? '⌕' : '≡';
      const title = document.createElement('strong'); title.className = 'agent-operation-title';
      title.textContent = known ? ({plan: '任务计划', module_status: '模块状态', open_module: '打开模块', project_overview: '项目概览', list_files: '列出文件', read_file: '读取文件', search_files: '搜索文件', create_file: '创建文件', replace_text: '修改文件', run_command: '运行命令', module_action: '模块操作'})[toolName] : (detail || '执行工具');
      summary.append(icon, title);
      if (known && noteLines.join('\n').trim()) { const note = document.createElement('small'); note.textContent = noteLines.join('\n').trim(); summary.append(note); }
      operation.append(summary); timeline.append(operation); followLatest(); return;
    }
    if (type === 'tool') {
      const operations = [...timeline.querySelectorAll('.agent-operation[data-awaiting-output]')];
      const operation = operations.at(-1);
      if (operation) {
        let data;
        try { data = JSON.parse(detail); } catch (_) { data = {result: detail || ''}; }
        const body = document.createElement('div'); body.className = 'agent-operation-body';
        const completed = {plan: '已规划步骤', module_status: '已查看模块状态', open_module: '已打开模块', project_overview: '已查看项目', list_files: '已列出文件', read_file: '已读取文件', search_files: '已搜索文件', run_command: '已运行命令', module_action: '已执行模块操作'};
        if (completed[data.name]) operation.querySelector('.agent-operation-title').textContent = completed[data.name];
        if (String(data.result || '').startsWith('用户拒绝')) operation.querySelector('.agent-operation-title').textContent = '已拒绝此操作';
        else if (data.name === 'module_action' && !String(data.result || '').startsWith('模块操作已处理')) operation.querySelector('.agent-operation-title').textContent = '模块操作未执行';
        else if (data.name === 'open_module' && !String(data.result || '').startsWith('已打开')) operation.querySelector('.agent-operation-title').textContent = '模块未打开';
        if (data.name === 'create_file' && String(data.result || '').startsWith('已创建 ')) operation.querySelector('.agent-operation-title').textContent = '已创建 1 个文件';
        if (data.name === 'replace_text' && String(data.result || '').startsWith('已修改 ')) operation.querySelector('.agent-operation-title').textContent = '已编辑 1 个文件';
        if (data.module) { const module = document.createElement('div'); module.className = 'agent-operation-file'; module.textContent = data.module; body.append(module); }
        if (data.path) {
          const file = document.createElement('div'); file.className = 'agent-operation-file';
          const path = document.createElement('span'); path.textContent = data.path; file.append(path);
          if (data.added || data.removed) {
            const counts = document.createElement('span'); counts.className = 'agent-operation-counts';
            const added = document.createElement('em'); added.textContent = '+' + (data.added || 0);
            const removed = document.createElement('em'); removed.textContent = '−' + (data.removed || 0);
            counts.append(added, removed); file.append(counts);
          }
          body.append(file);
        }
        if (data.before || data.after) {
          if (data.before) { const removed = document.createElement('pre'); removed.className = 'agent-operation-diff agent-diff-removed'; removed.textContent = '− ' + data.before.replaceAll('\n', '\n− '); body.append(removed); }
          if (data.after) { const added = document.createElement('pre'); added.className = 'agent-operation-diff agent-diff-added'; added.textContent = '+ ' + data.after.replaceAll('\n', '\n+ '); body.append(added); }
        } else if (data.result) { const output = document.createElement('pre'); output.textContent = data.result; body.append(output); }
        operation.append(body);
        delete operation.dataset.awaitingOutput; followLatest(); return;
      }
    }
    if (type === 'reasoning') {
      const reasoning = document.createElement('details'); reasoning.className = 'agent-reasoning';
      if (!restoringAgentSnapshot) animateNew(reasoning);
      const summary = document.createElement('summary');
      const icon = document.createElement('span'); icon.className = 'agent-operation-icon';
      icon.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 18h6m-5.5 3h5M12 2.5a6.5 6.5 0 0 0-3.6 11.9c.9.6 1.4 1.4 1.5 2.1h4.2c.1-.7.6-1.5 1.5-2.1A6.5 6.5 0 0 0 12 2.5z"/></svg>';
      const title = document.createElement('span'); title.textContent = '思考';
      summary.append(icon, title);
      const body = document.createElement('p'); body.textContent = detail || '';
      reasoning.append(summary, body); timeline.append(reasoning); followLatest(); return;
    }
    const entry = document.createElement('div'); entry.className = 'agent-entry agent-' + (['user', 'assistant', 'step', 'tool', 'error', 'status'].includes(type) ? type : 'status');
    if (!restoringAgentSnapshot) animateNew(entry);
    const label = document.createElement('small'); label.textContent = ({user: '你', assistant: 'Agent', step: '步骤', tool: '工具', error: '错误', status: '状态'})[type] || '状态';
    entry.append(label);
    if (type === 'assistant') renderAgentMarkdown(entry, detail);
    else { const body = document.createElement('p'); body.textContent = detail || ''; entry.append(body); }
    timeline.append(entry); followLatest();
  }
  function renderAgentMessage(message) {
    if (message.kind === 'agent.event') addAgentEvent(message.extra?.type, message.detail);
    if (message.kind === 'agent.stream') {
      // Multiple network chunks share a paint, instead of forcing a layout
      // and scrollbar update for every callback.
      pendingAgentStream = message.detail || '';
      if (!agentStreamFrame) agentStreamFrame = requestAnimationFrame(paintAgentStream);
    }
    if (message.kind === 'agent.options') {
      agentMode = message.extra?.mode === 'code' ? 'code' : 'chat';
      agentEffort = ['quick', 'normal', 'deep'].includes(message.extra?.effort) ? message.extra.effort : 'normal';
      agentProvider = message.extra?.provider || agentProvider;
      agentModel = message.extra?.model || '';
      if (document.activeElement !== $('agent-system-prompt')) $('agent-system-prompt').value = message.extra?.systemPrompt || '';
      updateAgentRunOptions();
    }
    if (message.kind === 'agent.memory') {
      if (document.activeElement !== $('agent-background')) $('agent-background').value = message.extra?.background || '';
      if (document.activeElement !== $('agent-memory-pool')) $('agent-memory-pool').value = message.extra?.summary || '';
      $('agent-memory-count').textContent = (message.extra?.archived || 0) ? `· 已整理 ${message.extra.archived} 条` : '';
    }
    if (message.kind === 'agent.models') {
      agentInstalledModels = Array.isArray(message.extra?.models) ? message.extra.models : [];
      renderAgentModelList();
      $('agent-model-error').textContent = message.extra?.error || '';
    }
    if (message.kind === 'agent.clipboard.files') {
      if (message.extra?.error) $('agent-state').textContent = message.extra.error;
      else {
        const files = Array.isArray(message.extra?.files) ? message.extra.files : [];
        if (agentFiles.length + files.length > 12 ||
            agentFiles.reduce((sum, file) => sum + file.size, 0) + files.reduce((sum, file) => sum + file.size, 0) > 5_000_000)
          $('agent-state').textContent = '附件超过 12 个或总量超过 5 MB';
        else { agentFiles.push(...files); renderAgentImages(); $('agent-state').textContent = ''; }
      }
    }
    if (message.kind === 'agent.sessions') {
      const nextId = message.extra?.activeId || '';
      if (nextId !== activeAgentSessionId && activeAgentSessionId)
        agentDrafts.set(activeAgentSessionId, {prompt: $('agent-prompt').value, images: agentImages, files: agentFiles});
      activeAgentSessionId = nextId;
      renderAgentSessions(message.extra || {});
    }
    if (message.kind === 'agent.session-meta') {
      $('agent-task-title').textContent = message.extra?.title || '新任务';
      $('agent-root').textContent = message.extra?.root || '未选择项目文件夹';
    }
    if (message.kind === 'agent.snapshot') {
      clearPendingAgentStream();
      const data = message.extra || {};
      $('agent-task-title').textContent = data.title || '新任务';
      $('agent-root').textContent = data.root || '未选择项目文件夹';
      agentMode = data.mode === 'code' ? 'code' : 'chat';
      agentEffort = ['quick', 'normal', 'deep'].includes(data.effort) ? data.effort : 'normal';
      $('agent-background').value = data.background || '';
      $('agent-memory-pool').value = data.memory || '';
      $('agent-memory-count').textContent = data.archived ? `· 已整理 ${data.archived} 条` : '';
      updateAgentRunOptions();
      const timeline = $('agent-timeline'); timeline.replaceChildren();
      restoringAgentSnapshot = true;
      try { for (const entry of data.events || []) addAgentEvent(entry.type, entry.text); }
      finally { restoringAgentSnapshot = false; }
      timeline.scrollTop = timeline.scrollHeight;
      if (!timeline.childElementCount) { const empty = document.createElement('div'); empty.className = 'agent-empty'; const title = document.createElement('strong'); title.textContent = agentMode === 'code' ? '描述任务，Agent 会记录执行过程。' : '开始对话'; empty.append(title); timeline.append(empty); }
      const draft = agentDrafts.get(activeAgentSessionId);
      $('agent-prompt').value = draft?.prompt || '';
      agentImages = draft?.images || []; agentFiles = draft?.files || []; renderAgentImages();
      renderAgentUsage(data);
      $('agent-continue').hidden = !data.needsContinuation;
      $('agent-continue').disabled = agentBusy;
    }
    if (message.kind === 'agent.continuation') {
      $('agent-continue').hidden = !message.extra?.available;
      $('agent-continue').disabled = agentBusy;
    }
    if (message.kind === 'agent.state') {
      agentBusy = !!message.extra?.busy; $('agent-state').textContent = agentBusy ? (message.detail || '正在处理') : '';
      $('ai-agent-state').textContent = agentBusy ? '正在处理' : '等待任务';
      $('agent-send').disabled = agentBusy; $('agent-cancel').hidden = !agentBusy;
      $('agent-continue').disabled = agentBusy;
      if (!agentBusy) $('agent-token-rate').hidden = true;
      $('agent-folder').disabled = agentBusy; $('agent-clear').disabled = agentBusy;
      for (const button of document.querySelectorAll('.agent-session-row button')) button.disabled = agentBusy;
    }
    if (message.kind === 'agent.root') $('agent-root').textContent = message.detail || '未选择项目文件夹';
    if (message.kind === 'agent.usage') renderAgentUsage(message.extra || {});
    if (message.kind === 'agent.rate') {
      const rate = $('agent-token-rate'), speed = Number(message.extra?.perSecond || 0);
      rate.hidden = !agentBusy || speed <= 0;
      rate.textContent = rate.hidden ? '' : `${message.extra?.reasoning ? '思考 · ' : ''}${message.extra?.estimated ? '约 ' : ''}${Math.round(speed)} token/s`;
      rate.title = rate.hidden ? '' : `本次输出 ${message.extra?.estimated ? '约 ' : ''}${Number(message.extra?.tokens || 0).toLocaleString()} token`;
    }
    if (message.kind === 'agent.approval') { agentApprovalId = message.extra?.id || ''; $('agent-approval-title').textContent = message.detail || '等待确认'; $('agent-approval-detail').textContent = message.extra?.preview || ''; $('agent-approval').hidden = false; }
    if (message.kind === 'agent.approval.clear') { agentApprovalId = ''; $('agent-approval').hidden = true; }
    if (message.kind === 'agent.navigate') { if (message.detail === 'course') openCourse(); else if (message.detail === 'homework') openHomework(); else if (message.detail === 'ai') openAi(); else if (message.detail === 'proxy') openProxy(); }
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
    if (message.kind === 'ui.clipboard.paste' && contextPasteTarget?.isConnected) {
      const input = contextPasteTarget; contextPasteTarget = null; input.focus();
      document.execCommand('insertText', false, message.text || '');
    }
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
    if (message.kind.startsWith('agent.')) renderAgentMessage(message);
  });
  for (const item of document.querySelectorAll('[data-view]')) item.addEventListener('click', () => {
    if (item.dataset.view === 'workspace') showWorkspace();
    else if (item.dataset.view === 'course') openCourse();
    else if (item.dataset.view === 'homework') openHomework();
    else if (item.dataset.view === 'ai') openAi();
    else if (item.dataset.view === 'proxy') openProxy();
    else show(item.dataset.view);
  });
  $('open-tools-card').addEventListener('click', () => show('modules'));
  for (const item of document.querySelectorAll('[data-open]')) item.addEventListener('click', () => item.dataset.open === 'course' ? openCourse() : item.dataset.open === 'homework' ? openHomework() : item.dataset.open === 'ai' ? openAi() : item.dataset.open === 'proxy' ? openProxy() : openDemo());
  $('module-course').addEventListener('click', openCourse);
  $('module-homework').addEventListener('click', openHomework);
  $('module-ai').addEventListener('click', openAi);
  $('module-demo').addEventListener('click', openDemo);
  $('module-proxy').addEventListener('click', openProxy);
  window.FaxToolboxUi = Object.freeze({openCourse, openHomework, openDemo, openAi, openProxy, showHome: () => show('home'),
    goBack, applyThemeFromHost: theme => setTheme(theme, false),
    applyDeveloperModeFromHost: enabled => setDeveloperMode(enabled, false, false),
    refresh: () => { show(currentView || 'home', false); send('ai.get-config'); }});
  const contextMenu = $('toolbox-context-menu');
  const contextSubmenu = $('toolbox-context-submenu');
  let contextTarget = null;
  let contextPasteTarget = null;
  function hideContextSubmenu() { contextSubmenu.hidden = true; contextSubmenu.replaceChildren(); }
  function closeContextMenu() { contextMenu.hidden = true; contextMenu.replaceChildren(); hideContextSubmenu(); contextTarget = null; }
  function contextAction(label, run, enabled = true) { return {label, run, enabled}; }
  function buildSessionContextActions(row) {
    const session = agentSessionMeta.get(row.dataset.sessionId);
    if (!session) return [];
    const id = session.id;
    const enabled = !agentBusy;
    const make = (label, icon, run, shortcut = '', items) => ({label, icon, run, shortcut, items, enabled});
    const sections = [...new Set([...agentSessionMeta.values()].map(item => item.section).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'zh-CN'));
    return [
      make('重命名', 'edit', () => renameAgentSession(row), 'Alt+Ctrl+R'),
      make(session.pinned ? '取消置顶' : '置顶', 'pin', () => send('agent.session.pin', {sessionId: id}), 'Alt+Ctrl+P'),
      make(session.unread ? '标记为已读' : '标记为未读', 'eye', () => send('agent.session.unread', {sessionId: id}), 'Ctrl+Shift+U'),
      make(session.archived ? '取消归档' : '归档', 'archive', () => send('agent.session.archive', {sessionId: id}), 'Ctrl+Shift+A'),
      make('永久删除', 'trash', () => row.querySelector('.agent-session-delete')?.click()),
      {separator: true},
      make('项目', 'folder', null, '', [
        make('选择项目文件夹…', 'folder', () => send('agent.session.project', {sessionId: id})),
        ...(session.root ? [make('移出项目', 'remove', () => send('agent.session.project.clear', {sessionId: id}))] : []),
      ]),
      make('分区', 'list', null, '', [
        ...sections.map(section => make((section === session.section ? '✓ ' : '') + section, 'list', () => send('agent.session.section', {sessionId: id, section}))),
        make('新建分区…', 'plus', () => confirmAction('新建分区', '输入分区名称', name => send('agent.session.section', {sessionId: id, section: name}), {input: true, confirmLabel: '创建', placeholder: '分区名称'})),
        ...(session.section ? [make('移出分区', 'remove', () => send('agent.session.section', {sessionId: id, section: ''}))] : [])
      ]),
      {separator: true},
      make('分享', 'share', null, '', [
        make('导出 Markdown…', 'share', () => send('agent.session.export', {sessionId: id})),
        make('复制会话内容', 'copy', () => send('agent.session.copy', {sessionId: id, kind: 'conversation'}))
      ]),
      make('复制', 'copy', null, '', [
        make('复制标题', 'copy', () => send('agent.session.copy', {sessionId: id, kind: 'title'})),
        make('复制会话内容', 'copy', () => send('agent.session.copy', {sessionId: id, kind: 'conversation'})),
        make('复制会话 ID', 'copy', () => send('agent.session.copy', {sessionId: id, kind: 'id'}))
      ]),
      {separator: true},
      make('分叉', 'fork', () => send('agent.session.fork', {sessionId: id})),
      {separator: true},
      make('打开方式', 'open', null, '', [
        make('在当前标签中打开', 'open', () => row.querySelector('.agent-session-select')?.click()),
        make('在新标签中打开', 'plus', () => send('agent.session.open', {sessionId: id, mode: 'tab'})),
        make('在新窗口中打开', 'open', () => send('agent.session.open', {sessionId: id, mode: 'window'}))
      ]),
      make('在新窗口中打开', 'open', () => send('agent.session.open', {sessionId: id, mode: 'window'}))
    ];
  }
  function buildContextActions(target) {
    const sessionRow = target.closest('.agent-session-row');
    if (currentView === 'ai' && sessionRow) return buildSessionContextActions(sessionRow);
    const actions = [];
    const add = (label, run, enabled = true) => actions.push(contextAction(label, run, enabled));
    const click = id => $(id)?.click();
    const input = target.closest('input:not([type="checkbox"]):not([type="file"]),textarea,[contenteditable="true"]');
    const selected = String(window.getSelection()?.toString() || (input && 'selectionStart' in input ? input.value.slice(input.selectionStart, input.selectionEnd) : '')).trim();
    if (input) {
      add('剪切', () => { input.focus(); document.execCommand('cut'); }, !!selected);
      add('复制', () => { input.focus(); document.execCommand('copy'); }, !!selected);
      add('粘贴', () => { contextPasteTarget = input; send('ui.clipboard.paste'); });
      add('全选', () => { input.focus(); if (input.select) input.select(); else document.execCommand('selectAll'); });
    } else if (selected) add('复制所选内容', () => { send('ui.clipboard.copy', {text: selected.slice(0, 65536)}); });
    const moduleCard = target.closest('[data-open],#module-ai,#module-course,#module-homework,#module-demo');
    if (moduleCard && !moduleCard.hidden) add('打开' + (moduleCard.querySelector('strong')?.textContent || '工具'), () => moduleCard.click());
    const vaultRow = target.closest('.vault-row');
    if (currentView === 'vault' && vaultRow) add('删除此项', () => vaultRow.querySelector('button')?.click());
    const settingsItem = target.closest('[data-settings-panel]');
    if (currentView === 'settings' && settingsItem) add('打开' + settingsItem.querySelector('strong')?.textContent, () => settingsItem.click());
    const page = {
      home: [['查看所有工具', () => show('modules')], ['查看状态', () => show('activity')]],
      modules: [['打开 Agent', openAi], ['打开学习通', openCourse], ['打开作业校对', openHomework]],
      workspace: [['查看所有工具', () => show('modules')]],
      ai: [['新建会话', () => click('agent-clear'), !agentBusy], ['选择项目文件夹', () => click('agent-folder'), !agentBusy], ['模型配置', () => { document.querySelector('.agent-config-panel').open = true; click('ai-configure'); }]],
      course: [['返回学习通页面', () => click('course-open')], ['保存当前登录状态', () => click('course-save-session')], ['开始任务', () => click('course-start'), !$('course-start').disabled], ['暂停任务', () => click('course-pause'), !$('course-pause').disabled], ['结束任务', () => click('course-stop'), !$('course-stop').disabled], ['分析页面', () => click('course-analyze'), developerMode]],
      homework: [['查看学习通页面', () => click('homework-open')], ['查看校对结果', () => click('homework-show-results')], ['开始校对', () => click('homework-start'), !$('homework-start').disabled]],
      demo: [['开始观察', () => click('start'), !$('start').disabled], ['结束观察', () => click('cancel'), !$('cancel').disabled]],
      activity: [['导出运行日志', () => click('diagnostics-export')], ['检查当前网页', () => click('diagnostics-inspect'), developerMode], ['打开网页调试器', () => click('diagnostics-devtools'), developerMode]],
      settings: [['外观设置', () => document.querySelector('[data-settings-panel="appearance"]').click()], ['服务设置', () => document.querySelector('[data-settings-panel="services"]').click()], ['管理密码箱', () => show('vault')]],
      vault: [['添加账号', () => click('vault-add')], ['刷新列表', () => send('vault.list')]]
    }[currentView] || [];
    for (const [label, run, enabled = true] of page) if (!actions.some(item => item.label === label)) add(label, run, enabled);
    if (viewHistory.length) add('返回上一页', goBack);
    return actions;
  }
  function showContextMenu(event) {
    if (event.target.closest('.toolbox-context-menu')) { event.preventDefault(); return; }
    event.preventDefault();
    closeContextMenu();
    contextTarget = event.target;
    renderContextItems(contextMenu, buildContextActions(contextTarget));
    contextMenu.hidden = false;
    const width = contextMenu.offsetWidth, height = contextMenu.offsetHeight;
    contextMenu.style.left = Math.max(8, Math.min(event.clientX, innerWidth - width - 8)) + 'px';
    contextMenu.style.top = Math.max(8, Math.min(event.clientY, innerHeight - height - 8)) + 'px';
  }
  function contextIcon(name) {
    const paths = {
      edit: '<path d="M4 20h4l11-11-4-4L4 16zM13 7l4 4"/>',
      pin: '<path d="m9 3 6 6-1 3 3 3-2 2-3-3-3 1-6-6zM9 15l-5 5"/>',
      eye: '<path d="M2 12s4-6 10-6 10 6 10 6-4 6-10 6S2 12 2 12z"/><circle cx="12" cy="12" r="2.5"/>',
      archive: '<rect x="3" y="4" width="18" height="5" rx="1"/><path d="M5 9v11h14V9M10 13h4"/>',
      trash: '<path d="M4 7h16M9 7V4h6v3m3 0-1 13H7L6 7M10 11v6m4-6v6"/>',
      folder: '<path d="M3 7a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
      list: '<path d="M9 6h12M9 12h12M9 18h12M3 6h2M3 12h2M3 18h2"/>',
      share: '<path d="M12 16V3m0 0L7 8m5-5 5 5M4 15v5h16v-5"/>',
      copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3"/>',
      fork: '<circle cx="6" cy="4" r="2"/><circle cx="6" cy="20" r="2"/><circle cx="18" cy="12" r="2"/><path d="M6 6v12M8 12h8"/>',
      open: '<path d="M6 18 18 6M9 6h9v9"/>',
      plus: '<path d="M12 4v16M4 12h16"/>',
      remove: '<path d="M5 12h14"/>'
    };
    if (!paths[name]) return null;
    const icon = document.createElement('span'); icon.className = 'toolbox-context-icon';
    icon.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name]}</svg>`;
    return icon;
  }
  function renderContextItems(host, actions) {
    host.replaceChildren();
    host.classList.toggle('session-menu', actions.some(action => action.icon));
    for (const action of actions) {
      if (action.separator) { const line = document.createElement('div'); line.className = 'toolbox-context-separator'; host.append(line); continue; }
      const button = document.createElement('button'); button.type = 'button'; button.role = 'menuitem';
      button.disabled = action.enabled === false;
      const icon = contextIcon(action.icon); if (icon) button.append(icon);
      const label = document.createElement('span'); label.className = 'toolbox-context-label'; label.textContent = action.label; button.append(label);
      if (action.shortcut) { const hint = document.createElement('kbd'); hint.textContent = action.shortcut; button.append(hint); }
      if (action.items) { const arrow = document.createElement('span'); arrow.className = 'toolbox-context-arrow'; arrow.textContent = '›'; button.append(arrow); button.setAttribute('aria-haspopup', 'menu'); }
      if (host === contextMenu) button.addEventListener('pointerenter', () => action.items ? showContextSubmenu(action.items, button) : hideContextSubmenu());
      button.addEventListener('click', () => {
        if (action.items) { showContextSubmenu(action.items, button); contextSubmenu.querySelector('button:not(:disabled)')?.focus(); return; }
        closeContextMenu(); action.run?.();
      });
      host.append(button);
    }
  }
  function showContextSubmenu(items, trigger) {
    renderContextItems(contextSubmenu, items); contextSubmenu.hidden = false;
    const rect = trigger.getBoundingClientRect();
    const width = contextSubmenu.offsetWidth, height = contextSubmenu.offsetHeight;
    const right = rect.right + width + 5 <= innerWidth;
    contextSubmenu.style.left = Math.max(8, Math.min(right ? rect.right + 4 : rect.left - width - 4, innerWidth - width - 8)) + 'px';
    contextSubmenu.style.top = Math.max(8, Math.min(rect.top - 5, innerHeight - height - 8)) + 'px';
  }
  document.addEventListener('contextmenu', showContextMenu);
  document.addEventListener('pointerdown', event => { if (!event.target.closest('.toolbox-context-menu')) closeContextMenu(); }, true);
  document.addEventListener('scroll', event => { if (!event.target.closest?.('.toolbox-context-menu')) closeContextMenu(); }, true);
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !contextMenu.hidden) { event.preventDefault(); if (!contextSubmenu.hidden) hideContextSubmenu(); else closeContextMenu(); }
    if (event.key === 'F10' && event.shiftKey) { event.preventDefault(); const rect = (document.activeElement || document.body).getBoundingClientRect(); showContextMenu({target: document.activeElement || document.body, clientX: rect.left + 10, clientY: rect.bottom, preventDefault() {}}); contextMenu.querySelector('button:not(:disabled)')?.focus(); }
    if (currentView !== 'ai' || agentBusy || confirmation.open || !$('command-palette').hidden || /^(INPUT|TEXTAREA)$/.test(document.activeElement?.tagName || '')) return;
    const row = document.activeElement?.closest('.agent-session-row') || [...document.querySelectorAll('.agent-session-row')].find(item => item.dataset.sessionId === activeAgentSessionId);
    if (!row) return;
    const key = event.key.toLowerCase();
    if (event.altKey && event.ctrlKey && key === 'r') { event.preventDefault(); closeContextMenu(); renameAgentSession(row); }
    else if (event.altKey && event.ctrlKey && key === 'p') { event.preventDefault(); closeContextMenu(); send('agent.session.pin', {sessionId: row.dataset.sessionId}); }
    else if (event.ctrlKey && event.shiftKey && key === 'u') { event.preventDefault(); closeContextMenu(); send('agent.session.unread', {sessionId: row.dataset.sessionId}); }
    else if (event.ctrlKey && event.shiftKey && key === 'a') { event.preventDefault(); closeContextMenu(); send('agent.session.archive', {sessionId: row.dataset.sessionId}); }
  });
  window.FaxToolboxContextMenu = Object.freeze({openFor: (selector) => { const element = document.querySelector(selector); if (!element) return false; const rect = element.getBoundingClientRect(); showContextMenu({target: element, clientX: rect.left + 8, clientY: rect.bottom - 8, preventDefault() {}}); return !contextMenu.hidden; }});
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
  $('homework-ai-open').addEventListener('click', () => send('ai.configure', {consumer: 'homework'}));
  $('course-ai-open').addEventListener('click', () => send('ai.configure', {consumer: 'course'}));
  $('course-save-session').addEventListener('click', () => send('course.session.save'));
  $('course-analyze').addEventListener('click', () => send('course.analyze'));
  $('course-start').addEventListener('click', () => send('course.start'));
  $('course-chapters').addEventListener('change', () => { $('course-jump').disabled = !catalogLive || !$('course-chapters').value; });
  $('course-jump').addEventListener('click', () => send('course.jump', {chapterId: $('course-chapters').value}));
  $('course-pause').addEventListener('click', () => send('course.pause'));
  $('course-stop').addEventListener('click', () => send('course.stop'));
  $('answers-save').addEventListener('click', () => send('answers.save', {chapter: $('answers-chapter').value.trim(), answers: $('answers-text').value}));
  $('ai-configure').addEventListener('click', () => send('ai.configure'));
  for (const item of document.querySelectorAll('[data-ai-consumer]')) {
    const select = () => { $('ai-test-result').textContent = ''; send('ai.select', {consumer: item.dataset.aiConsumer}); };
    item.addEventListener('click', select);
    item.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); select(); } });
  }
  $('agent-folder').addEventListener('click', () => send('agent.folder'));
  $('agent-run-toggle').addEventListener('click', () => {
    const panel = $('agent-run-panel'); panel.hidden = !panel.hidden;
    $('agent-run-toggle').setAttribute('aria-expanded', String(!panel.hidden));
    if (!panel.hidden && agentProvider === 'ollama') send('agent.models');
    else if (!panel.hidden) renderAgentMessage({kind: 'agent.models', extra: {models: []}});
  });
  document.addEventListener('click', event => {
    if (!event.target.closest('.agent-run-settings')) {
      $('agent-run-panel').hidden = true; $('agent-run-toggle').setAttribute('aria-expanded', 'false');
    }
  });
  for (const radio of document.querySelectorAll('input[name="agent-mode"]')) radio.addEventListener('change', () => {
    if (!radio.checked || agentBusy) return;
    agentMode = radio.value; updateAgentRunOptions(); send('agent.options', {mode: agentMode, effort: agentEffort});
  });
  $('agent-effort').addEventListener('input', event => {
    agentEffort = ['quick', 'normal', 'deep'][Number(event.target.value)] || 'normal'; updateAgentRunOptions();
  });
  $('agent-effort').addEventListener('change', () => {
    if (!agentBusy) send('agent.options', {mode: agentMode, effort: agentEffort});
  });
  $('agent-model-list').addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button || agentBusy) return;
    if (button.dataset.agentProvider && button.dataset.agentProvider !== agentProvider)
      send('agent.configure', {provider: button.dataset.agentProvider, model: button.dataset.agentModel});
    else if (button.classList.contains('agent-model-configure')) send('agent.configure');
    else if (button.dataset.agentModel && button.dataset.agentModel !== agentModel)
      send('agent.model', {model: button.dataset.agentModel});
    $('agent-run-panel').hidden = true; $('agent-run-toggle').setAttribute('aria-expanded', 'false');
  });
  $('agent-background').addEventListener('change', event => send('agent.background', {text: event.target.value}));
  $('agent-memory-pool').addEventListener('change', event => send('agent.memory', {text: event.target.value}));
  $('agent-system-prompt').addEventListener('change', event => send('agent.system', {text: event.target.value}));
  $('agent-sidebar-toggle').addEventListener('click', () => { const sidebar = document.querySelector('.agent-side'); sidebar.classList.toggle('mobile-open'); $('agent-sidebar-toggle').setAttribute('aria-expanded', String(sidebar.classList.contains('mobile-open'))); });
  $('agent-search-toggle').addEventListener('click', () => { const input = $('agent-session-search'); input.hidden = !input.hidden; if (!input.hidden) input.focus(); else { input.value = ''; agentSessionSearch = ''; filterAgentSessions(); } });
  $('agent-session-search').addEventListener('input', event => { agentSessionSearch = event.target.value.trim().toLocaleLowerCase(); filterAgentSessions(); });
  setupAgentAddMenu();
  $('agent-image-file').addEventListener('change', event => addAgentImages([...event.target.files]));
  $('agent-prompt').addEventListener('paste', event => {
    const files = [...(event.clipboardData?.files || [])];
    if (files.some(file => !allowedAgentDocuments.test(file.name) && !file.type.startsWith('image/') && file.size === 0)) {
      event.preventDefault(); send('agent.clipboard'); return;
    }
    if (files.length) { event.preventDefault(); addAgentFiles(files.map(file => ({file, name: file.name}))); }
    else send('agent.clipboard');
  });
  const dropZone = document.querySelector('#view-ai .agent-conversation');
  dropZone.addEventListener('dragover', event => { if (![...(event.dataTransfer?.types || [])].includes('Files')) return; event.preventDefault(); dropZone.classList.add('agent-drop-target'); });
  dropZone.addEventListener('dragleave', event => { if (!dropZone.contains(event.relatedTarget)) dropZone.classList.remove('agent-drop-target'); });
  dropZone.addEventListener('drop', async event => {
    event.preventDefault(); dropZone.classList.remove('agent-drop-target');
    try { await addAgentFiles(await agentDroppedItems(event.dataTransfer)); }
    catch (error) { $('agent-state').textContent = error.message || '无法读取拖入内容'; }
  });
  $('agent-clear').addEventListener('click', () => send('agent.clear'));
  $('agent-continue').addEventListener('click', () => { if (!agentBusy && !agentPreparingImages) send('agent.continue'); });
  $('agent-cancel').addEventListener('click', () => send('agent.cancel'));
  $('agent-approve').addEventListener('click', () => { if (agentApprovalId) send('agent.approve', {approvalId: agentApprovalId}); });
  $('agent-reject').addEventListener('click', () => { if (agentApprovalId) send('agent.reject', {approvalId: agentApprovalId}); });
  $('agent-form').addEventListener('submit', event => { event.preventDefault(); const prompt = $('agent-prompt').value.trim(); if ((!prompt && !agentImages.length && !agentFiles.length) || agentBusy || agentPreparingImages) return; const images = agentImages, files = agentFiles.map(({name, data}) => ({name, data})); const planOnly = agentPlanOnly; agentImages = []; agentFiles = []; agentPlanOnly = false; document.querySelector('.agent-plus').classList.remove('plan-active'); renderAgentImages(); $('agent-prompt').value = ''; agentDrafts.delete(activeAgentSessionId); send('agent.send', {prompt, images, files, planOnly}); });
  $('agent-prompt').addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); $('agent-form').requestSubmit(); } });
  document.addEventListener('keydown', event => { if (currentView === 'ai' && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'n') { event.preventDefault(); $('agent-clear').click(); $('agent-prompt').focus(); } });
  $('ai-test').addEventListener('click', () => send('ai.test'));
  $('ai-balance-refresh').addEventListener('click', () => send('ai.balance.refresh'));
  $('vault-add').addEventListener('click', () => send('vault.save'));
  $('settings-vault').addEventListener('click', () => show('vault'));
  $('settings-open-ai').addEventListener('click', openAi);
  for (const item of document.querySelectorAll('[data-settings-panel]')) item.addEventListener('click', () => {
    const current = document.querySelector('.settings-detail [data-panel]:not([hidden])');
    for (const button of document.querySelectorAll('[data-settings-panel]')) button.classList.toggle('selected', button === item);
    for (const panel of document.querySelectorAll('[data-panel]')) {
      panel.classList.remove('motion-new');
      panel.hidden = panel.dataset.panel !== item.dataset.settingsPanel;
    }
    const next = document.querySelector('.settings-detail [data-panel]:not([hidden])');
    if (current !== next && next) animateNew(next);
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
  let suppressFlyoutUntilLeave = false;
  const hideFlyoutNow = () => { clearTimeout(flyoutOpenTimer); clearTimeout(flyoutCloseTimer); toolGroup.classList.remove('flyout-open'); };
  const openFlyout = () => { clearTimeout(flyoutCloseTimer); clearTimeout(flyoutOpenTimer);
    if (!suppressFlyoutUntilLeave) flyoutOpenTimer = setTimeout(() => toolGroup.classList.add('flyout-open'), 380); };
  const closeFlyout = () => { clearTimeout(flyoutOpenTimer); clearTimeout(flyoutCloseTimer);
    flyoutCloseTimer = setTimeout(hideFlyoutNow, 180); };
  toolGroup.addEventListener('pointerenter', openFlyout);
  toolGroup.addEventListener('pointerleave', () => { suppressFlyoutUntilLeave = false; closeFlyout(); });
  toolGroup.querySelector('.nav-item').addEventListener('pointerdown', () => { suppressFlyoutUntilLeave = true; hideFlyoutNow(); });
  toolGroup.addEventListener('focusin', event => { if (!suppressFlyoutUntilLeave && event.target.matches(':focus-visible')) { clearTimeout(flyoutCloseTimer); toolGroup.classList.add('flyout-open'); } });
  toolGroup.addEventListener('focusout', () => setTimeout(() => {
    if (!toolGroup.contains(document.activeElement)) toolGroup.classList.remove('flyout-open');
  }, 0));
  toolGroup.querySelectorAll('.nav-flyout button').forEach(button => button.addEventListener('click', () => { suppressFlyoutUntilLeave = true; hideFlyoutNow(); }));
  for (const item of document.querySelectorAll('[data-theme]')) item.addEventListener('click', () => setTheme(item.dataset.theme));
  let savedTheme = 'light'; try { savedTheme = localStorage.getItem('fax-toolbox-theme') || 'light'; } catch (_) { /* Use light default. */ }
  setTheme(savedTheme);
  const initialModule = new URLSearchParams(location.search).get('module');
  if (initialModule === 'course') openCourse();
  else if (initialModule === 'homework') openHomework();
  else if (initialModule === 'demo') openDemo();
  else if (initialModule === 'ai') openAi();
  else if (initialModule === 'proxy') openProxy();
  else show('home');
  send('ai.get-config');
})();
