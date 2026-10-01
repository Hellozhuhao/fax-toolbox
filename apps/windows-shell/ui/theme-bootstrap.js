(() => {
  try {
    document.documentElement.dataset.theme = localStorage.getItem('fax-toolbox-theme') === 'dark' ? 'dark' : 'light';
  } catch (_) {
    document.documentElement.dataset.theme = 'light';
  }
})();
