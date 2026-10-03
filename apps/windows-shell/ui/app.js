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
  const viewNames = {home: '首页', modules: '工具', workspace: '工作台', demo: '页面事件演示', course: '学习通', homework: '作业校对', ai: 'Agent', activity: '状态', settings: '设置', vault: '密码箱'};
  $('home-tool-count').textContent = String(document.querySelectorAll('#view-home .module-card:not([hidden])').length);
  const stateName = state => stateNames[state] || state || '未知';
  function send(command, extra = {}) { window.chrome?.webview?.postMessage(JSON.stringify({v: 1, id: crypto.randomUUID(), command, ...extra})); }
  function show(view, record = true) {
    if (record && currentView && currentView !== view) viewHistory.push(currentView);
    currentView = view;
    if (view !== 'ai') { document.querySelector('.agent-side')?.classList.remove('mobile-open'); $('agent-sidebar-toggle')?.setAttribute('aria-expanded', 'false'); }
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
    activeModule = 'ai'; $('workspace-module').textContent = 'Agent';
    $('workspace-detail').textContent = '处理项目任务，并管理各模块的模型连接。';
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
    const detail = message.error || (!message.configured ? '尚未配置模型' :
      `${message.provider} · ${message.model}\nAPI Key ${message.hasKey ? '已保存' : '未填写'}`);
    const consumer = message.consumer || 'course';
    $('ai-config-heading').textContent = consumer === 'agent' ? 'Agent 配置' : consumer === 'homework' ? '作业校对配置' : '学习通答题配置';
    for (const item of document.querySelectorAll('[data-ai-consumer]')) { item.classList.toggle('selected', item.dataset.aiConsumer === consumer); item.setAttribute('aria-pressed', String(item.dataset.aiConsumer === consumer)); }
    for (const item of message.consumers || []) {
      const target = $('ai-' + item.id + '-config');
      if (target) target.textContent = item.configured ? `${item.provider} · ${item.model}` : '尚未配置模型';
      if (item.id === 'course') $('course-ai-summary').textContent = item.configured ? `${item.provider} · ${item.model}` : '尚未配置学习通 AI';
      if (item.id === 'homework') $('homework-ai-summary').textContent = item.configured ? `${item.provider} · ${item.model}` : '尚未配置作业 AI';
      if (item.id === 'agent') {
        agentImagesSupported = !(item.provider === '阿里百炼' && item.model === 'qwen-plus');
        $('agent-image-file').disabled = !agentImagesSupported;
        $('agent-image-add').classList.toggle('disabled', !agentImagesSupported);
        $('agent-image-add').title = agentImagesSupported ? '添加图片，也可直接粘贴截图' : '当前 qwen-plus 预设仅支持文字';
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
  let agentBusy = false;
  let agentApprovalId = '';
  let agentPreparingImages = false;
  let agentImages = [];
  let agentImagesSupported = true;
  let activeAgentSessionId = '';
  const agentDrafts = new Map();
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
  function renderAgentSessions(data) {
    const list = $('agent-session-list'); list.replaceChildren();
    const groups = new Map();
    for (const session of data.sessions || []) {
      const project = session.root || '';
      if (!groups.has(project)) {
        const group = document.createElement('section'); group.className = 'agent-session-group';
        const heading = document.createElement('h4'); heading.textContent = project ? project.split(/[\\/]/).filter(Boolean).pop() : '最近'; heading.title = project;
        group.append(heading); list.append(group); groups.set(project, group);
      }
      const row = document.createElement('div'); row.className = 'agent-session-row'; row.dataset.sessionId = session.id;
      const select = document.createElement('button'); select.type = 'button'; select.className = 'agent-session-select';
      select.textContent = session.title || '新任务'; select.title = select.textContent;
      select.classList.toggle('selected', session.id === data.activeId); select.disabled = agentBusy;
      select.addEventListener('click', () => { if (!agentBusy && session.id !== activeAgentSessionId) send('agent.session.select', {sessionId: session.id}); document.querySelector('.agent-side').classList.remove('mobile-open'); $('agent-sidebar-toggle').setAttribute('aria-expanded', 'false'); });
      const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'agent-session-delete';
      remove.textContent = '×'; remove.title = '删除会话'; remove.setAttribute('aria-label', '删除会话 ' + select.textContent); remove.disabled = agentBusy;
      remove.addEventListener('click', () => { if (!agentBusy && confirm('删除这个 Agent 会话及其记录？')) send('agent.session.delete', {sessionId: session.id}); });
      row.append(select, remove); groups.get(project).append(row);
    }
    filterAgentSessions();
  }
  function renderAgentUsage(data) {
    const input = Number(data.input || 0), output = Number(data.output || 0);
    const rate = input > 0 && data.cached != null ? Math.round(100 * Number(data.cached) / input) + '%' : '—';
    $('agent-meter').hidden = !data.calls;
    const amount = data.cost == null ? '' : ` · 估算 ${data.currency === 'CNY' ? '¥' : '$'}${Number(data.cost).toFixed(4)}`;
    $('agent-meter').textContent = `输入 ${input.toLocaleString()} · 输出 ${output.toLocaleString()} · 缓存 ${rate}${amount}`;
    $('agent-usage').textContent = `${data.calls || 0} 次请求 · 输入 ${input.toLocaleString()} token · 输出 ${output.toLocaleString()} token · 缓存命中 ${rate}`;
    $('agent-cost').textContent = data.cost == null ? '本会话暂无费用估算' : `估算费用：${data.currency === 'CNY' ? '¥' : '$'}${Number(data.cost).toFixed(6)} ${data.currency || ''}`;
  }
  function renderAgentImages() {
    const tray = $('agent-attachments'); tray.replaceChildren(); tray.hidden = agentImages.length === 0;
    agentImages.forEach((dataUrl, index) => {
      const item = document.createElement('div'); item.className = 'agent-image';
      const preview = document.createElement('img'); preview.src = dataUrl; preview.alt = '待发送图片 ' + (index + 1);
      const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = '×'; remove.title = '移除图片';
      remove.addEventListener('click', () => { agentImages.splice(index, 1); renderAgentImages(); });
      item.append(preview, remove); tray.append(item);
    });
  }
  async function addAgentImages(files) {
    if (agentBusy || agentPreparingImages) return;
    if (!agentImagesSupported) { $('agent-state').textContent = '当前模型仅支持文字'; return; }
    if (agentImages.length + files.length > 4) { $('agent-state').textContent = '最多添加 4 张图片'; return; }
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
      renderAgentImages(); $('agent-state').textContent = '';
    } catch (error) { $('agent-state').textContent = error.message || '图片无法读取'; }
    finally { agentPreparingImages = false; $('agent-send').disabled = agentBusy; $('agent-image-file').value = ''; }
  }
  function renderAgentMarkdown(container, source) {
    const lines = String(source || '').split(/\r?\n/);
    let paragraph = [], list = null, listKind = '', code = null;
    const inline = (target, value) => {
      for (const part of value.split(/(`[^`]+`|\*\*[^*]+\*\*)/g)) {
        if (!part) continue;
        if (part.startsWith('`') && part.endsWith('`')) { const node = document.createElement('code'); node.textContent = part.slice(1, -1); target.append(node); }
        else if (part.startsWith('**') && part.endsWith('**')) { const node = document.createElement('strong'); node.textContent = part.slice(2, -2); target.append(node); }
        else target.append(document.createTextNode(part));
      }
    };
    const flushParagraph = () => { if (!paragraph.length) return; const node = document.createElement('p'); inline(node, paragraph.join('\n')); container.append(node); paragraph = []; };
    const closeList = () => { list = null; listKind = ''; };
    for (const line of lines) {
      if (line.trim().startsWith('```')) {
        flushParagraph(); closeList();
        if (code) { container.append(code); code = null; }
        else { code = document.createElement('pre'); const child = document.createElement('code'); code.append(child); }
        continue;
      }
      if (code) { code.firstChild.textContent += (code.firstChild.textContent ? '\n' : '') + line; continue; }
      if (!line.trim()) { flushParagraph(); closeList(); continue; }
      const heading = /^(#{1,3})\s+(.+)$/.exec(line);
      if (heading) { flushParagraph(); closeList(); const node = document.createElement('h' + Math.min(heading[1].length + 3, 6)); inline(node, heading[2]); container.append(node); continue; }
      const item = /^\s*(?:([-*])|(\d+)[.)])\s+(.+)$/.exec(line);
      if (item) { flushParagraph(); const kind = item[2] ? 'ol' : 'ul'; if (!list || listKind !== kind) { list = document.createElement(kind); container.append(list); listKind = kind; } const node = document.createElement('li'); inline(node, item[3]); list.append(node); continue; }
      closeList(); paragraph.push(line);
    }
    flushParagraph(); if (code) container.append(code);
  }
  function addAgentEvent(type, detail) {
    const timeline = $('agent-timeline');
    timeline.querySelector('.agent-empty')?.remove();
    if (type === 'step') {
      const operation = document.createElement('details'); operation.className = 'agent-operation'; operation.dataset.awaitingOutput = 'true';
      const summary = document.createElement('summary'); summary.textContent = detail || '执行工具';
      operation.append(summary); timeline.append(operation); timeline.scrollTop = timeline.scrollHeight; return;
    }
    if (type === 'tool') {
      const operations = [...timeline.querySelectorAll('.agent-operation[data-awaiting-output]')];
      const operation = operations.at(-1);
      if (operation) {
        const output = document.createElement('pre'); output.textContent = detail || ''; operation.append(output);
        delete operation.dataset.awaitingOutput; timeline.scrollTop = timeline.scrollHeight; return;
      }
    }
    const entry = document.createElement('div'); entry.className = 'agent-entry agent-' + (['user', 'assistant', 'step', 'tool', 'error', 'status'].includes(type) ? type : 'status');
    const label = document.createElement('small'); label.textContent = ({user: '你', assistant: 'Agent', step: '步骤', tool: '工具', error: '错误', status: '状态'})[type] || '状态';
    entry.append(label);
    if (type === 'assistant') renderAgentMarkdown(entry, detail);
    else { const body = document.createElement('p'); body.textContent = detail || ''; entry.append(body); }
    timeline.append(entry); timeline.scrollTop = timeline.scrollHeight;
  }
  function renderAgentMessage(message) {
    if (message.kind === 'agent.event') addAgentEvent(message.extra?.type, message.detail);
    if (message.kind === 'agent.sessions') {
      const nextId = message.extra?.activeId || '';
      if (nextId !== activeAgentSessionId && activeAgentSessionId)
        agentDrafts.set(activeAgentSessionId, {prompt: $('agent-prompt').value, images: agentImages});
      activeAgentSessionId = nextId;
      renderAgentSessions(message.extra || {});
    }
    if (message.kind === 'agent.snapshot') {
      const data = message.extra || {};
      $('agent-task-title').textContent = data.title || '新任务';
      $('agent-root').textContent = data.root || '未选择项目文件夹';
      const timeline = $('agent-timeline'); timeline.replaceChildren();
      for (const entry of data.events || []) addAgentEvent(entry.type, entry.text);
      if (!timeline.childElementCount) { const empty = document.createElement('div'); empty.className = 'agent-empty'; const title = document.createElement('strong'); title.textContent = '描述任务，Agent 会列出步骤并记录操作。'; empty.append(title); timeline.append(empty); }
      const draft = agentDrafts.get(activeAgentSessionId);
      $('agent-prompt').value = draft?.prompt || '';
      agentImages = draft?.images || []; renderAgentImages();
      renderAgentUsage(data);
    }
    if (message.kind === 'agent.state') {
      agentBusy = !!message.extra?.busy; $('agent-state').textContent = agentBusy ? (message.detail || '正在处理') : '';
      $('ai-agent-state').textContent = agentBusy ? '正在处理' : '等待任务';
      $('agent-send').disabled = agentBusy; $('agent-cancel').hidden = !agentBusy;
      $('agent-folder').disabled = agentBusy; $('agent-clear').disabled = agentBusy;
      for (const button of document.querySelectorAll('.agent-session-row button')) button.disabled = agentBusy;
    }
    if (message.kind === 'agent.root') $('agent-root').textContent = message.detail || '未选择项目文件夹';
    if (message.kind === 'agent.usage') renderAgentUsage(message.extra || {});
    if (message.kind === 'agent.approval') { agentApprovalId = message.extra?.id || ''; $('agent-approval-title').textContent = message.detail || '等待确认'; $('agent-approval-detail').textContent = message.extra?.preview || ''; $('agent-approval').hidden = false; }
    if (message.kind === 'agent.approval.clear') { agentApprovalId = ''; $('agent-approval').hidden = true; }
    if (message.kind === 'agent.navigate') { if (message.detail === 'course') openCourse(); else if (message.detail === 'homework') openHomework(); else if (message.detail === 'ai') openAi(); }
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
    else show(item.dataset.view);
  });
  $('open-tools-card').addEventListener('click', () => show('modules'));
  for (const item of document.querySelectorAll('[data-open]')) item.addEventListener('click', () => item.dataset.open === 'course' ? openCourse() : item.dataset.open === 'homework' ? openHomework() : item.dataset.open === 'ai' ? openAi() : openDemo());
  $('module-course').addEventListener('click', openCourse);
  $('module-homework').addEventListener('click', openHomework);
  $('module-ai').addEventListener('click', openAi);
  $('module-demo').addEventListener('click', openDemo);
  window.FaxToolboxUi = Object.freeze({openCourse, openHomework, openDemo, openAi, showHome: () => show('home'),
    goBack, applyThemeFromHost: theme => setTheme(theme, false),
    applyDeveloperModeFromHost: enabled => setDeveloperMode(enabled, false, false),
    refresh: () => { show(currentView || 'home', false); send('ai.get-config'); }});
  const contextMenu = $('toolbox-context-menu');
  let contextTarget = null;
  let contextPasteTarget = null;
  function closeContextMenu() { contextMenu.hidden = true; contextMenu.replaceChildren(); contextTarget = null; }
  function contextAction(label, run, enabled = true) { return {label, run, enabled}; }
  function buildContextActions(target) {
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
    const session = target.closest('.agent-session-row');
    if (currentView === 'ai' && session) {
      add('切换到此会话', () => session.querySelector('.agent-session-select')?.click(), !agentBusy);
      add('重命名会话', () => renameAgentSession(session), !agentBusy);
      add('删除此会话', () => session.querySelector('.agent-session-delete')?.click(), !agentBusy);
    }
    const vaultRow = target.closest('.vault-row');
    if (currentView === 'vault' && vaultRow) add('删除此项', () => vaultRow.querySelector('button')?.click());
    const settingsItem = target.closest('[data-settings-panel]');
    if (currentView === 'settings' && settingsItem) add('打开' + settingsItem.querySelector('strong')?.textContent, () => settingsItem.click());
    const page = {
      home: [['查看所有工具', () => show('modules')], ['打开 Agent', openAi]],
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
    if (event.target.closest('#toolbox-context-menu')) { event.preventDefault(); return; }
    event.preventDefault();
    closeContextMenu();
    contextTarget = event.target;
    for (const action of buildContextActions(contextTarget)) {
      const button = document.createElement('button'); button.type = 'button'; button.role = 'menuitem';
      button.textContent = action.label; button.disabled = !action.enabled;
      button.addEventListener('click', () => { closeContextMenu(); action.run(); });
      contextMenu.append(button);
    }
    contextMenu.hidden = false;
    const width = contextMenu.offsetWidth, height = contextMenu.offsetHeight;
    contextMenu.style.left = Math.max(8, Math.min(event.clientX, innerWidth - width - 8)) + 'px';
    contextMenu.style.top = Math.max(8, Math.min(event.clientY, innerHeight - height - 8)) + 'px';
  }
  document.addEventListener('contextmenu', showContextMenu);
  document.addEventListener('pointerdown', event => { if (!event.target.closest('#toolbox-context-menu')) closeContextMenu(); }, true);
  document.addEventListener('scroll', closeContextMenu, true);
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !contextMenu.hidden) { event.preventDefault(); closeContextMenu(); }
    if (event.key === 'F10' && event.shiftKey) { event.preventDefault(); const rect = (document.activeElement || document.body).getBoundingClientRect(); showContextMenu({target: document.activeElement || document.body, clientX: rect.left + 10, clientY: rect.bottom, preventDefault() {}}); contextMenu.querySelector('button:not(:disabled)')?.focus(); }
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
  for (const item of document.querySelectorAll('[data-ai-consumer]')) {
    const select = () => { $('ai-test-result').textContent = ''; send('ai.select', {consumer: item.dataset.aiConsumer}); };
    item.addEventListener('click', select);
    item.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); select(); } });
  }
  $('agent-folder').addEventListener('click', () => send('agent.folder'));
  $('agent-sidebar-toggle').addEventListener('click', () => { const sidebar = document.querySelector('.agent-side'); sidebar.classList.toggle('mobile-open'); $('agent-sidebar-toggle').setAttribute('aria-expanded', String(sidebar.classList.contains('mobile-open'))); });
  $('agent-inspector-toggle').addEventListener('click', () => { const panel = $('agent-inspector'); panel.hidden = !panel.hidden; $('agent-inspector-toggle').setAttribute('aria-expanded', String(!panel.hidden)); });
  $('agent-inspector-close').addEventListener('click', () => { $('agent-inspector').hidden = true; $('agent-inspector-toggle').setAttribute('aria-expanded', 'false'); });
  $('agent-search-toggle').addEventListener('click', () => { const input = $('agent-session-search'); input.hidden = !input.hidden; if (!input.hidden) input.focus(); else { input.value = ''; agentSessionSearch = ''; filterAgentSessions(); } });
  $('agent-session-search').addEventListener('input', event => { agentSessionSearch = event.target.value.trim().toLocaleLowerCase(); filterAgentSessions(); });
  $('agent-image-file').addEventListener('change', event => addAgentImages([...event.target.files]));
  $('agent-prompt').addEventListener('paste', event => { const files = [...(event.clipboardData?.files || [])].filter(file => file.type.startsWith('image/')); if (files.length) { event.preventDefault(); addAgentImages(files); } });
  $('agent-clear').addEventListener('click', () => send('agent.clear'));
  $('agent-cancel').addEventListener('click', () => send('agent.cancel'));
  $('agent-approve').addEventListener('click', () => { if (agentApprovalId) send('agent.approve', {approvalId: agentApprovalId}); });
  $('agent-reject').addEventListener('click', () => { if (agentApprovalId) send('agent.reject', {approvalId: agentApprovalId}); });
  $('agent-form').addEventListener('submit', event => { event.preventDefault(); const prompt = $('agent-prompt').value.trim(); if ((!prompt && !agentImages.length) || agentBusy || agentPreparingImages) return; const images = agentImages; agentImages = []; renderAgentImages(); $('agent-prompt').value = ''; agentDrafts.delete(activeAgentSessionId); send('agent.send', {prompt, images}); });
  $('agent-prompt').addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); $('agent-form').requestSubmit(); } });
  document.addEventListener('keydown', event => { if (currentView === 'ai' && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'n') { event.preventDefault(); $('agent-clear').click(); $('agent-prompt').focus(); } });
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
