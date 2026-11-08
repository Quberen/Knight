// Draws text that Java2D can't (colour emoji, rare symbols in kaomoji) with Chromium,
// whose emoji font is the same Noto Color Emoji that Android uses.
// Called by PreviewTest:  node tools/preview/raster.mjs <requests.json> <outDir>
// requests: [{ key, text, size, color }]  ->  <outDir>/<key>.png and <outDir>/index.json
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  ({ chromium } = require('/opt/node-tools/node_modules/playwright'));
}

const [reqFile, outDir] = process.argv.slice(2);
const requests = JSON.parse(fs.readFileSync(reqFile, 'utf8'));
fs.mkdirSync(outDir, { recursive: true });
const indexFile = path.join(outDir, 'index.json');
const index = fs.existsSync(indexFile) ? JSON.parse(fs.readFileSync(indexFile, 'utf8')) : {};

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setContent('<canvas id="c"></canvas>');
const drawn = await page.evaluate((list) => {
  const c = document.getElementById('c');
  const ctx = c.getContext('2d');
  const out = {};
  for (const r of list) {
    const font = `${r.size}px "WenQuanYi Zen Hei", Inter, "Noto Color Emoji", sans-serif`;
    ctx.font = font;
    const m = ctx.measureText(r.text);
    const ascent = Math.ceil(m.fontBoundingBoxAscent);
    const height = ascent + Math.ceil(m.fontBoundingBoxDescent);
    c.width = Math.max(1, Math.ceil(m.width));
    c.height = Math.max(1, height);
    ctx.font = font;
    ctx.fillStyle = r.color;
    ctx.fillText(r.text, 0, ascent);
    out[r.key] = { width: m.width, ascent, data: c.toDataURL('image/png') };
  }
  return out;
}, requests);
await browser.close();

for (const [key, v] of Object.entries(drawn)) {
  fs.writeFileSync(path.join(outDir, `${key}.png`), Buffer.from(v.data.split(',')[1], 'base64'));
  index[key] = { width: v.width, ascent: v.ascent };
}
fs.writeFileSync(indexFile, JSON.stringify(index));
console.log(`drew ${Object.keys(drawn).length} strings`);
