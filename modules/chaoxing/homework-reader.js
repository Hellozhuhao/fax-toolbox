(() => {
  'use strict';
  // Read the complete current document in one pass. No clicks, edits, or submissions.
  if (!/(^|\.)chaoxing\.com$/i.test(location.hostname))
    return {questions: [], candidateCount: 0, skippedMarkerCount: 0, unreadableImageCount: 0};

  const clean = value => String(value || '').replace(/\s+/g, ' ').trim();
  const visible = element => {
    const box = element.getBoundingClientRect();
    return box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== 'hidden';
  };
  const marker = /此次\s*(?:作\s*业|作\s*为)\s*为\s*第\s*[_＿\s]*次\s*作\s*业/;
  let unreadableImageCount = 0;
  const imageIndices = new Map([...document.images].map((image, index) => [image, index]));
  const imageRefs = [];
  let currentQuestionNumber = 0;

  function imageMath(image) {
    let value = image.getAttribute('data-latex') || image.getAttribute('data-tex') ||
      image.getAttribute('data-formula') || image.getAttribute('data-math') ||
      image.getAttribute('aria-label') || image.getAttribute('alt') || image.getAttribute('title') || '';
    if (!value) {
      try {
        const url = new URL(image.currentSrc || image.src, location.href);
        for (const key of ['latex', 'tex', 'formula', 'math']) {
          value = url.searchParams.get(key) || '';
          if (value) break;
        }
      } catch (_) { /* Keep the missing-image marker. */ }
    }
    value = clean(value);
    const formulaLike = /[\\=+\-*/^_0-9∑∫√∞≤≥≠]|^[a-zA-Z]$/.test(value);
    if (value && formulaLike &&
      !/^(?:图片|image|img|公式|公式图片|数学公式|矩阵|行列式|点击查看大图)(?:\s*\d+)?$/i.test(value)) {
      if (/\\(?:begin|frac|sqrt|sum|left|right|alpha|beta)|\^\{|_\{|[&]{1,2}/.test(value))
        return `\\(${value}\\)`;
      return value;
    }
    unreadableImageCount++;
    const imageNumber = imageRefs.length + 1;
    const imageMarker = `[公式图片${imageNumber}]`;
    imageRefs.push({questionNumber: currentQuestionNumber, marker: imageMarker,
      imageIndex: imageIndices.get(image) ?? -1});
    return imageMarker;
  }

  function content(node, skipOptions = false) {
    if (node.nodeType === Node.TEXT_NODE) return node.nodeValue || '';
    if (node.nodeType !== Node.ELEMENT_NODE) return '';
    const element = node;
    if (element.matches('script,style,button,select,input,textarea,[contenteditable="true"],.analysis,.解析,.rightAnswer,.correctAnswer')) return '';
    if (skipOptions && element.matches('.stem_answer,.answerBg,.Zy_ulTop,.option,.options,.choice,.choices,.answer,.answers,label,li[role="radio"],li[role="checkbox"],li[onclick*="addChoice"]')) return '';
    if (element.tagName === 'IMG') return ` ${imageMath(element)} `;
    if (element.tagName === 'BR') return ' ';
    if (element.tagName === 'MATH') {
      const tex = element.querySelector('annotation[encoding*="tex"]')?.textContent;
      return tex ? ` \\(${tex}\\) ` : ` ${element.textContent || ''} `;
    }
    if (element.hasAttribute('data-latex')) return ` \\(${element.getAttribute('data-latex')}\\) `;
    return [...element.childNodes].map(child => content(child, skipOptions)).join('') +
      (/^(P|DIV|H[1-6]|LI)$/.test(element.tagName) ? ' ' : '');
  }

  const selectors = [
    '.mark_table form .questionLi', 'div[id^="question"].questionLi', '.questionLi',
    'div[id^="question"]', 'div[id^="sigleQuestionDiv"]', '.singleQuesId',
    '.TiMu.newTiMu', '.TiMu.divQuestion', '.divQuestion',
    '.StudentTimu,.timu,.TiMu,.questionWrap,.ans-question,.q_body,.question-item,.question,[data-question-id]'
  ];
  let nodes = [];
  for (const selector of selectors) {
    const found = [...document.querySelectorAll(selector)].filter(visible);
    if (found.length) { nodes = found; break; }
  }
  const questions = [];
  let skippedMarkerCount = 0;
  let unreadableQuestionCount = 0;
  for (const item of nodes) {
    const firstImageRef = imageRefs.length;
    currentQuestionNumber = questions.length + 1;
    const typeLabel = clean(item.getAttribute('typename') ||
      item.querySelector('.colorShallow,.newZy_TItle')?.textContent || '');
    const title = item.querySelector('h3.mark_name,.mark_name,.Zy_TItle,.question-title,.questionTitle,.TiMuTitle,.title,.stem');
    const text = clean(content(title || item, !title))
      .replace(/^\d+\s*[.．、]\s*/, '')
      .replace(/^[(（\[〖]?\s*(?:单选题|多选题|选择题|判断题|填空题|简答题|解答题|问答题|论述题)\s*[,，]?\s*\d*\s*分?\s*[)）\]〗]?\s*/, '')
      .trim();
    if (marker.test(text)) { imageRefs.length = firstImageRef; skippedMarkerCount++; continue; }
    if (text.length < 2) { imageRefs.length = firstImageRef; unreadableQuestionCount++; continue; }

    const typeHint = clean(`${typeLabel} ${title?.textContent || ''}`).slice(0, 100);
    const type = /多选题/.test(typeHint) ? 'multi' :
      /单选题|选择题|判断题/.test(typeHint) ? 'single' :
      /填空题/.test(typeHint) ? 'blank' :
      /简答题|解答题|问答题|论述题/.test(typeHint) ? 'essay' :
      item.querySelector('input[type="checkbox"],li[role="checkbox"]') ? 'multi' :
      item.querySelector('input[type="radio"],li[role="radio"]') ? 'single' :
      /_{2,}|＿{2,}|…{2,}/.test(text) ? 'blank' : 'essay';
    const marks = (text.match(/_{2,}|＿{2,}|…{2,}/g) || []).length;
    const blankCount = type === 'blank' ? Math.max(1, Math.min(8, marks || 1)) : 0;
    questions.push({number: questions.length + 1, type, text: text.slice(0, 2400),
      options: [], blankCount});
  }
  return {questions, imageRefs, candidateCount: nodes.length, skippedMarkerCount,
    unreadableQuestionCount, unreadableImageCount};
})()
