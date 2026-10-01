/* Read-only course index. It never reads forms, cookies, or network responses. */
(() => {
  'use strict';
  if (!location.hostname.toLowerCase().endsWith('.chaoxing.com')) return 'null';
  if (!window.FaxCatalogReader) {
    const idFromUrl = value => {
      if (!value) return '';
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
      const byNumber = new Map();
      const roots = document.querySelectorAll('#coursetree,.coursetree,#catalog,.catalog,#chapterTree,.ztree,[class*="catalog"],[class*="Catalog"],[id*="catalog"]');
      const root = Array.from(roots).find(item =>
        item.querySelectorAll('a,li,div,span').length >= 3 &&
        (item.textContent.match(/\d+(?:\.\d+)+\s*\p{L}/gu) || []).length >= 2) || document;
      const nodes = root.querySelectorAll('a,li,div,span,[role="treeitem"]');
      let order = 0;
      for (const element of nodes) {
        if (element.closest('.question,.StudentTimu,.timu,.TiMu,[class*="question"],[class*="Question"]')) continue;
        const name = clean(element.textContent);
        if (name.length < 4 || name.length > 80) continue;
        const label = name.match(/^(\d+(?:\.\d+)*)(?:\s*)(.+)$/);
        if (!label || /^\./.test(label[2]) || !/[\p{L}]/u.test(label[2]) ||
            /\s\d+(?:\.\d+)+\s*[\p{L}]/u.test(label[2]) || !visible(element)) continue;
        const number = label[1];
        const linkCandidate = element.closest('a') || element.querySelector('a');
        const link = linkCandidate && clean(linkCandidate.textContent) === name ? linkCandidate : null;
        const href = link?.getAttribute('href') || '';
        const data = element.dataset;
        const click = element.getAttribute('onclick') || link?.getAttribute('onclick') || '';
        const explicit = data.chapterId || data.knowledgeId || data.id ||
          link?.dataset.chapterId || link?.dataset.knowledgeId || link?.dataset.id || '';
        const match = click.match(/(?:chapterid|knowledgeid)[^\d]{0,16}(\d{3,})/i);
        const id = String(idFromUrl(href) || explicit || match?.[1] || hash(name));
        if (id.length > 128) continue;
        const classes = String(element.className) + ' ' + String(element.parentElement?.className || '');
        const active = /(^|[-_\s])(active|current|selected|on|doing)($|[-_\s])/i.test(classes) ||
          element.getAttribute('aria-current') === 'page';
        const previous = byNumber.get(number);
        if (!previous || name.length < previous.name.length ||
            (name.length === previous.name.length && active && !previous.active))
          byNumber.set(number, {id, number, name, element, active, order: previous?.order ?? order++});
      }
      const rows = Array.from(byNumber.values()).sort((a, b) => a.order - b.order).slice(0, 120);
      const counts = new Map();
      for (const row of rows) counts.set(row.id, (counts.get(row.id) || 0) + 1);
      for (const row of rows) if (counts.get(row.id) > 1) row.id = 'chapter:' + row.number;
      return rows;
    };
    window.FaxCatalogReader = {
      read() {
        const rows = collect();
        const urlId = idFromUrl(location.href);
        const heading = clean(document.querySelector('h1,h2,.chapterTitle,.chapter-title')?.textContent);
        const byHeading = heading && rows.find(row => row.name.replace(/^\d+(?:\.\d+)*\s+/, '') === heading);
        // The course often changes chapters without changing the outer page URL.
        // The heading is briefly replaced while Chaoxing swaps task panes. The
        // selected catalog row is a better source of chapter identity.
        const active = rows.find(row => row.active) || rows.find(row => row.id === urlId) || byHeading;
        const currentId = active?.id || urlId;
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
