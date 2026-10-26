const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '..');
const ui = path.join(root, 'apps/windows-shell/ui');

test('skin manager: persistence, independent modules, import/export, custom GUI, isolation and recovery', {timeout: 120000}, async () => {
  const server = http.createServer((request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const file = path.resolve(ui, '.' + (relative === '/' ? '/index.html' : relative));
    if (!file.startsWith(ui + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404).end(); return; }
    const types = {'.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.woff2': 'font/woff2'};
    response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream'); response.end(fs.readFileSync(file));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({headless: true, executablePath: process.env.FAX_CHROMIUM_EXECUTABLE || undefined, args: ['--no-sandbox']});
  try {
    const page = await browser.newPage({viewport: {width: 1280, height: 1000}, acceptDownloads: true});
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}`);
    await page.waitForFunction(() => window.FaxToolboxSkins?.getSelection());
    const status = () => page.locator('.sidebar button[data-view="activity"]').click();
    await status();
    assert.equal(await page.locator('#skin-global option').count(), 3);
    assert.equal(await page.locator('[data-skin-module]').count(), 6);
    assert.equal(await page.locator('#view-modules #skin-manager').count(), 0);
    await page.locator('#skin-global').selectOption('fax.midnight');
    assert.equal(await page.evaluate(() => document.documentElement.dataset.theme), 'dark');
    await page.locator('[data-skin-module="ai"]').selectOption('fax.ocean');
    assert.deepEqual(await page.evaluate(() => [getComputedStyle(document.getElementById('view-ai')).getPropertyValue('--skin-accent'), getComputedStyle(document.getElementById('view-course')).getPropertyValue('--skin-accent')]), ['#087b86', '#b5a0ff']);
    await page.locator('#skin-global').selectOption('fax.classic');
    assert.equal(await page.locator('[data-skin-module="ai"]').inputValue(), 'fax.ocean');
    await page.reload(); await page.waitForFunction(() => window.FaxToolboxSkins?.getSelection()); await status();
    assert.equal(await page.locator('[data-skin-module="ai"]').inputValue(), 'fax.ocean');
    await page.locator('#skin-unify').click();
    assert.deepEqual(await page.evaluate(() => FaxToolboxSkins.getSelection().overrides), {});
    if (process.env.FAX_SKIN_SCREENSHOTS) {
      fs.mkdirSync(path.join(root, '.cache'), {recursive: true});
      await page.screenshot({path: path.join(root, '.cache/skins-light.png'), fullPage: true});
    }
    await page.locator('#skin-file').setInputFiles(path.join(root, 'examples/skins/studio.faxskin'));
    await page.waitForFunction(() => [...document.getElementById('skin-global').options].some(option => option.value === 'example.studio'));
    await page.locator('#skin-global').selectOption('example.studio');
    assert.equal(await page.locator('.app').getAttribute('data-skin-navigation'), 'top');
    await page.locator('[data-skin-module="ai"]').selectOption('fax.midnight');
    assert.deepEqual(await page.evaluate(() => [getComputedStyle(document.querySelector('#view-ai .card')).borderTopStyle, getComputedStyle(document.querySelector('#view-course .card')).borderTopStyle]), ['solid', 'dashed']);
    await page.locator('#skin-unify').click();
    await page.keyboard.press('Alt+1');
    assert.equal(await page.locator('body').getAttribute('data-view'), 'home');
    await status();
    const download = page.waitForEvent('download'); await page.locator('#skin-export').click();
    const exported = await download;
    assert.equal(exported.suggestedFilename(), 'example.studio.faxskin');
    assert.equal(JSON.parse(fs.readFileSync(await exported.path(), 'utf8')).id, 'example.studio');
    await page.locator('#skin-file').setInputFiles({name: 'invalid.faxskin', mimeType: 'application/json', buffer: Buffer.from('{"format":"wrong"}')});
    await page.waitForFunction(() => document.getElementById('skin-feedback').textContent.includes('导入失败'));
    assert.match(await page.locator('#skin-feedback').textContent(), /导入失败/);
    assert.equal(await page.locator('#skin-global').inputValue(), 'example.studio');
    await page.locator('#skin-file').setInputFiles(path.join(root, 'examples/skins/launchpad.faxskin'));
    await page.waitForFunction(() => [...document.getElementById('skin-global').options].some(option => option.value === 'example.launchpad'));
    await page.locator('#skin-global').selectOption('example.launchpad');
    await page.locator('.sidebar button[data-view="home"]').click();
    await page.waitForFunction(() => document.body.classList.contains('skin-shell-replaced'));
    const frame = page.frames().find(item => item.url().includes('/skin-frame.html'));
    assert.ok(frame, 'Custom GUI frame is missing');
    assert.equal(await frame.locator('#tools button').count(), 6);
    assert.deepEqual(await frame.evaluate(() => {
      let parentAccessible = true, storageAccessible = true;
      try { void parent.document.body; } catch (_) { parentAccessible = false; }
      try { localStorage.getItem('test'); } catch (_) { storageAccessible = false; }
      return {parentAccessible, storageAccessible, version: ToolboxSkin.version};
    }), {parentAccessible: false, storageAccessible: false, version: 1});
    if (process.env.FAX_SKIN_SCREENSHOTS) await page.screenshot({path: path.join(root, '.cache/skins-custom.png'), fullPage: true});
    await frame.getByRole('button', {name: 'Agent', exact: true}).click();
    await page.waitForFunction(() => document.body.dataset.view === 'ai');
    assert.equal(await page.locator('#view-ai').isVisible(), true);
    await page.locator('#skin-recovery-open').click();
    await page.locator('#skin-recovery-reset').click();
    assert.equal(await page.locator('#skin-global').inputValue(), 'fax.classic');
    assert.equal(await page.locator('#skin-global option').count(), 5);
    await page.locator('[data-skin-module="ai"]').selectOption('example.studio');
    await page.locator('#skin-manage').selectOption('example.studio'); await page.locator('#skin-remove').click();
    assert.equal(await page.locator('[data-skin-module="ai"]').inputValue(), '');
    assert.equal(await page.locator('#skin-global option').count(), 4);
    const broken = JSON.parse(fs.readFileSync(path.join(root, 'examples/skins/launchpad.faxskin'), 'utf8'));
    broken.id = 'example.broken'; broken.extension.script = 'throw new Error("intentional extension failure")';
    await page.locator('#skin-file').setInputFiles({name: 'broken.faxskin', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(broken))});
    await page.waitForFunction(() => [...document.getElementById('skin-global').options].some(option => option.value === 'example.broken'));
    await page.locator('#skin-global').selectOption('example.broken');
    await page.waitForFunction(() => document.getElementById('skin-feedback').textContent.includes('扩展已停用'));
    assert.equal(await page.locator('.app').isVisible(), true);
    await page.locator('#skin-reset').click();
    assert.equal(await page.locator('#skin-global').inputValue(), 'fax.classic');
    assert.deepEqual(errors.filter(message => !message.includes('intentional extension failure')), []);
  } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
});
