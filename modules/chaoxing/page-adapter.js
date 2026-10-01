/* DOM adapter only. The Rust backend selects actions; no credentials or filesystem access. */
(() => {
  'use strict';
  const host = location.hostname.toLowerCase();
  if (!host.endsWith('.chaoxing.com') || !window.chrome?.webview) return;

  window.FaxCourseAdapter = {
    start(sessionId, generation) {
      if (window.__faxCourseAgentStarted === sessionId) return;
      window.__faxCourseAdapterStop?.();
      window.__faxCourseAgentStarted = sessionId;
      let active = true;
      let running = true;
      let seq = 0;
      let lastObservation = '';
      let lastAt = 0;
      let video = null;
      let quizSubmitted = false;
      let quizSubmitPhase = '';
      let quizSubmitStartedAt = 0;
      let quizSubmitButton = null;
      let contextKey = '';
      let lastAdvanceKey = '';
      let lastAdvanceAt = 0;
      const confirmationTargets = 'button,a,[role="button"],input[type="button"],input[type="submit"],div,span';
      let pendingInspection = 0;
      let pendingAction = 0;

      function chapter() {
        const query = new URLSearchParams(location.search);
        return query.get('chapterId') || query.get('knowledgeid') || query.get('chapterid') || '';
      }
      function pageContext() {
        const heading = document.querySelector('h1,h2,.chapterTitle,.chapter-title')?.textContent?.trim() || '';
        const selected = document.querySelector('#coursetree .active,.coursetree .active,#catalog .active,.catalog .active,#chapterTree .active,.ztree .curSelectedNode,[aria-current="page"]')?.textContent?.trim() || '';
        const taskTab = Array.from(document.querySelectorAll('[role="tab"],.tab,nav button,nav li'))
          .find(item => /(^|\s)(active|current|selected|on)(\s|$)/i.test(String(item.className)) ||
            item.getAttribute('aria-selected') === 'true')?.textContent?.trim() || '';
        return [location.href, heading.slice(0, 100), selected.slice(0, 100), taskTab.slice(0, 40)].join('|');
      }
      function refreshContext() {
        const current = pageContext();
        if (current === contextKey) return;
        contextKey = current;
        video = null;
        quizSubmitted = false;
        quizSubmitPhase = '';
        quizSubmitButton = null;
        lastObservation = '';
        lastAt = 0;
        if (pendingAction) { clearTimeout(pendingAction); pendingAction = 0; }
      }
      function report(kind, force = false) {
        if (!active || !running) return;
        const key = kind + ':' + contextKey;
        const now = Date.now();
        if (!force && key === lastObservation && now - lastAt < 3000) return;
        lastObservation = key; lastAt = now;
        window.chrome.webview.postMessage(JSON.stringify({v: 1, kind: 'course.observe', sessionId,
          generation, seq: ++seq, observation: kind, chapter: chapter()}));
      }
      function visible(element) {
        if (!element) return false;
        const box = element.getBoundingClientRect();
        return box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== 'hidden';
      }
      function byText(words, scope = document) {
        for (const element of scope.querySelectorAll('button,a,[role="button"],input[type="button"],input[type="submit"]')) {
          if (!visible(element)) continue;
          const label = (element.innerText || element.value || element.getAttribute('aria-label') || '').trim();
          if (words.some(word => label === word || label.startsWith(word + ' '))) return element;
        }
        return null;
      }
      function buttonLabel(element) {
        return (element.innerText || element.value || element.getAttribute('aria-label') || '').trim();
      }
      function confirmationButton() {
        const buttons = document.querySelectorAll(confirmationTargets);
        for (const button of buttons) {
          if (!visible(button) || !['提交', '确认提交'].includes(buttonLabel(button)) || button === quizSubmitButton) continue;
          for (let dialog = button.parentElement, depth = 0;
            dialog && depth < 12 && dialog !== document.body; dialog = dialog.parentElement, depth++) {
            if (!visible(dialog)) continue;
            const content = (dialog.innerText || dialog.textContent || '').replace(/\s+/g, ' ').trim();
            if (content.length > 240 || !/确认提交[？?]?/.test(content) ||
                /验证码|人机验证|安全验证|风险提示/.test(content) ||
                Array.from(dialog.querySelectorAll('input:not([type="button"]):not([type="submit"]),textarea,select,[contenteditable="true"]')).some(visible)) continue;
            const cancel = Array.from(dialog.querySelectorAll(confirmationTargets)).find(element =>
              visible(element) && buttonLabel(element) === '取消');
            if (cancel && cancel !== button && !button.disabled && button.getAttribute('aria-disabled') !== 'true')
              return button;
          }
        }
        return null;
      }
      function quizResultReady() {
        return /我的答案\s*[:：]/.test(document.body?.innerText || '');
      }
      function inspectQuizSubmission() {
        if (!quizSubmitPhase) return false;
        if (quizSubmitPhase === 'confirmed') {
          if (quizResultReady()) { report('QUIZ_RESULT_READY'); return true; }
          const remaining = findQuiz();
          const feedback = (document.body?.innerText || '').slice(-5000);
          if (remaining.length && /答错|回答错误|答案错误|选择错误|请重试|再试一次/.test(feedback)) {
            quizSubmitted = false; quizSubmitPhase = ''; quizSubmitButton = null;
            report('QUIZ_RETRY_NEEDED', true);
            return true;
          }
          if (remaining.length && /回答正确|答对了|选择正确/.test(feedback)) {
            quizSubmitted = false; quizSubmitPhase = ''; quizSubmitButton = null;
            report('QUIZ_RESOLVED', true);
            byText(['继续播放', '继续学习', '继续'])?.click();
            setTimeout(inspect, 250);
            return true;
          }
          if (!remaining.length && !confirmationButton()) {
            quizSubmitted = false; quizSubmitPhase = ''; quizSubmitButton = null;
            report('QUIZ_RESOLVED', true);
            setTimeout(inspect, 250);
            return true;
          }
          if (Date.now() - quizSubmitStartedAt > 15000) {
            quizSubmitPhase = 'needs-review'; report('QUIZ_SUBMIT_UNCONFIRMED', true);
          }
          return true;
        }
        if (quizSubmitPhase === 'needs-review') return true;
        const confirm = confirmationButton();
        if (confirm) {
          quizSubmitPhase = 'confirmed';
          confirm.click();
          report('QUIZ_SUBMITTED', true);
          return true;
        }
        const label = quizSubmitButton ? buttonLabel(quizSubmitButton) : '';
        const feedback = (document.body?.innerText || '').slice(-5000);
        if (/已提交|提交成功|已完成|查看成绩/.test(label) ||
            /答错|回答错误|答案错误|回答正确|答对了/.test(feedback)) {
          quizSubmitPhase = 'confirmed';
          report('QUIZ_SUBMITTED', true);
          return true;
        }
        if (Date.now() - quizSubmitStartedAt > 10000) {
          quizSubmitPhase = 'needs-review';
          report('QUIZ_SUBMIT_UNCONFIRMED', true);
        }
        return true;
      }
      function nextUnit() {
        if (window.top !== window) return;
        refreshContext();
        if (lastAdvanceKey === contextKey && Date.now() - lastAdvanceAt < 8000) return;
        const labels = /^(下一步|下一节|下一章|下一个任务点)$/;
        const candidates = document.querySelectorAll('#next,.nextBtn,.next_btn,[class*="next_btn"],button,a,[role="button"],input[type="button"],input[type="submit"]');
        const button = Array.from(candidates).find(element => visible(element) && !element.disabled &&
          element.getAttribute('aria-disabled') !== 'true' &&
          (labels.test(buttonLabel(element).replace(/\s+/g, ' ').trim()) ||
            element.matches('#next,.nextBtn,.next_btn,[class*="next_btn"]')));
        if (button) {
          lastAdvanceKey = contextKey; lastAdvanceAt = Date.now();
          button.click(); return;
        }
        const tree = document.querySelector('#coursetree, .coursetree, #catalog, .catalog, #chapterTree, .ztree');
        if (!tree) { report('NEXT_UNAVAILABLE'); return; }
        const nodes = Array.from(tree.querySelectorAll('a, .unit, .ncells')).filter(node =>
          node.textContent.trim() && node.textContent.trim().length < 100);
        const active = nodes.findIndex(node => /(^|\s)(on|active|current|doing)(\s|$)/i.test(
          String(node.className) + ' ' + String(node.parentElement?.className || '')));
        if (active >= 0) {
          const next = nodes.slice(active + 1).find(node => visible(node) && /^\s*[\d.]+/.test(node.textContent));
          if (next) { lastAdvanceKey = contextKey; lastAdvanceAt = Date.now(); next.click(); return; }
        }
        report('NEXT_UNAVAILABLE');
      }
      function advanceAfterVideo() {
        if (window.top !== window) return;
        if (findQuiz().length) { report('QUIZ_FOUND', true); return; }
        const tab = Array.from(document.querySelectorAll('button,a,li,[role="tab"]')).find(element =>
          visible(element) && /^(?:\d+\s*)?(?:测验|章节测验|检测|章节检测|作业)$/.test(
            (element.innerText || element.textContent || '').replace(/\s+/g, ' ').trim()));
        if (tab) {
          const active = tab.getAttribute('aria-selected') === 'true' ||
            /(^|\s)(active|current|on|selected)(\s|$)/i.test(String(tab.className));
          if (!active) tab.click();
          else inspect();
          return;
        }
        nextUnit();
      }
      const questionSelector = '.StudentTimu, .timu, .TiMu, [class*="Timu"], [class*="timu"], .questionLi, .questionWrap, .ans-question, .q_body, .question-item, .question, [data-question-id]';
      function findQuiz() {
        const controls = 'input[type="radio"],input[type="checkbox"],textarea,input[type="text"],input[type="number"],li[role="radio"],li[role="checkbox"],li[onclick*="addChoice"]';
        const candidates = Array.from(document.querySelectorAll(questionSelector)).filter(item =>
          visible(item) && item.querySelector(controls));
        // A chapter wrapper often has the same class family as each question.
        // Prefer the smallest matching container so one quiz is not mistaken for one question.
        const leaves = candidates.filter(item => !candidates.some(other => other !== item && item.contains(other)));
        if (leaves.length) return leaves;
        const inferred = new Set();
        const marker = /\b\d{1,3}\s*[【\[]\s*(?:单选题|多选题|判断题|填空题)\s*[】\]]/g;
        for (const control of document.querySelectorAll(controls)) {
          for (let parent = control.parentElement, depth = 0;
            parent && parent !== document.body && depth < 10; parent = parent.parentElement, depth++) {
            if (!visible(parent)) continue;
            const count = parent.querySelectorAll(controls).length;
            if (count < 2 || count > 12) continue;
            const label = (parent.innerText || parent.textContent || '').slice(0, 2600);
            const numbered = [...label.matchAll(marker)].length;
            const unnumbered = [...label.matchAll(/(?:^|\s)(?:单选题|多选题|判断题|填空题)(?=\s|$)/g)].length;
            if (label.length > 2400 || numbered + unnumbered !== 1) continue;
            inferred.add(parent);
            break;
          }
        }
        return Array.from(inferred).filter(item => !Array.from(inferred).some(other => other !== item && item.contains(other)));
      }
      function snapshotQuiz() {
        const questions = findQuiz();
        return {questions: (questions.length > 50 ? [] : questions).map((question, index) => {
          const custom = Array.from(question.querySelectorAll('li[role="radio"],li[role="checkbox"],li[onclick*="addChoice"]'));
          const inputs = custom.length ? custom : Array.from(question.querySelectorAll('input[type="radio"],input[type="checkbox"]'));
          const blanks = question.querySelectorAll('input[type="text"],input[type="number"],textarea');
          const options = inputs.slice(0, 8).map(option => {
            const label = option.tagName === 'LI' ? option :
              option.closest('label') || option.parentElement || option;
            return (label.innerText || label.textContent || option.value || '').trim().slice(0, 300);
          });
          const multi = inputs.some(option => option.matches('input[type="checkbox"],li[role="checkbox"]'));
          const hasUncapturedMedia = Array.from(question.querySelectorAll('img,canvas')).some(media =>
            visible(media) && media.getBoundingClientRect().width >= 48 &&
            media.getBoundingClientRect().height >= 32);
          return {number: index + 1, type: inputs.length ? (multi ? 'multi' : 'single') : 'blank',
            text: (question.innerText || question.textContent || '').trim().slice(0, 1200),
            options, blankCount: blanks.length, hasUncapturedMedia};
        })};
      }
      function fillQuiz(answers) {
        if (quizSubmitted) return;
        const questions = findQuiz();
        if (!questions.length || !Array.isArray(answers) || answers.length !== questions.length) {
          report('QUIZ_UNMATCHED', true); return;
        }
        for (const entry of answers) {
          const question = questions[Number(entry.number) - 1];
          const answer = String(entry.answer || '').trim();
          if (!question || !answer) { report('QUIZ_UNMATCHED', true); return; }
          const options = question?.querySelectorAll('li[role="radio"],li[role="checkbox"],li[onclick*="addChoice"]');
          const inputs = options?.length ? options : question?.querySelectorAll('input[type="radio"],input[type="checkbox"]');
          if (!inputs?.length) {
            const blanks = question.querySelectorAll('input[type="text"],input[type="number"],textarea');
            const values = answer.split('|').map(value => value.trim());
            if (!blanks.length || values.length !== blanks.length || values.some(value => !value)) {
              report('QUIZ_UNMATCHED', true); return;
            }
            for (let i = 0; i < blanks.length; i++) {
              const control = blanks[i];
              const prototype = control.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
              Object.getOwnPropertyDescriptor(prototype, 'value').set.call(control, values[i]);
              control.dispatchEvent(new Event('input', {bubbles: true}));
              control.dispatchEvent(new Event('change', {bubbles: true}));
            }
            continue;
          }
          let letters = [];
          if (/^[A-Ha-h,，\s]+$/.test(answer)) letters = answer.toUpperCase().replace(/[^A-H]/g, '').split('');
          else if (/^(对|错|正确|错误|true|false)$/i.test(answer)) {
            const truth = /^(对|正确|true)$/i.test(answer);
            const optionIndex = Array.from(inputs).findIndex(option =>
              truth ? /^(对|正确|true)$/i.test(option.textContent.trim()) :
                /^(错|错误|false)$/i.test(option.textContent.trim()));
            if (optionIndex >= 0) letters = [String.fromCharCode(65 + optionIndex)];
          }
          if (!letters.length) { report('QUIZ_UNMATCHED', true); return; }
          const selected = input => input.checked || input.getAttribute('aria-checked') === 'true' ||
            /(^|\s)(selected|checked)(\s|$)/i.test(String(input.className));
          if (Array.from(inputs).some(input => input.matches('input[type="checkbox"],li[role="checkbox"]'))) {
            for (let i = 0; i < inputs.length; i++) {
              const input = inputs[i];
              if (input.disabled) { report('QUIZ_UNMATCHED', true); return; }
              if (selected(input) !== letters.includes(String.fromCharCode(65 + i))) input.click();
            }
            continue;
          }
          for (const letter of letters) {
            const input = inputs[letter.charCodeAt(0) - 65];
            if (!input || input.disabled) { report('QUIZ_UNMATCHED', true); return; }
            if (!selected(input)) input.click();
          }
        }
        const submit = byText(['提交', '提交答案', '确认提交']);
        if (!submit || submit.disabled) { report('QUIZ_UNMATCHED', true); return; }
        quizSubmitted = true;
        quizSubmitPhase = 'requested';
        quizSubmitStartedAt = Date.now();
        quizSubmitButton = submit;
        submit.click();
        report('QUIZ_CONFIRM_PENDING', true);
        setTimeout(() => { if (active && running) inspect(); }, 100);
      }
      function findDocumentNext() {
        return document.querySelector('.nextPage, .next-page, [aria-label="下一页"]') ||
          byText(['下一页']);
      }
      function benignPromptButton() {
        const button = byText(['继续播放', '继续学习', '我知道了', '知道了']);
        if (!button) return null;
        const dialog = button.closest('[role="dialog"], .layui-layer, .modal, .dialog, .popup');
        if (!dialog || dialog.querySelector('input,textarea,select,[contenteditable="true"]')) return null;
        return button;
      }
      function challengeVisible() {
        const selectors = 'input[placeholder*="验证码"],input[name*="captcha"],iframe[src*="captcha"],iframe[src*="verify"],.geetest_panel,[id*="captcha"],[class*="captcha"]';
        if (Array.from(document.querySelectorAll(selectors)).some(visible)) return true;
        return Array.from(document.querySelectorAll('[role="dialog"],.layui-layer,.modal,.dialog,.popup')).some(dialog =>
          visible(dialog) && /验证码|人机验证|行为验证|账号异常|访问异常|安全验证|风险提示/.test((dialog.textContent || '').slice(0, 240)));
      }
      function inspect() {
        if (!active || !running) return;
        refreshContext();
        if (challengeVisible()) { report('CHALLENGE_FOUND'); return; }
        if (inspectQuizSubmission()) return;
        if (quizResultReady()) { report('QUIZ_RESULT_READY'); return; }
        if (benignPromptButton()) { report('PROMPT_FOUND'); return; }
        if (/\/knowledge\/cards/i.test(location.pathname)) {
          const tab = Array.from(document.querySelectorAll('[role="tab"],.tab,a,li')).find(element =>
            visible(element) && element.textContent.trim() === '视频' &&
            !/(^|\s)(on|active|current)(\s|$)/i.test(String(element.className)));
          if (tab) { report('VIDEO_TAB_FOUND'); return; }
        }
        // A quiz can be shown over a video that remains mounted and visible in the DOM.
        // In that state the questions, rather than the old video, own the current task.
        if (findQuiz().length && !quizSubmitted) { report('QUIZ_FOUND'); return; }
        const answerControls = document.querySelectorAll('input[type="radio"],input[type="checkbox"],li[role="radio"],li[role="checkbox"]').length;
        if (answerControls >= 2 && /单选题|多选题|判断题|章节测验|章节检测/.test((document.body?.innerText || '').slice(0, 3000))) {
          report('QUIZ_UNMATCHED'); return;
        }
        const foundVideo = document.querySelector('video');
        if (foundVideo && visible(foundVideo)) {
          foundVideo.defaultMuted = true;
          foundVideo.muted = true;
          if (video !== foundVideo) {
            video = foundVideo;
            video.addEventListener('ended', () => setTimeout(() => {
              if (findQuiz().length) inspect(); else report('VIDEO_ENDED', true);
            }, 350), {once: true});
          }
          if (foundVideo.ended) report('VIDEO_ENDED');
          else if (foundVideo.paused) report('VIDEO_FOUND');
          return;
        }
        if (/\/ppt\/|\/pdf\/|\/html\/|courseware/i.test(location.pathname)) {
          const next = findDocumentNext();
          if (next && !next.disabled && next.getAttribute('aria-disabled') !== 'true') report('DOCUMENT_FOUND');
          else if (document.readyState === 'complete') report('DOCUMENT_DONE');
        }
      }
      const onMessage = event => {
        if (!active || !running) return;
        const action = event.data;
        if (action?.v !== 1 || action?.kind !== 'course.action' ||
            action.sessionId !== sessionId || action.generation !== generation) return;
        switch (action.action) {
          case 'PLAY_VIDEO': if (video && !video.ended) {
            video.defaultMuted = true;
            video.muted = true;
            video.play().catch(error =>
              report(error?.name === 'NotAllowedError' ? 'PLAYBACK_BLOCKED' : 'PLAYBACK_FAILED', true));
          } break;
          case 'NEXT_UNIT': nextUnit(); break;
          case 'ADVANCE_AFTER_VIDEO': advanceAfterVideo(); break;
          case 'DOCUMENT_NEXT': {
            const next = findDocumentNext();
            if (next && !next.disabled && !pendingAction) pendingAction = setTimeout(() => {
              pendingAction = 0;
              if (!active || !running || next.disabled) return;
              scrollTo(0, document.body.scrollHeight); next.click();
            }, 1500);
            break;
          }
          case 'FILL_QUIZ': fillQuiz(action.data); break;
          case 'CONFIRM_QUIZ_SUBMISSION':
            if (!quizSubmitPhase) {
              quizSubmitPhase = 'requested';
              quizSubmitStartedAt = Date.now();
            }
            inspectQuizSubmission();
            break;
          case 'ACK_QUIZ_SUBMISSION': quizSubmitPhase = 'confirmed'; break;
          case 'DISMISS_PROMPT': benignPromptButton()?.click(); break;
          case 'SWITCH_VIDEO_TAB': {
            const tab = Array.from(document.querySelectorAll('[role="tab"],.tab,a,li')).find(element =>
              visible(element) && element.textContent.trim() === '视频');
            tab?.click(); break;
          }
        }
      };
      window.chrome.webview.addEventListener('message', onMessage);
      window.__faxCourseAdapterSnapshotQuiz = snapshotQuiz;
      window.__faxCourseAdapterDiagnose = () => ({questions: findQuiz().length,
        answerControls: document.querySelectorAll('input[type="radio"],input[type="checkbox"],li[role="radio"],li[role="checkbox"]').length,
        running, chapter: chapter(), lastObservation: lastObservation.split(':')[0] || '',
        submitPhase: quizSubmitPhase || 'idle', submitAgeMs: quizSubmitPhase ? Date.now() - quizSubmitStartedAt : 0,
        submitted: quizSubmitted, pendingInspection: !!pendingInspection, pendingAction: !!pendingAction,
        video: video ? {paused: video.paused, ended: video.ended, muted: video.muted,
          currentTime: Math.round(video.currentTime || 0)} : null});
      window.__faxCourseAdapterInspect = inspect;
      const observer = new MutationObserver(() => {
        if (!active || !running || document.readyState === 'loading' || pendingInspection) return;
        pendingInspection = setTimeout(() => { pendingInspection = 0; inspect(); }, 150);
      });
      const inspectAfterTaskTabClick = event => {
        const tab = event.target?.closest?.('button,a,li,[role="tab"]');
        if (!tab || !/(?:视频|测验|检测|作业)/.test(buttonLabel(tab))) return;
        setTimeout(inspect, 180);
        setTimeout(inspect, 850);
      };
      document.addEventListener('click', inspectAfterTaskTabClick, true);
      const ready = () => { if (active) { observer.observe(document.documentElement,
        {childList: true, subtree: true, attributes: true,
          attributeFilter: ['class', 'style', 'hidden', 'aria-selected']}); inspect(); } };
      if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, {once: true});
      else ready();
      const timer = setInterval(inspect, 2500);
      window.__faxCourseAdapterPause = () => {
        running = false;
        if (pendingAction) { clearTimeout(pendingAction); pendingAction = 0; }
        if (pendingInspection) { clearTimeout(pendingInspection); pendingInspection = 0; }
        if (video && !video.paused) video.pause();
      };
      window.__faxCourseAdapterResume = (inspectNow = true) => {
        if (!active) return;
        running = true;
        if (inspectNow) inspect();
      };
      window.__faxCourseAdapterStop = () => {
        active = false; observer.disconnect(); clearInterval(timer);
        if (pendingInspection) clearTimeout(pendingInspection);
        if (pendingAction) clearTimeout(pendingAction);
        window.chrome.webview.removeEventListener('message', onMessage);
        document.removeEventListener('click', inspectAfterTaskTabClick, true);
        window.__faxCourseAgentStarted = null;
        window.__faxCourseAdapterPause = null;
        window.__faxCourseAdapterResume = null;
        window.__faxCourseAdapterSnapshotQuiz = null;
        window.__faxCourseAdapterDiagnose = null;
        window.__faxCourseAdapterInspect = null;
      };
    },
    snapshotQuiz() { return window.__faxCourseAdapterSnapshotQuiz?.() ?? null; },
    diagnose() { return window.__faxCourseAdapterDiagnose?.() ?? null; },
    inspect() { window.__faxCourseAdapterInspect?.(); },
    pause() { window.__faxCourseAdapterPause?.(); },
    resume(inspectNow = true) { window.__faxCourseAdapterResume?.(inspectNow); },
    stop() { window.__faxCourseAdapterStop?.(); }
  };
})();
