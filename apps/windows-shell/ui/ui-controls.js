/* Shared controls preserve real form values and native application commands. */
(() => {
  "use strict";
  const descriptors = new Map();
  let active,
    floating,
    sequence = 0,
    scheduled = 0,
    tooltipTimer,
    tooltipTarget;
  const icon = (name) => window.FaxIcons.svg(name);
  const variables = [
    "--bg",
    "--panel",
    "--surface",
    "--border",
    "--text",
    "--subtle",
    "--accent",
    "--on-accent",
    "--error",
    "--skin-radius",
    "--skin-font-family",
  ];
  function inherit(from, to) {
    const style = getComputedStyle(from);
    for (const name of variables)
      to.style.setProperty(name, style.getPropertyValue(name));
    to.style.colorScheme = style.colorScheme;
  }
  function closeSelect(focus = false) {
    if (!active) return;
    const entry = active;
    active = null;
    entry.combo.setAttribute("aria-expanded", "false");
    entry.combo.removeAttribute("aria-activedescendant");
    if (entry.popup.matches(":popover-open")) entry.popup.hidePopover();
    entry.popup.remove();
    if (focus && entry.combo.isConnected) entry.combo.focus();
  }
  function positionSelect() {
    if (!active) return;
    const { combo, popup } = active,
      rect = combo.getBoundingClientRect();
    if (!combo.checkVisibility() || rect.bottom < 0 || rect.top > innerHeight) {
      closeSelect();
      return;
    }
    const below = innerHeight - rect.bottom - 12,
      above = rect.top - 12;
    const height = Math.min(
      300,
      Math.max(below, above),
      popup.scrollHeight + 12,
    );
    const top =
      below >= Math.min(240, popup.scrollHeight + 12) || below >= above
        ? rect.bottom + 6
        : Math.max(8, rect.top - height - 6);
    const width = Math.min(innerWidth - 16, Math.max(rect.width, 210));
    Object.assign(popup.style, {
      left: Math.max(8, Math.min(rect.left, innerWidth - width - 8)) + "px",
      top: top + "px",
      width: width + "px",
      maxHeight: Math.max(80, height) + "px",
    });
  }
  function highlight(entry, index) {
    const enabled = entry.items.filter((item) => !item.disabled);
    const chosen = entry.items[index];
    if (!chosen || chosen.disabled || !enabled.length) return;
    entry.index = index;
    for (let i = 0; i < entry.items.length; i++)
      entry.items[i].node.classList.toggle("highlighted", i === index);
    entry.combo.setAttribute("aria-activedescendant", chosen.node.id);
    chosen.node.scrollIntoView({ block: "nearest" });
  }
  function choose(entry, index) {
    const item = entry.items[index];
    if (!item || item.disabled || entry.select.matches(":disabled")) return;
    const changed = entry.select.value !== item.value;
    entry.select.value = item.value;
    closeSelect(true);
    refresh(entry);
    if (changed) {
      entry.select.dispatchEvent(new Event("input", { bubbles: true }));
      entry.select.dispatchEvent(new Event("change", { bubbles: true }));
    }
  }
  function openSelect(entry, start) {
    if (entry.select.matches(":disabled")) return;
    if (floating && !floating.panel.contains(entry.select)) floating.close();
    closeSelect();
    active = entry;
    const popup = document.createElement("div");
    popup.className = "ui-select-popup";
    popup.id = entry.combo.getAttribute("aria-controls");
    popup.role = "listbox";
    popup.setAttribute("aria-label", entry.combo.getAttribute("aria-label"));
    if ("showPopover" in popup) popup.setAttribute("popover", "manual");
    entry.popup = popup;
    entry.items = [];
    let group;
    for (const option of entry.select.options) {
      if (option.hidden) continue;
      if (
        option.parentElement.tagName === "OPTGROUP" &&
        group !== option.parentElement
      ) {
        group = option.parentElement;
        const caption = document.createElement("div");
        caption.className = "ui-select-group";
        caption.textContent = group.label;
        popup.append(caption);
      }
      const node = document.createElement("div"),
        index = entry.items.length;
      node.className = "ui-select-option";
      node.role = "option";
      node.id = popup.id + "-" + index;
      node.setAttribute("aria-selected", String(option.selected));
      const disabled = option.disabled || option.parentElement.disabled;
      node.setAttribute("aria-disabled", String(!!disabled));
      const label = document.createElement("span");
      label.textContent = option.label;
      node.append(label);
      node.insertAdjacentHTML("beforeend", icon("check"));
      node.addEventListener("pointerdown", (event) => event.preventDefault());
      node.addEventListener("pointermove", () => highlight(entry, index));
      node.addEventListener("click", () => choose(entry, index));
      entry.items.push({ node, value: option.value, disabled });
      popup.append(node);
    }
    inherit(entry.select, popup);
    (entry.select.closest("dialog") || document.body).append(popup);
    if (popup.showPopover) popup.showPopover();
    entry.combo.setAttribute("aria-expanded", "true");
    positionSelect();
    const selected = entry.items.findIndex(
      (item) => item.value === entry.select.value && !item.disabled,
    );
    const first = entry.items.findIndex((item) => !item.disabled);
    highlight(
      entry,
      start === "last"
        ? entry.items.findLastIndex((item) => !item.disabled)
        : selected >= 0
          ? selected
          : first,
    );
  }
  function keySelect(entry, event) {
    const key = event.key,
      opened = active === entry;
    if (key === "Tab") {
      if (opened) closeSelect();
      return;
    }
    if (key === "Escape" && opened) {
      event.preventDefault();
      event.stopImmediatePropagation();
      closeSelect(true);
      return;
    }
    if (["ArrowDown", "ArrowUp", "Home", "End", "Enter", " "].includes(key)) {
      event.preventDefault();
      event.stopPropagation();
      if (!opened) {
        openSelect(
          entry,
          key === "End" || key === "ArrowUp" ? "last" : "first",
        );
        return;
      }
      if (key === "Enter" || key === " ") {
        choose(entry, entry.index);
        return;
      }
      const indexes = entry.items.flatMap((item, index) =>
        item.disabled ? [] : [index],
      );
      const position = indexes.indexOf(entry.index);
      const index =
        key === "Home"
          ? indexes[0]
          : key === "End"
            ? indexes.at(-1)
            : indexes[
                (position + (key === "ArrowUp" ? -1 : 1) + indexes.length) %
                  indexes.length
              ];
      highlight(entry, index);
      return;
    }
    if (
      key.length === 1 &&
      !event.ctrlKey &&
      !event.metaKey &&
      !event.altKey &&
      !event.isComposing
    ) {
      if (!opened) openSelect(entry);
      clearTimeout(entry.searchTimer);
      entry.search = (entry.search || "") + key.toLocaleLowerCase();
      const index = entry.items.findIndex(
        (item) =>
          !item.disabled &&
          item.node.textContent
            .trim()
            .toLocaleLowerCase()
            .startsWith(entry.search),
      );
      if (index >= 0) highlight(entry, index);
      entry.searchTimer = setTimeout(() => {
        entry.search = "";
      }, 700);
    }
  }
  function refresh(entry) {
    if (!entry.select.isConnected) {
      if (active === entry) closeSelect();
      descriptors.delete(entry.select);
      return;
    }
    const text =
      entry.select.selectedOptions[0]?.label ||
      entry.select.options[0]?.label ||
      "请选择";
    if (entry.label.textContent !== text) entry.label.textContent = text;
    const disabled = entry.select.matches(":disabled");
    entry.combo.tabIndex = disabled ? -1 : 0;
    entry.combo.setAttribute("aria-disabled", String(disabled));
    if (disabled && active === entry) closeSelect();
    const labelCopy = entry.select.labels?.[0]?.cloneNode(true);
    labelCopy
      ?.querySelectorAll("select,.ui-select-field,small,svg")
      .forEach((node) => node.remove());
    const labelled = labelCopy?.textContent.trim();
    const name =
      entry.select.getAttribute("aria-label") || labelled || "选择选项";
    if (entry.combo.getAttribute("aria-label") !== name)
      entry.combo.setAttribute("aria-label", name);
  }
  function enhance(select) {
    if (
      descriptors.has(select) ||
      select.multiple ||
      select.size > 1 ||
      select.dataset.uiNative !== undefined
    )
      return;
    const wrapper = document.createElement("span");
    wrapper.className = "ui-select-field";
    const combo = document.createElement("span");
    combo.className = "ui-select";
    combo.role = "combobox";
    combo.tabIndex = 0;
    combo.setAttribute("aria-haspopup", "listbox");
    combo.setAttribute("aria-expanded", "false");
    combo.setAttribute("aria-controls", "ui-options-" + ++sequence);
    const label = document.createElement("span");
    label.className = "ui-select-value";
    combo.append(label);
    combo.insertAdjacentHTML("beforeend", icon("chevron"));
    select.before(wrapper);
    wrapper.append(select, combo);
    select.classList.add("ui-select-source");
    select.tabIndex = -1;
    select.setAttribute("aria-hidden", "true");
    const entry = { select, combo, label, index: -1 };
    descriptors.set(select, entry);
    combo.addEventListener("click", (event) => {
      event.preventDefault();
      if (active === entry) closeSelect();
      else openSelect(entry);
    });
    combo.addEventListener("keydown", (event) => keySelect(entry, event));
    combo.addEventListener("blur", () => {
      if (active === entry) closeSelect();
    });
    select.addEventListener("change", () => refresh(entry));
    refresh(entry);
  }
  function refreshAll() {
    scheduled = 0;
    for (const select of document.querySelectorAll("select")) enhance(select);
    for (const entry of descriptors.values()) refresh(entry);
    window.FaxIcons.hydrate();
    if (active && !active.combo.checkVisibility()) closeSelect();
  }

  const focusable = (host) => [...host.querySelectorAll(
    'button:not(:disabled),a[href],input:not(:disabled),textarea:not(:disabled),select:not(:disabled),[tabindex="0"]',
  )].filter((node) => node.tabIndex >= 0 && node.checkVisibility());

  // Keep popovers in their module's DOM/skin scope while drawing above clipped panes.
  function createPopover({ trigger, panel, menu = false, onOpen }) {
    panel.id ||= "ui-popover-" + ++sequence;
    panel.classList.add("ui-floating-panel");
    panel.role = menu ? "menu" : "region";
    panel.setAttribute("aria-label", trigger.getAttribute("aria-label") || trigger.textContent.trim());
    trigger.setAttribute("aria-controls", panel.id);
    trigger.setAttribute("aria-expanded", "false");
    if (menu) {
      trigger.setAttribute("aria-haspopup", "menu");
      panel.querySelectorAll("button").forEach((node) => node.role = "menuitem");
    }
    if (panel.showPopover) panel.setAttribute("popover", "manual");
    const entry = {
      panel,
      close(restore = false) {
        if (panel.hidden) return;
        if (active && panel.contains(active.select)) closeSelect();
        if (panel.matches(":popover-open")) panel.hidePopover();
        panel.hidden = true;
        trigger.setAttribute("aria-expanded", "false");
        if (floating === entry) floating = null;
        if (restore && trigger.checkVisibility()) trigger.focus({ preventScroll: true });
      },
      position() {
        if (panel.hidden) return;
        if (!trigger.checkVisibility()) { entry.close(); return; }
        const rect = trigger.getBoundingClientRect();
        panel.style.maxHeight = Math.max(80, innerHeight - 24) + "px";
        panel.style.maxWidth = Math.max(160, innerWidth - 24) + "px";
        const { width, height } = panel.getBoundingClientRect();
        const above = rect.top - height - 8;
        const top = above >= 12 ? above : Math.min(rect.bottom + 8, innerHeight - height - 12);
        panel.style.left = Math.max(12, Math.min(rect.left, innerWidth - width - 12)) + "px";
        panel.style.top = Math.max(12, top) + "px";
      },
      open(last = false) {
        if (trigger.matches(":disabled")) return;
        closeSelect();
        floating?.close();
        hideTooltip();
        inherit(trigger, panel);
        panel.hidden = false;
        floating = entry;
        trigger.setAttribute("aria-expanded", "true");
        if (panel.showPopover) panel.showPopover();
        onOpen?.();
        entry.position();
        const items = focusable(panel);
        (last ? items.at(-1) : items[0])?.focus({ preventScroll: true });
      },
    };
    trigger.addEventListener("click", () => panel.hidden ? entry.open() : entry.close(true));
    trigger.addEventListener("keydown", (event) => {
      if (["ArrowDown", "ArrowUp"].includes(event.key)) {
        event.preventDefault();
        entry.open(event.key === "ArrowUp");
      }
    });
    panel.addEventListener("keydown", (event) => {
      if (!menu || !["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
      const items = focusable(panel), index = items.indexOf(document.activeElement);
      event.preventDefault();
      const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 :
        (index + (event.key === "ArrowUp" ? -1 : 1) + items.length) % items.length;
      items[next]?.focus();
    });
    document.addEventListener("pointerdown", (event) => {
      if (!panel.contains(event.target) && !trigger.contains(event.target)) entry.close();
    });
    document.addEventListener("focusin", (event) => {
      if (!panel.contains(event.target) && !trigger.contains(event.target) &&
          !active?.popup.contains(event.target)) entry.close();
    });
    new ResizeObserver(() => entry.position()).observe(panel);
    return entry;
  }

  // Native dialogs supply focus containment, background inertness and Escape.
  // The same existing panel returns to its desktop position without losing input.
  function createDrawer({ trigger, panel, media, label }) {
    const breakpoint = matchMedia(media), anchor = document.createComment("drawer position");
    panel.before(anchor);
    const dialog = document.createElement("dialog");
    dialog.className = "ui-drawer";
    dialog.id = "ui-drawer-" + ++sequence;
    dialog.setAttribute("aria-label", label);
    dialog.hidden = true;
    const heading = document.createElement("div");
    heading.className = "ui-drawer-heading";
    const title = document.createElement("h2");
    title.textContent = label;
    const dismiss = document.createElement("button");
    dismiss.type = "button";
    dismiss.className = "secondary ui-drawer-close";
    dismiss.setAttribute("aria-label", "关闭" + label);
    dismiss.innerHTML = icon("close");
    heading.append(title, dismiss);
    dialog.append(heading);
    anchor.parentElement.append(dialog);
    trigger.setAttribute("aria-controls", dialog.id);
    trigger.setAttribute("aria-haspopup", "dialog");
    trigger.setAttribute("aria-expanded", "false");
    const restorePanel = () => {
      if (dialog.open) return;
      if (dialog.contains(panel)) anchor.after(panel);
      dialog.hidden = true;
      trigger.setAttribute("aria-expanded", "false");
    };
    const close = (restore = true) => {
      if (!dialog.open) return;
      if (active && dialog.contains(active.select)) closeSelect();
      if (floating && dialog.contains(floating.panel)) floating.close();
      dialog.close();
      restorePanel();
      if (restore && trigger.checkVisibility()) trigger.focus({ preventScroll: true });
    };
    const open = () => {
      if (!breakpoint.matches || dialog.open) return;
      floating?.close();
      closeSelect();
      hideTooltip();
      inherit(panel, dialog);
      dialog.append(panel);
      dialog.hidden = false;
      trigger.setAttribute("aria-expanded", "true");
      dialog.showModal();
      (panel.querySelector('[role="tab"][aria-selected="true"]') || focusable(panel)[0] || dismiss).focus();
    };
    trigger.addEventListener("click", () => dialog.open ? close() : open());
    dismiss.addEventListener("click", () => close());
    dialog.addEventListener("cancel", (event) => { event.preventDefault(); close(); });
    dialog.addEventListener("close", restorePanel);
    dialog.addEventListener("click", (event) => {
      if (event.target !== dialog) return;
      const rect = dialog.getBoundingClientRect();
      if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) close();
    });
    breakpoint.addEventListener("change", () => close(false));
    addEventListener("fax:view-changed", () => close(false));
    return { open, close, isOpen: () => dialog.open };
  }
  const observer = new MutationObserver((records) => {
    if (
      !records.some(
        (record) =>
          record.type === "childList" ||
          ["disabled", "hidden", "aria-label", "label", "selected"].includes(
            record.attributeName,
          ),
      )
    )
      return;
    if (!scheduled) scheduled = requestAnimationFrame(refreshAll);
  });
  observer.observe(document.body, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ["disabled", "hidden", "aria-label", "label", "selected"],
  });
  document.addEventListener(
    "pointerdown",
    (event) => {
      if (
        active &&
        !active.combo.contains(event.target) &&
        !active.popup.contains(event.target)
      )
        closeSelect();
    },
    true,
  );
  document.addEventListener(
    "scroll",
    (event) => {
      if (active && !active.popup.contains(event.target)) positionSelect();
      if (floating && !floating.panel.contains(event.target)) floating.position();
    },
    true,
  );
  addEventListener("resize", () => {
    positionSelect();
    floating?.position();
    hideTooltip();
  });
  addEventListener("fax:view-changed", () => {
    closeSelect();
    floating?.close();
    hideTooltip();
  });
  addEventListener("fax:skin-state", () => {
    closeSelect();
    floating?.close();
    refreshAll();
  });
  document.addEventListener("reset", () => setTimeout(refreshAll, 0));

  // Tooltips enhance the existing title attribute; title remains available as a fallback.
  const tooltip = document.createElement("div");
  tooltip.id = "ui-tooltip";
  tooltip.role = "tooltip";
  tooltip.hidden = true;
  if (tooltip.showPopover) tooltip.setAttribute("popover", "manual");
  document.body.append(tooltip);
  function hideTooltip() {
    clearTimeout(tooltipTimer);
    if (tooltip.matches(":popover-open")) tooltip.hidePopover();
    tooltip.hidden = true;
    if (tooltipTarget?.getAttribute("aria-describedby") === tooltip.id)
      tooltipTarget.removeAttribute("aria-describedby");
    tooltipTarget = null;
  }
  function showTooltip(target) {
    hideTooltip();
    const text = target.hasAttribute("title")
      ? target.getAttribute("title")
      : target.dataset.tooltip;
    if (!text || target.matches(":disabled")) return;
    target.dataset.tooltip = text;
    target.removeAttribute("title");
    tooltipTarget = target;
    tooltipTimer = setTimeout(() => {
      if (!target.isConnected || !target.checkVisibility()) return;
      (target.closest("dialog[open]") || document.body).append(tooltip);
      tooltip.textContent = text;
      inherit(target, tooltip);
      tooltip.hidden = false;
      if (tooltip.showPopover) tooltip.showPopover();
      const rect = target.getBoundingClientRect();
      tooltip.style.left =
        Math.max(
          8,
          Math.min(
            rect.left + rect.width / 2 - tooltip.offsetWidth / 2,
            innerWidth - tooltip.offsetWidth - 8,
          ),
        ) + "px";
      tooltip.style.top =
        (rect.bottom + tooltip.offsetHeight + 12 < innerHeight
          ? rect.bottom + 8
          : Math.max(8, rect.top - tooltip.offsetHeight - 8)) + "px";
      if (!target.hasAttribute("aria-describedby"))
        target.setAttribute("aria-describedby", tooltip.id);
    }, 550);
  }
  document.addEventListener("pointerover", (event) => {
    const target = event.target.closest("[title],[data-tooltip]");
    if (target && !target.contains(event.relatedTarget)) showTooltip(target);
  });
  document.addEventListener("pointerout", (event) => {
    if (tooltipTarget && !tooltipTarget.contains(event.relatedTarget))
      hideTooltip();
  });
  document.addEventListener("focusin", (event) => {
    const target = event.target.closest("[title],[data-tooltip]");
    if (target) showTooltip(target);
  });
  document.addEventListener("focusout", hideTooltip);
  document.addEventListener("pointerdown", hideTooltip);
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") hideTooltip();
    if (event.key === "Escape" && floating && !event.isComposing) {
      event.preventDefault();
      event.stopImmediatePropagation();
      floating.close(true);
    }
  });

  document.addEventListener(
    "beforetoggle",
    (event) => {
      if (event.target.tagName === "DIALOG" && event.newState === "open")
        floating?.close();
      if (event.target.tagName === "DIALOG" && event.newState === "open" &&
          !event.target.classList.contains("ui-drawer"))
        inherit(
          document.activeElement || document.documentElement,
          event.target,
        );
    },
    true,
  );

  // Tabs use one focus model even when their panels belong to different modules.
  document.addEventListener("keydown", (event) => {
    const tab = event.target.closest('[role="tab"]'),
      list = tab?.closest('[role="tablist"]');
    if (
      !list ||
      ![
        "ArrowLeft",
        "ArrowRight",
        "ArrowUp",
        "ArrowDown",
        "Home",
        "End",
      ].includes(event.key)
    )
      return;
    const tabs = [...list.querySelectorAll('[role="tab"]')].filter(
      (item) => !item.disabled && !item.hidden,
    );
    const index = tabs.indexOf(tab);
    let next =
      event.key === "Home"
        ? 0
        : event.key === "End"
          ? tabs.length - 1
          : (index +
              (["ArrowLeft", "ArrowUp"].includes(event.key) ? -1 : 1) +
              tabs.length) %
            tabs.length;
    event.preventDefault();
    tabs[next]?.focus();
    tabs[next]?.click();
  });
  window.FaxUi = Object.freeze({
    refresh: refreshAll,
    createPopover,
    createDrawer,
    closeOverlays: () => {
      closeSelect();
      floating?.close();
      hideTooltip();
    },
    inherit,
  });
  refreshAll();
})();
