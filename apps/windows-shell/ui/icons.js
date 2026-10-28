/* One geometric vocabulary for navigation, tools, menus and feedback. */
(() => {
  "use strict";
  const paths = {
    home: '<path d="m3 10 9-7 9 7v10H3zM9 20v-7h6v7"/>',
    tools:
      '<rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2"/>',
    agent:
      '<path d="M7 5h10a4 4 0 0 1 4 4v6a4 4 0 0 1-4 4h-4l-5 3v-3H7a4 4 0 0 1-4-4V9a4 4 0 0 1 4-4zM8 11h.01M16 11h.01M9 15h6M12 2v3"/>',
    course:
      '<path d="M3 4h6a4 4 0 0 1 3 2 4 4 0 0 1 3-2h6v15h-6a4 4 0 0 0-3 2 4 4 0 0 0-3-2H3zM12 6v15M6 8h3M6 12h3M15 8h3M15 12h3"/>',
    homework: '<path d="M6 3h9l4 4v14H6zM14 3v5h5M9 12h7m-7 4 2 2 4-4"/>',
    proxy:
      '<circle cx="12" cy="5" r="3"/><circle cx="5" cy="18" r="3"/><circle cx="19" cy="18" r="3"/><path d="m10 8-4 7m8-7 4 7M8 18h8"/>',
    android:
      '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M10 5h4m-3 14h2M2 8v8m20-8v8"/>',
    demo: '<path d="m8 7-5 5 5 5m8-10 5 5-5 5m-3-13-2 16"/>',
    activity: '<path d="M3 12h4l3-7 4 14 3-7h4"/><path d="M3 4v16"/>',
    settings:
      '<path d="M4 6h16M4 12h16M4 18h16"/><circle cx="8" cy="6" r="2"/><circle cx="16" cy="12" r="2"/><circle cx="10" cy="18" r="2"/>',
    search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/>',
    chevron: '<path d="m7 10 5 5 5-5"/>',
    arrow: '<path d="M4 12h16m-6-6 6 6-6 6"/>',
    back: '<path d="M20 12H4m6-6-6 6 6 6"/>',
    close: '<path d="m6 6 12 12M18 6 6 18"/>',
    check: '<path d="m5 12 4 4L19 6"/>',
    folder: '<path d="M3 7V5h7l2 3h9v12H3z"/>',
    plus: '<path d="M12 4v16M4 12h16"/>',
    target: '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1"/>',
    plan: '<path d="M9 6h11M9 12h11M9 18h11M3 6l1 1 2-2M3 12l1 1 2-2M3 18l1 1 2-2"/>',
    draw: '<path d="m4 16 11-11 4 4L8 20H4zM13 7l4 4M15 5l2-2 4 4-2 2"/>',
    menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
    send: '<path d="m3 3 19 9-19 9 4-9zM7 12h15"/>',
    attach: '<path d="m9 14 7-7a3 3 0 0 1 4 4l-9 9a5 5 0 0 1-7-7L14 3"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    skin: '<path d="m8 3-5 4 3 5 2-1v10h8V11l2 1 3-5-5-4a4 4 0 0 1-8 0z"/>',
    upload: '<path d="M12 16V3m-5 5 5-5 5 5M4 15v6h16v-6"/>',
    download: '<path d="M12 3v13m-5-5 5 5 5-5M4 16v5h16v-5"/>',
    shield: '<path d="m12 2 8 3v7c0 5-8 10-8 10S4 17 4 12V5zM8 12l3 3 5-6"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10v.1"/>',
    warning: '<path d="m12 3 10 18H2zM12 9v5m0 3v.1"/>',
    undo: '<path d="M4 10h10a6 6 0 0 1 0 12M4 10l5-5m-5 5 5 5"/>',
    more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
  };
  const svg = (name) =>
    `<svg class="ui-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[name] || paths.tools}</svg>`;
  function hydrate(root = document) {
    for (const node of root.querySelectorAll("[data-icon]")) {
      if (node.dataset.iconRendered === node.dataset.icon) continue;
      node.innerHTML = svg(node.dataset.icon);
      node.dataset.iconRendered = node.dataset.icon;
      node.setAttribute("aria-hidden", "true");
    }
  }
  window.FaxIcons = Object.freeze({ svg, hydrate });
  hydrate();
})();
