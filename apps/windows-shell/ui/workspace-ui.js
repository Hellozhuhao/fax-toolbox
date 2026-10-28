(() => {
  "use strict";
  const $ = (id) => document.getElementById(id),
    icon = (name) => window.FaxIcons.svg(name);
  const names = {
    ai: "Agent",
    course: "学习通",
    homework: "作业校对",
    proxy: "网络管家",
    android: "安卓投屏",
    demo: "页面事件演示",
  };
  const open = (id) =>
    window.FaxToolboxUi.skinActions["module." + id + ".open"]?.run({});
  let recent = [];
  try {
    recent = JSON.parse(localStorage.getItem("fax-recent-tools") || "[]")
      .filter((id) => id in names)
      .slice(0, 4);
  } catch (_) {}
  function renderRecent() {
    const items = recent.filter(
      (id) => id !== "demo" || $("developer-mode").checked,
    );
    $("home-recent").hidden = !items.length;
    $("recent-items").replaceChildren();
    for (const id of items) {
      const link = document.createElement("a");
      link.href = "#" + id;
      link.innerHTML = icon(id === "ai" ? "agent" : id);
      link.append(document.createTextNode(names[id]));
      link.addEventListener("click", (event) => {
        event.preventDefault();
        open(id);
      });
      $("recent-items").append(link);
    }
  }
  function syncNavigation() {
    for (const item of document.querySelectorAll(".sidebar .nav-item")) {
      const active = item.classList.contains("active");
      if (active) item.setAttribute("aria-current", "page");
      else item.removeAttribute("aria-current");
    }
  }
  addEventListener("fax:view-changed", (event) => {
    const view = event.detail.view;
    if (view in names) {
      recent = [view, ...recent.filter((id) => id !== view)].slice(0, 4);
      try {
        localStorage.setItem("fax-recent-tools", JSON.stringify(recent));
      } catch (_) {}
      renderRecent();
    }
    syncNavigation();
  });
  syncNavigation();
  renderRecent();
  $("home-prompt-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const text = $("home-prompt").value.trim();
    const previous = $("agent-prompt").value;
    if (
      text &&
      previous.trim() &&
      previous.trim() !== text &&
      !(await window.FaxToolboxUi.confirm({
        title: "替换未发送的草稿？",
        message: "Agent 中还有未发送的内容。继续后将用首页的内容替换草稿。",
        confirmLabel: "替换草稿",
      }))
    )
      return;
    open("ai");
    if (text) window.FaxToolboxUi.skinActions["agent.input"].run({ text });
    $("agent-prompt").focus();
  });
  function filterTools() {
    const query = $("tool-search").value.trim().toLocaleLowerCase();
    const category =
      document.querySelector('[name="tool-category"]:checked')?.value || "all";
    let count = 0;
    for (const card of document.querySelectorAll(
      ".catalog-list .library-card",
    )) {
      const shown =
        (!card.hasAttribute("data-developer-tool") ||
          $("developer-mode").checked) &&
        (category === "all" || category === card.dataset.category) &&
        (!query ||
          (card.textContent + " " + card.dataset.keywords)
            .toLocaleLowerCase()
            .includes(query));
      if (card.hidden === shown) card.hidden = !shown;
      if (shown) count++;
    }
    $("tool-empty").hidden = count > 0;
    $("tool-result-count").textContent = query
      ? `找到 ${count} 个工具`
      : `${count} 个工具`;
  }
  $("tool-search").addEventListener("input", filterTools);
  for (const input of document.querySelectorAll('[name="tool-category"]'))
    input.addEventListener("change", filterTools);
  $("tool-clear").addEventListener("click", () => {
    $("tool-search").value = "";
    document.querySelector('[name="tool-category"][value="all"]').checked =
      true;
    filterTools();
    $("tool-search").focus();
  });
  $("developer-mode").addEventListener("change", () => {
    if (
      !$("developer-mode").checked &&
      document.querySelector('[name="tool-category"]:checked')?.value === "开发"
    )
      document.querySelector('[name="tool-category"][value="all"]').checked =
        true;
    filterTools();
    renderRecent();
  });
  filterTools();

  // The Status column owns both the live session overview and appearance management.
  const activity = $("view-activity"),
    manager = $("skin-manager");
  manager.querySelector(".skin-selection").parentElement.hidden = true;
  const tabs = document.createElement("div");
  tabs.className = "surface-tabs";
  tabs.role = "tablist";
  tabs.setAttribute("aria-label", "状态栏目");
  const overview = document.createElement("section");
  overview.id = "status-overview";
  overview.role = "tabpanel";
  overview.setAttribute("aria-labelledby", "status-tab-overview");
  const skins = document.createElement("section");
  skins.id = "status-skins";
  skins.role = "tabpanel";
  skins.setAttribute("aria-labelledby", "status-tab-skins");
  skins.hidden = true;
  const header = [...activity.children].filter((node) =>
    node.matches("h2,.eyebrow,.lead"),
  );
  for (const node of [...activity.children])
    if (!header.includes(node) && node !== manager) overview.append(node);
  skins.append(manager);
  activity.append(tabs, overview, skins);
  let currentStatus = "overview";
  function selectStatus(id) {
    currentStatus = id;
    for (const tab of tabs.children) {
      const chosen = tab.dataset.statusPanel === id;
      tab.setAttribute("aria-selected", String(chosen));
      tab.tabIndex = chosen ? 0 : -1;
    }
    overview.hidden = id !== "overview";
    skins.hidden = id !== "skins";
  }
  for (const [id, label, glyph] of [
    ["overview", "运行状态", "activity"],
    ["skins", "皮肤", "skin"],
  ]) {
    const tab = document.createElement("button");
    tab.type = "button";
    tab.role = "tab";
    tab.id = "status-tab-" + id;
    tab.dataset.statusPanel = id;
    tab.setAttribute("aria-controls", "status-" + id);
    tab.innerHTML = icon(glyph);
    tab.append(document.createTextNode(label));
    tab.addEventListener("click", () => selectStatus(id));
    tabs.append(tab);
  }
  selectStatus(currentStatus);
  function openSkins() {
    window.FaxToolboxUi.showActivity();
    selectStatus("skins");
  }
  $("settings-open-skins").addEventListener("click", openSkins);
  $("skin-recovery-open").addEventListener("click", openSkins);
  $("shell-back").addEventListener("click", () => window.FaxToolboxUi.goBack());
  $("agent-search-toggle").innerHTML = icon("search");
  $("agent-sidebar-toggle").innerHTML = icon("menu");
  for (const toggle of document.querySelectorAll(".study-toggle"))
    toggle.innerHTML = icon("menu") + "<span>分类</span>";
  $("agent-image-add").innerHTML = icon("attach") + "<span>图片</span>";
  $("agent-image-add").tabIndex = 0;
  $("agent-image-add").addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      $("agent-image-file").click();
    }
  });
  // Preserve labels for settings panels and one keyboard model across all tab groups.
  const settingsMenu = document.querySelector(".settings-menu");
  settingsMenu.role = "tablist";
  settingsMenu.setAttribute("aria-orientation", "vertical");
  function syncSettingsTabs() {
    for (const tab of settingsMenu.children) {
      const id = tab.dataset.settingsPanel,
        selected = tab.classList.contains("selected");
      tab.role = "tab";
      tab.id = "settings-tab-" + id;
      tab.setAttribute("aria-selected", String(selected));
      tab.tabIndex = selected ? 0 : -1;
      tab.setAttribute("aria-controls", "settings-panel-" + id);
      const panel = document.querySelector('[data-panel="' + id + '"]');
      panel.id = "settings-panel-" + id;
      panel.role = "tabpanel";
      panel.setAttribute("aria-labelledby", tab.id);
    }
  }
  settingsMenu.addEventListener("click", syncSettingsTabs);
  syncSettingsTabs();
  for (const control of document.querySelectorAll("[data-activity-filter]"))
    control.setAttribute(
      "aria-pressed",
      String(control.classList.contains("selected")),
    );
  document.querySelector(".activity-filters").addEventListener("click", () => {
    for (const control of document.querySelectorAll("[data-activity-filter]"))
      control.setAttribute(
        "aria-pressed",
        String(control.classList.contains("selected")),
      );
  });
  window.FaxWorkspace = Object.freeze({ openSkins, filterTools });
})();
