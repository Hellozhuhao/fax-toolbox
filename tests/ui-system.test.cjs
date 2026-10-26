const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");
const { chromium } = require("playwright");
const root = path.resolve(__dirname, "..");
const ui = path.join(root, "apps/windows-shell/ui");
const builtins = JSON.parse(
  fs.readFileSync(path.join(ui, "skins/builtin.json"), "utf8"),
);

async function fixture() {
  const server = http.createServer((req, res) => {
    const relative = decodeURIComponent(
      new URL(req.url, "http://localhost").pathname,
    );
    const file = path.resolve(
      ui,
      "." + (relative === "/" ? "/index.html" : relative),
    );
    if (
      !file.startsWith(ui + path.sep) ||
      !fs.existsSync(file) ||
      !fs.statSync(file).isFile()
    ) {
      res.writeHead(404).end();
      return;
    }
    res.setHeader(
      "Content-Type",
      {
        ".html": "text/html",
        ".js": "text/javascript",
        ".css": "text/css",
        ".json": "application/json",
        ".svg": "image/svg+xml",
        ".woff2": "font/woff2",
      }[path.extname(file)] || "application/octet-stream",
    );
    res.end(fs.readFileSync(file));
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const browser = await chromium.launch({
    headless: true,
    executablePath: process.env.FAX_CHROMIUM_EXECUTABLE || undefined,
    args: ["--no-sandbox"],
  });
  const page = await browser.newPage({
    viewport: { width: 1280, height: 900 },
  });
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.addInitScript((skins) => {
    const listeners = [];
    let current = {
      skins,
      settings: { globalId: "fax.classic", overrides: {} },
      importedIds: [],
    };
    window.uiCommands = [];
    window.emitUi = (message) =>
      listeners.forEach((listener) => listener({ data: { v: 1, ...message } }));
    window.chrome ||= {};
    window.chrome.webview = {
      addEventListener: (kind, listener) => {
        if (kind === "message") listeners.push(listener);
      },
      postMessage: (json) => {
        const command = JSON.parse(json);
        window.uiCommands.push(command);
        if (command.command === "skins.update")
          current = {
            skins: [...skins, ...command.state.imported],
            settings: command.state.settings,
            importedIds: command.state.imported.map((skin) => skin.id),
          };
        if (command.command === "skins.reset")
          current = {
            ...current,
            settings: { globalId: "fax.classic", overrides: {} },
          };
        if (command.command.startsWith("skins."))
          queueMicrotask(() =>
            window.emitUi({ kind: "skins.state", state: current }),
          );
      },
    };
  }, builtins);
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.waitForFunction(() => window.FaxToolboxSkins?.getSelection());
  await page.evaluate(() => document.fonts.ready);
  return {
    page,
    errors,
    close: async () => {
      await browser.close();
      await new Promise((resolve) => server.close(resolve));
    },
  };
}
const emit = (page, message) =>
  page.evaluate((message) => window.emitUi(message), message);
const command = (page, name) =>
  page.evaluate(
    (name) => window.uiCommands.filter((item) => item.command === name),
    name,
  );
async function skins(page) {
  await page.locator('.sidebar [data-view="activity"]').click();
  await page.locator("#status-tab-skins").click();
}
async function select(page, name, label) {
  await page.getByRole("combobox", { name, exact: true }).click();
  await page.getByRole("option", { name: label, exact: true }).click();
}
async function theme(page, id) {
  await skins(page);
  await page.locator(`.skin-choice:has(input[value="${id}"])`).click();
  await page.waitForFunction(
    (id) => FaxToolboxSkins.getSelection().globalId === id,
    id,
  );
}

test(
  "public navigation, search, draft safety, keyboard palette and recent tools",
  { timeout: 60000 },
  async () => {
    const f = await fixture(),
      { page } = f;
    try {
      await page.locator("#home-prompt").fill("帮我整理项目");
      await page.locator("#home-prompt-form button").click();
      assert.equal(
        await page.locator("#agent-prompt").inputValue(),
        "帮我整理项目",
      );
      assert.equal(
        (await command(page, "agent.send")).length,
        0,
        "Opening Agent must not send a request",
      );
      await page.locator('.sidebar [data-view="home"]').click();
      assert.equal(await page.locator("#home-recent").isVisible(), true);
      await page.locator("#home-prompt").fill("另一项任务");
      await page.locator("#home-prompt-form button").click();
      assert.equal(await page.locator("#toolbox-confirm").isVisible(), true);
      await page.keyboard.press("Escape");
      assert.equal(
        await page.locator("#agent-prompt").inputValue(),
        "帮我整理项目",
      );
      await page.locator("#home-prompt-form button").click();
      await page.locator("#toolbox-confirm-accept").click();
      await page.waitForFunction(() => document.body.dataset.view === "ai");
      assert.equal(
        await page.locator("#agent-prompt").inputValue(),
        "另一项任务",
      );
      await page.locator('.sidebar [data-view="modules"]').click();
      await page.locator("#tool-search").fill("USB");
      assert.equal(
        await page.locator(".library-card:not([hidden])").count(),
        1,
      );
      await page.locator("#module-android").click();
      assert.equal((await command(page, "android.open")).length, 1);
      await page.locator("#tool-search").fill("不存在的工具");
      assert.equal(await page.locator("#tool-empty").isVisible(), true);
      await page.locator("#tool-clear").click();
      await page
        .locator('.filter-segments label:has(input[value="学习"])')
        .click();
      assert.equal(
        await page.locator(".library-card:not([hidden])").count(),
        2,
      );
      await page.keyboard.press("Control+k");
      await page.locator("#command-search").fill("皮肤");
      await page.keyboard.press("Enter");
      await page.waitForFunction(
        () => !document.getElementById("status-skins").hidden,
      );
      assert.equal(await page.locator("#command-palette").isVisible(), false);
      await page.locator("#command-open").click();
      await page.keyboard.press("Escape");
      assert.equal(
        await page
          .locator("#command-open")
          .evaluate((node) => node === document.activeElement),
        true,
      );
      assert.deepEqual(f.errors, []);
    } finally {
      await f.close();
    }
  },
);

test(
  "skin cards, styled dropdown keyboard semantics, inheritance, undo, dialogs and focus",
  { timeout: 60000 },
  async () => {
    const f = await fixture(),
      { page } = f;
    try {
      await theme(page, "fax.midnight");
      assert.equal(
        await page
          .locator('input[name="skin-choice"]:checked')
          .evaluate((node) => node === document.activeElement),
        true,
      );
      await page.keyboard.press("ArrowRight");
      await page.waitForFunction(
        () => FaxToolboxSkins.getSelection().globalId === "fax.ocean",
      );
      assert.equal(
        await page
          .locator('input[name="skin-choice"]:checked')
          .evaluate((node) => node === document.activeElement),
        true,
      );
      await select(page, "Agent的皮肤", "海风 · 内置");
      assert.equal(
        await page.evaluate(() => FaxToolboxSkins.getSelection().overrides.ai),
        "fax.ocean",
      );
      await page
        .getByRole("combobox", { name: "Agent的皮肤", exact: true })
        .focus();
      await page.keyboard.press("Enter");
      await page.keyboard.press("Home");
      await page.keyboard.press("Escape");
      assert.equal(
        await page.evaluate(() => FaxToolboxSkins.getSelection().overrides.ai),
        "fax.ocean",
        "Escape must leave selection untouched",
      );
      assert.equal(
        await page
          .getByRole("combobox", { name: "Agent的皮肤", exact: true })
          .evaluate((node) => node === document.activeElement),
        true,
      );
      await theme(page, "fax.classic");
      assert.equal(
        await page.evaluate(() => FaxToolboxSkins.getSelection().overrides.ai),
        "fax.ocean",
      );
      await page.locator("#skin-unify").click();
      await page.waitForFunction(
        () =>
          Object.keys(FaxToolboxSkins.getSelection().overrides).length === 0,
      );
      await page.locator("#skin-undo").click();
      await page.waitForFunction(
        () => FaxToolboxSkins.getSelection().overrides.ai === "fax.ocean",
      );
      await page.locator("#skin-reset").click();
      assert.equal(
        await page
          .locator("#toolbox-confirm-cancel")
          .evaluate((node) => node === document.activeElement),
        true,
      );
      await page.locator("#toolbox-confirm-cancel").click();
      assert.equal(
        await page.evaluate(() => FaxToolboxSkins.getSelection().overrides.ai),
        "fax.ocean",
      );
      await page.locator("#skin-reset").click();
      await page.locator("#toolbox-confirm-accept").click();
      await page.waitForFunction(
        () =>
          Object.keys(FaxToolboxSkins.getSelection().overrides).length === 0,
      );
      assert.deepEqual(f.errors, []);
    } finally {
      await f.close();
    }
  },
);

test(
  "module command contracts, task states, retained work and explicit approvals survive the redesign",
  { timeout: 60000 },
  async () => {
    const f = await fixture(),
      { page } = f;
    try {
      await page.locator('[data-open="course"]').click();
      assert.equal((await command(page, "course.activate")).length, 1);
      assert.equal(await page.locator("#course-start").isDisabled(), true);
      await emit(page, {
        kind: "course.status",
        state: "READY",
        ready: true,
        detail: "已识别课程",
        url: "https://example.com/course",
      });
      await emit(page, {
        kind: "course.catalog",
        live: true,
        currentId: "c1",
        entries: [
          { id: "c1", name: "第一章" },
          { id: "c2", name: "第二章" },
        ],
      });
      await select(page, "选择章节", "第二章");
      await page.locator("#course-jump").click();
      assert.equal((await command(page, "course.jump")).at(-1).chapterId, "c2");
      await page.locator("#course-start").click();
      assert.equal((await command(page, "course.start")).length, 1);
      await emit(page, {
        kind: "course.status",
        state: "RUNNING",
        ready: true,
        detail: "正在运行",
      });
      assert.equal(await page.locator("#course-start").isDisabled(), true);
      assert.equal(await page.locator("#course-pause").isDisabled(), false);
      await page.locator("#course-pause").click();
      assert.equal((await command(page, "course.pause")).length, 1);
      await emit(page, {
        kind: "course.status",
        state: "PAUSED",
        ready: true,
        detail: "已暂停",
      });
      assert.equal(
        await page.locator("#course-start").textContent(),
        "继续任务",
      );
      await page.locator('.sidebar [data-view="ai"]').click();
      await page.locator("#agent-prompt").fill("尚未发送的中文草稿");
      await page
        .locator("#agent-prompt")
        .dispatchEvent("keydown", {
          key: "Enter",
          isComposing: true,
          keyCode: 229,
        });
      assert.equal(
        (await command(page, "agent.send")).length,
        0,
        "IME confirmation must not send the message",
      );
      await emit(page, {
        kind: "agent.approval",
        detail: "确认修改项目文件",
        extra: { id: "approval-1", preview: "修改 README.md" },
      });
      await theme(page, "fax.midnight");
      await page.locator('.sidebar [data-view="ai"]').click();
      assert.equal(
        await page.locator("#agent-prompt").inputValue(),
        "尚未发送的中文草稿",
      );
      assert.equal(await page.locator("#agent-approval").isVisible(), true);
      assert.equal((await command(page, "agent.approve")).length, 0);
      await page.locator("#agent-reject").click();
      assert.equal(
        (await command(page, "agent.reject")).at(-1).approvalId,
        "approval-1",
      );
      await emit(page, {
        kind: "agent.state",
        extra: { busy: true },
        detail: "正在处理",
      });
      assert.equal(await page.locator("#agent-send").isDisabled(), true);
      await page.locator("#agent-cancel").click();
      assert.equal((await command(page, "agent.cancel")).length, 1);
      await emit(page, { kind: "agent.state", extra: { busy: false } });
      await page.locator('.sidebar [data-view="modules"]').click();
      await page.locator("#module-homework").click();
      await emit(page, {
        kind: "homework.result",
        items: [
          {
            number: 1,
            type: "essay",
            question: "解方程：2x + 1 = 5",
            answer: "x = 2",
            explanation: "先移项，再除以 2。",
          },
        ],
      });
      assert.match(
        await page.locator("#homework-results").textContent(),
        /x = 2/,
      );
      await page.locator(".homework-explanation summary").click();
      assert.equal((await command(page, "homework.explain")).at(-1).number, 1);
      await page.locator('.sidebar [data-view="modules"]').click();
      await page.locator("#module-proxy").click();
      await emit(page, {
        kind: "proxy.status",
        data: {
          ok: true,
          config: {
            coreType: "xray",
            proxyUrl: "http://127.0.0.1:10809",
            workProcesses: [],
            gameProcesses: [],
            gamePathHints: [],
            gamePathExcludes: [],
          },
          status: {
            running: false,
            coreRunning: false,
            portReady: true,
            logs: [],
          },
        },
      });
      await page.locator("#proxy-on").click();
      assert.equal((await command(page, "proxy.on")).length, 0);
      await page.locator("#proxy-confirm-cancel").click();
      assert.equal((await command(page, "proxy.on")).length, 0);
      await page.locator("#proxy-on").click();
      await page.locator("#proxy-confirm-ok").click();
      assert.equal((await command(page, "proxy.on")).length, 1);
      assert.equal(
        (await command(page, "course.stop")).length,
        0,
        "Changing views or themes must not stop another task",
      );
      assert.deepEqual(f.errors, []);
    } finally {
      await f.close();
    }
  },
);

test(
  "reachable surfaces: themes, narrow work panes, long content, context menus, dialogs and screenshots",
  { timeout: 120000 },
  async () => {
    const f = await fixture(),
      { page } = f;
    const capture = process.env.FAX_UI_SCREENSHOTS;
    const out = path.join(root, ".cache/ui-redesign/review");
    if (capture) fs.mkdirSync(out, { recursive: true });
    const shot = async (name) => {
      if (capture) {
        const overlay = /^(dropdown|dialog|context|command)-/.test(name);
        if (!overlay) await page.evaluate(() => scrollTo(0, 0));
        await page.screenshot({
          path: path.join(out, name + ".png"),
          fullPage: !overlay,
        });
      }
    };
    try {
      await shot("home-light");
      await emit(page, {
        kind: "course.status",
        state: "PAUSED",
        ready: true,
        chapter: "chapter-1",
        detail: "任务已暂停，可以继续或停止。",
        url: "https://example.com/course",
      });
      await emit(page, {
        kind: "course.catalog",
        live: true,
        currentId: "chapter-1",
        entries: [
          { id: "chapter-1", name: "第一章 · 课程概览" },
          { id: "chapter-2", name: "第二章 · 练习与复习" },
        ],
      });
      await emit(page, {
        kind: "homework.result",
        items: [
          {
            number: 1,
            type: "essay",
            question: "解方程：2x + 1 = 5",
            answer: "x = 2",
            explanation: "等式两边先减去 1，再除以 2。",
          },
        ],
      });
      await emit(page, {
        kind: "agent.event",
        extra: { type: "user" },
        detail: "帮我整理项目的迭代计划。",
      });
      await emit(page, {
        kind: "agent.event",
        extra: { type: "assistant" },
        detail:
          "可以先列出当前功能、已知问题和下一版本目标，再按优先级安排。涉及项目文件的修改会先向你展示具体操作。",
      });
      await emit(page, {
        kind: "proxy.status",
        data: {
          ok: true,
          config: {
            coreType: "xray",
            proxyUrl: "http://127.0.0.1:10809",
            workProcesses: ["ChatGPT"],
            gameProcesses: [],
          },
          status: {
            running: false,
            coreRunning: false,
            portReady: true,
            logs: ["自动切换已停止。"],
          },
        },
      });
      await emit(page, {
        kind: "proxy.detected",
        connections: [
          {
            name: "本机 Xray",
            proxyUrl: "http://127.0.0.1:10809",
            coreType: "xray",
            xrayConfig: "C:/example/config.json",
            ready: true,
          },
        ],
        applications: [
          { name: "ChatGPT", processName: "ChatGPT", running: true },
        ],
      });
      for (const width of [1280, 820, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        for (const view of [
          "home",
          "modules",
          "ai",
          "course",
          "homework",
          "proxy",
          "settings",
          "activity",
        ]) {
          await page.evaluate((view) => {
            const key = ["home", "modules", "activity", "settings"].includes(
              view,
            )
              ? "navigation." + view
              : "module." + view + ".open";
            FaxToolboxUi.skinActions[key].run({});
          }, view);
          await page.waitForTimeout(40);
          const overflow = await page.evaluate(() => ({
            page: document.documentElement.scrollWidth > innerWidth + 1,
            view: document.body.dataset.view,
          }));
          assert.equal(
            overflow.page,
            false,
            `Horizontal overflow at ${width}px in ${view}: ${JSON.stringify(await page.locator("#view-"+view+" *").evaluateAll(nodes=>nodes.filter(node=>node.checkVisibility()&&node.getBoundingClientRect().right>innerWidth+1).map(node=>({tag:node.tagName,cls:node.className,id:node.id,right:node.getBoundingClientRect().right})).slice(0,12)))}`,
          );
          if (width === 1280) await shot(view + "-light");
        }
        await skins(page);
        await shot("skins-" + width);
      }
      await page.setViewportSize({ width: 1280, height: 900 });
      await theme(page, "fax.midnight");
      await shot("skins-dark");
      await page
        .getByRole("combobox", { name: "Agent的皮肤", exact: true })
        .click();
      const bounds = await page.getByRole("listbox").boundingBox();
      assert.ok(
        bounds.x >= 0 &&
          bounds.y >= 0 &&
          bounds.x + bounds.width <= 1281 &&
          bounds.y + bounds.height <= 901,
      );
      await shot("dropdown-dark");
      await page.keyboard.press("Escape");
      await page.locator("#skin-reset").click();
      await shot("dialog-dark");
      await page.keyboard.press("Escape");
      await page.locator('.sidebar [data-view="ai"]').click();
      await shot("agent-dark");
      await page.locator("#agent-prompt").focus();
      await page.keyboard.press("Shift+F10");
      assert.equal(
        await page.locator("#toolbox-context-menu").isVisible(),
        true,
      );
      await page.keyboard.press("ArrowDown");
      assert.equal(
        await page
          .locator("#toolbox-context-menu button")
          .evaluateAll((nodes) =>
            nodes.some((node) => node === document.activeElement),
          ),
        true,
      );
      await shot("context-dark");
      await page.keyboard.press("Escape");
      assert.equal(
        await page.locator("#toolbox-context-menu").isVisible(),
        false,
      );
      await page.locator("#command-open").click();
      await shot("command-dark");
      await page.keyboard.press("Escape");
      await theme(page, "fax.ocean");
      await shot("skins-ocean");
      await page.emulateMedia({ reducedMotion: "reduce" });
      assert.equal(
        await page
          .locator(".sidebar .nav-item")
          .first()
          .evaluate((node) => getComputedStyle(node).transitionDuration),
        "0s",
      );
      const duplicates = await page.evaluate(() => {
        const ids = [...document.querySelectorAll("[id]")].map(
          (node) => node.id,
        );
        return ids.filter((id, index) => ids.indexOf(id) !== index);
      });
      assert.deepEqual(duplicates, []);
      assert.deepEqual(f.errors, []);
    } finally {
      await f.close();
    }
  },
);
