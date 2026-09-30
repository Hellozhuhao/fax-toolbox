(() => {
  'use strict';
  if (!window.matchMedia('(pointer: fine)').matches) return;

  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  let activeButton = null;
  let targetX = 0;
  let targetY = 0;
  let currentX = 0;
  let currentY = 0;
  let frame = 0;

  function animate() {
    frame = 0;
    if (!activeButton || activeButton.disabled || activeButton.getAttribute('aria-disabled') === 'true') return;
    const speed = reduceMotion ? 1 : 0.28;
    currentX += (targetX - currentX) * speed;
    currentY += (targetY - currentY) * speed;
    activeButton.style.setProperty('--glow-x', `${currentX}px`);
    activeButton.style.setProperty('--glow-y', `${currentY}px`);
    if (Math.abs(targetX - currentX) + Math.abs(targetY - currentY) > 0.4)
      frame = requestAnimationFrame(animate);
  }

  document.addEventListener('pointermove', event => {
    const button = event.target.closest('button:not(:disabled):not([aria-disabled="true"])');
    if (!button) { activeButton = null; return; }
    const bounds = button.getBoundingClientRect();
    targetX = event.clientX - bounds.left;
    targetY = event.clientY - bounds.top;
    if (button !== activeButton) {
      activeButton = button;
      currentX = targetX;
      currentY = targetY;
    }
    if (!frame) frame = requestAnimationFrame(animate);
  }, {passive: true});
})();
