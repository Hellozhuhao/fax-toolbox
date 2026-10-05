/* DOM-only Agent answer renderer. Model output is never inserted as HTML. */
((root, factory) => {
  const renderer = factory();
  if (typeof module === 'object' && module.exports) module.exports = renderer;
  if (root) root.FaxAgentMarkdown = renderer;
})(typeof window === 'undefined' ? null : window, () => {
  const safeUrl = (raw, image = false) => {
    try {
      const url = new URL(raw.trim());
      if (url.protocol === 'https:' || (!image && url.protocol === 'http:') ||
          (!image && url.protocol === 'mailto:')) return url.href;
    } catch (_) { /* Display invalid links as text. */ }
    return null;
  };
  const splitRow = line => {
    let value = line.trim();
    if (value.startsWith('|')) value = value.slice(1);
    if (value.endsWith('|')) value = value.slice(0, -1);
    return value.split(/(?<!\\)\|/).map(cell => cell.trim().replace(/\\\|/g, '|'));
  };
  const separator = line => splitRow(line).every(cell => /^:?-{3,}:?$/.test(cell)) && splitRow(line).length > 1;

  function inline(target, value) {
    const pattern = /(!?\[[^\]\n]*\]\([^\s)]+(?:\s+"[^"]*")?\)|`[^`\n]+`|\*\*[^*\n]+\*\*|~~[^~\n]+~~|\*[^*\n]+\*)/g;
    let offset = 0;
    for (const match of String(value).matchAll(pattern)) {
      if (match.index > offset) target.append(document.createTextNode(value.slice(offset, match.index)));
      const token = match[0];
      const linked = /^(!?)\[([^\]]*)\]\(([^\s)]+)(?:\s+"[^"]*")?\)$/.exec(token);
      if (linked) {
        const image = linked[1] === '!';
        const href = safeUrl(linked[3], image);
        if (!href) target.append(document.createTextNode(token));
        else if (image) {
          const img = document.createElement('img');
          img.src = href; img.alt = linked[2]; img.loading = 'lazy';
          img.className = 'agent-inline-image';
          target.append(img);
        } else {
          const anchor = document.createElement('a');
          anchor.href = href; anchor.textContent = linked[2] || href;
          anchor.target = '_blank'; anchor.rel = 'noopener noreferrer';
          target.append(anchor);
        }
      } else if (token.startsWith('`')) {
        const code = document.createElement('code'); code.textContent = token.slice(1, -1); target.append(code);
      } else {
        const tag = token.startsWith('**') ? 'strong' : token.startsWith('~~') ? 'del' : 'em';
        const mark = tag === 'em' ? 1 : 2;
        const element = document.createElement(tag);
        element.textContent = token.slice(mark, -mark); target.append(element);
      }
      offset = match.index + token.length;
    }
    if (offset < value.length) target.append(document.createTextNode(value.slice(offset)));
  }

  function renderAnimation(container, source) {
    let data;
    try { data = JSON.parse(source); } catch (_) { return false; }
    if (!data || !Array.isArray(data.bars) || data.bars.length < 1 || data.bars.length > 12) return false;
    const card = document.createElement('figure'); card.className = 'agent-animation';
    if (typeof data.title === 'string' && data.title.trim()) {
      const caption = document.createElement('figcaption');
      caption.textContent = data.title.slice(0, 120); card.append(caption);
    }
    for (const bar of data.bars) {
      if (!bar || typeof bar.label !== 'string' || typeof bar.value !== 'number' ||
          !Number.isFinite(bar.value) || bar.value < 0 || bar.value > 100) return false;
      const row = document.createElement('div'); row.className = 'agent-animation-row';
      const label = document.createElement('span'); label.textContent = bar.label.slice(0, 80);
      const track = document.createElement('div'); track.className = 'agent-animation-track';
      const fill = document.createElement('span'); fill.style.setProperty('--agent-bar-width', bar.value + '%');
      track.append(fill);
      const number = document.createElement('span'); number.textContent = bar.value + '%';
      row.append(label, track, number); card.append(row);
    }
    container.append(card); return true;
  }

  function renderHtml(container, source) {
    if (typeof DOMParser === 'undefined' || source.length > 24000) return false;
    const parsed = new DOMParser().parseFromString(source, 'text/html');
    parsed.querySelectorAll('script,iframe,object,embed,link,meta,base,form').forEach(node => node.remove());
    parsed.querySelectorAll('*').forEach(node => {
      for (const attribute of [...node.attributes]) {
        const name = attribute.name.toLowerCase();
        if (name.startsWith('on') || name === 'srcset' || name === 'action' ||
            (name === 'href' && !attribute.value.startsWith('#')) ||
            (name === 'src' && !/^data:image\/(?:png|jpeg|gif|webp|svg\+xml);/i.test(attribute.value)))
          node.removeAttribute(attribute.name);
      }
    });
    const card = document.createElement('figure'); card.className = 'agent-visual-preview';
    const frame = document.createElement('iframe');
    frame.title = 'AI 生成的可视化'; frame.setAttribute('sandbox', 'allow-same-origin'); frame.setAttribute('referrerpolicy', 'no-referrer');
    const dark = document.documentElement.dataset.theme === 'dark';
    const palette = getComputedStyle(document.documentElement);
    const surface = palette.getPropertyValue('--surface').trim() || (dark ? '#25252b' : '#fafafa');
    const text = palette.getPropertyValue('--text').trim() || (dark ? '#eee' : '#222');
    frame.srcdoc = '<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src &#39;none&#39;; script-src &#39;none&#39;; style-src &#39;unsafe-inline&#39;; img-src data:; base-uri &#39;none&#39;; form-action &#39;none&#39;"><style>html{background:' +
      surface + ';color-scheme:' + (dark ? 'dark' : 'light') + '}body{margin:0;padding:16px;font:14px/1.6 system-ui,sans-serif;color:' +
      text + ';background:transparent}*{box-sizing:border-box}img,svg{max-width:100%}</style>' +
      parsed.head.innerHTML + '<style>@media(prefers-reduced-motion:reduce){*,*::before,*::after{animation:none!important;transition:none!important}}</style></head><body>' + parsed.body.innerHTML + '</body></html>';
    card.append(frame);
    const details = document.createElement('details');
    const summary = document.createElement('summary'); summary.textContent = '查看源码';
    const pre = document.createElement('pre'); const code = document.createElement('code');
    code.textContent = source; pre.append(code); details.append(summary, pre); card.append(details);
    container.append(card); return true;
  }

  function render(container, source, highlightCode = () => {}) {
    const lines = String(source || '').split(/\r?\n/);
    let paragraph = [], list = null, listKind = '', code = null, codeLanguage = '';
    const flushParagraph = () => {
      if (!paragraph.length) return;
      const node = document.createElement('p'); inline(node, paragraph.join('\n'));
      container.append(node); paragraph = [];
    };
    const closeList = () => { list = null; listKind = ''; };
    const flushCode = () => {
      if (!code) return;
      const content = code.join('\n');
      const visual = codeLanguage === 'animation' ? renderAnimation(container, content) :
        codeLanguage === 'html' ? renderHtml(container, content) : false;
      if (!visual) {
        const pre = document.createElement('pre');
        const element = document.createElement('code'); element.textContent = content;
        pre.append(element); highlightCode(element, codeLanguage); container.append(pre);
      }
      code = null; codeLanguage = '';
    };
    for (let index = 0; index < lines.length; index++) {
      const line = lines[index];
      if (line.trim().startsWith('```')) {
        flushParagraph(); closeList();
        if (code) flushCode();
        else { code = []; codeLanguage = line.trim().slice(3).trim().toLowerCase(); }
        continue;
      }
      if (code) { code.push(line); continue; }
      if (!line.trim()) { flushParagraph(); closeList(); continue; }
      if (index + 1 < lines.length && line.includes('|') && separator(lines[index + 1])) {
        const headers = splitRow(line), aligns = splitRow(lines[index + 1]);
        if (headers.length === aligns.length && headers.length <= 12) {
          flushParagraph(); closeList();
          const wrapper = document.createElement('div'); wrapper.className = 'agent-table-scroll';
          const table = document.createElement('table');
          const head = document.createElement('thead'); const headRow = document.createElement('tr');
          headers.forEach((cell, column) => {
            const th = document.createElement('th'); inline(th, cell);
            if (aligns[column].endsWith(':')) th.style.textAlign = aligns[column].startsWith(':') ? 'center' : 'right';
            headRow.append(th);
          });
          head.append(headRow); table.append(head);
          const body = document.createElement('tbody'); index++;
          while (index + 1 < lines.length && lines[index + 1].trim().includes('|')) {
            const cells = splitRow(lines[index + 1]);
            if (cells.length !== headers.length) break;
            index++;
            const row = document.createElement('tr');
            cells.forEach((cell, column) => {
              const td = document.createElement('td'); inline(td, cell);
              if (aligns[column].endsWith(':')) td.style.textAlign = aligns[column].startsWith(':') ? 'center' : 'right';
              row.append(td);
            });
            body.append(row);
          }
          table.append(body); wrapper.append(table); container.append(wrapper); continue;
        }
      }
      const heading = /^(#{1,3})\s+(.+)$/.exec(line);
      if (heading) {
        flushParagraph(); closeList();
        const node = document.createElement('h' + Math.min(heading[1].length + 3, 6));
        inline(node, heading[2]); container.append(node); continue;
      }
      const item = /^\s*(?:([-*])|(\d+)[.)])\s+(.+)$/.exec(line);
      if (item) {
        flushParagraph(); const kind = item[2] ? 'ol' : 'ul';
        if (!list || listKind !== kind) { list = document.createElement(kind); container.append(list); listKind = kind; }
        const node = document.createElement('li'); inline(node, item[3]); list.append(node); continue;
      }
      if (/^\s*---+\s*$/.test(line)) { flushParagraph(); closeList(); container.append(document.createElement('hr')); continue; }
      if (/^>\s?/.test(line)) {
        flushParagraph(); closeList();
        const node = document.createElement('blockquote'); inline(node, line.replace(/^>\s?/, '')); container.append(node); continue;
      }
      closeList(); paragraph.push(line);
    }
    flushParagraph(); flushCode();
  }
  return {render, safeUrl, splitRow, separator};
});
