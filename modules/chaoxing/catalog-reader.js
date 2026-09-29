/* Read-only course index. It never reads forms, cookies, or network responses. */
(() => {
  'use strict';
  if (!location.hostname.toLowerCase().endsWith('.chaoxing.com')) return 'null';
  if (!window.FaxCatalogReader) {
    const idFromUrl = value => {
      try {
        const query = new URL(value, location.href).searchParams;
        return query.get('chapterId') || query.get('chapterid') ||
          query.get('knowledgeId') || query.get('knowledgeid') || '';
      } catch (_) { return ''; }
    };
    const hash = value => {
      let result = 2166136261;
      for (const char of value) { result ^= char.codePointAt(0); result = Math.imul(result, 16777619); }
      return 'name:' + (result >>> 0).toString(16);
    };
    const clean = value => String(value || '').replace(/\s+/g, ' ').trim();
    const visible = element => {
      const box = element.getBoundingClientRect();
      return box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== 'hidden';
    };
    const collect = () => {
      const seen = new Set();
      const rows = [];
      const nodes = document.querySelectorAll('a,li,[role="treeitem"],[onclick],div[class*="chapter"],div[class*="Chapter"],div[class*="catalog"],div[class*="Catalog"],div[class*="node"],span[class*="chapter"],span[class*="catalog"]');
      for (const element of nodes) {
        if (rows.length >= 120 || !visible(element)) continue;
        const name = clean(element.innerText || element.textContent);
        if (name.length < 4 || name.length > 80 || !/^\d+(?:\.\d+)*\s+\S/.test(name)) continue;
        if (Array.from(element.children).some(child => clean(child.innerText) === name)) continue;
        const link = element.closest('a') || element.querySelector('a');
        const href = link?.getAttribute('href') || '';
        const data = element.dataset;
        const click = element.getAttribute('onclick') || link?.getAttribute('onclick') || '';
        const explicit = data.chapterId || data.knowledgeId || data.id ||
          link?.dataset.chapterId || link?.dataset.knowledgeId || link?.dataset.id || '';
        const match = click.match(/(?:chapterid|knowledgeid)[^\d]{0,16}(\d{3,})/i);
        const id = String(idFromUrl(href) || explicit || match?.[1] || hash(name));
        if (id.length > 128 || seen.has(id)) continue;
        seen.add(id);
        const classes = String(element.className) + ' ' + String(element.parentElement?.className || '');
        const active = /(^|[-_\s])(active|current|selected|on|doing)($|[-_\s])/i.test(classes) ||
          element.getAttribute('aria-current') === 'page';
        rows.push({id, name, element, active});
      }
      return rows;
    };
    window.FaxCatalogReader = {
      read() {
        const rows = collect();
        const currentId = idFromUrl(location.href);
        const heading = clean(document.querySelector('h1,h2,.chapterTitle,.chapter-title')?.textContent);
        const byHeading = heading && rows.find(row => row.name.replace(/^\d+(?:\.\d+)*\s+/, '') === heading);
        const active = rows.find(row => row.id === currentId) || byHeading || rows.find(row => row.active);
        const currentName = active?.name ||
          (heading.length >= 4 && heading.length <= 80 && !/^(章节详情|课程详情|我的课程|学习通|目录)$/.test(heading) ? heading : '');
        return {currentId, currentName, entries: rows.map(({id, name}) => ({id, name}))};
      },
      jump(id) {
        const row = collect().find(item => item.id === id);
        if (!row) return false;
        row.element.click();
        return true;
      }
    };
  }
  return JSON.stringify(window.FaxCatalogReader.read());
})()
