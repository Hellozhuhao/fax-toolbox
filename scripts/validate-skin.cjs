const fs = require('node:fs');
const Core = require('../apps/windows-shell/ui/skin-core.js');
if (process.argv.length < 3) { console.error('Usage: node scripts/validate-skin.cjs <file.faxskin> [...]'); process.exitCode = 2; }
for (const file of process.argv.slice(2)) {
  try { const skin = Core.validatePackage(fs.readFileSync(file, 'utf8')); console.log(`${file}: OK — ${skin.id} / API v${skin.version} / ${skin.targets.join(', ')}`); }
  catch (error) { console.error(`${file}: ${error.message}`); process.exitCode = 1; }
}
