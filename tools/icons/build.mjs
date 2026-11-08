// Turns icons/*.svg into Kotlin path data, measures every icon, and draws a review sheet.
//
//   cd tools/icons && npm install && node build.mjs
//
// Each SVG is drawn on a 24×24 grid with 2px round strokes. Two inks only:
// full ink for lines, and ink at 35% for fills that show weight or state.
// The checks follow the review sheet the design is based on:
//   着色 (ink)     – inked area in px² at 24px; keeps icons equally heavy
//   重心 (balance) – centre of the ink; must sit within 1px of the middle
//   边距 (margin)  – nothing may touch the outer 1px of the grid
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import svgpath from 'svgpath';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  ({ chromium } = require('/opt/node-tools/node_modules/playwright'));
}

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const srcDir = path.join(root, 'icons');
const outDir = path.join(here, 'out');
const ktFile = path.join(root, 'core/src/main/kotlin/io/github/quberen/knight/ui/Icons.kt');
const resDir = path.join(root, 'app/src/main/res');

const INK = '#2B3240';
const LIMITS = { inkMin: 45, inkMax: 120, balance: 1.5, margin: 1.0 };

// ---- read the SVGs -----------------------------------------------------------

const attrs = (s) => Object.fromEntries([...s.matchAll(/([\w:-]+)="([^"]*)"/g)].map((m) => [m[1], m[2]]));
const num = (v, d = 0) => (v === undefined ? d : parseFloat(v));

function shapeToPath(tag, a) {
  switch (tag) {
    case 'path':
      return a.d;
    case 'circle': {
      const [cx, cy, r] = [num(a.cx), num(a.cy), num(a.r)];
      return `M${cx - r} ${cy}A${r} ${r} 0 1 0 ${cx + r} ${cy}A${r} ${r} 0 1 0 ${cx - r} ${cy}Z`;
    }
    case 'rect': {
      const [x, y, w, h] = [num(a.x), num(a.y), num(a.width), num(a.height)];
      const r = Math.min(num(a.rx, num(a.ry)), w / 2, h / 2);
      if (!r) return `M${x} ${y}H${x + w}V${y + h}H${x}Z`;
      return `M${x + r} ${y}H${x + w - r}A${r} ${r} 0 0 1 ${x + w} ${y + r}V${y + h - r}` +
        `A${r} ${r} 0 0 1 ${x + w - r} ${y + h}H${x + r}A${r} ${r} 0 0 1 ${x} ${y + h - r}` +
        `V${y + r}A${r} ${r} 0 0 1 ${x + r} ${y}Z`;
    }
    case 'line':
      return `M${a.x1} ${a.y1}L${a.x2} ${a.y2}`;
    case 'polyline':
    case 'polygon': {
      const p = a.points.trim().split(/[\s,]+/).map(Number);
      let d = `M${p[0]} ${p[1]}`;
      for (let i = 2; i < p.length; i += 2) d += `L${p[i]} ${p[i + 1]}`;
      return tag === 'polygon' ? d + 'Z' : d;
    }
  }
  throw new Error(`unsupported <${tag}>`);
}

/** Absolute path ops: [0,x,y] move, [1,x,y] line, [2,x1,y1,x2,y2,x,y] cubic, [3] close. */
function toOps(d) {
  const ops = [];
  let cx = 0, cy = 0, sx = 0, sy = 0;
  const r = (v) => Math.round(v * 1000) / 1000;
  for (const s of svgpath(d).abs().unshort().unarc().segments) {
    switch (s[0]) {
      case 'M': ops.push([0, r(s[1]), r(s[2])]); cx = sx = s[1]; cy = sy = s[2]; break;
      case 'L': ops.push([1, r(s[1]), r(s[2])]); cx = s[1]; cy = s[2]; break;
      case 'H': ops.push([1, r(s[1]), r(cy)]); cx = s[1]; break;
      case 'V': ops.push([1, r(cx), r(s[1])]); cy = s[1]; break;
      case 'C': ops.push([2, ...s.slice(1).map(r)]); cx = s[5]; cy = s[6]; break;
      case 'Q': {
        const [qx, qy, x, y] = s.slice(1);
        ops.push([2, r(cx + (2 / 3) * (qx - cx)), r(cy + (2 / 3) * (qy - cy)),
          r(x + (2 / 3) * (qx - x)), r(y + (2 / 3) * (qy - y)), r(x), r(y)]);
        cx = x; cy = y; break;
      }
      case 'Z': ops.push([3]); cx = sx; cy = sy; break;
      default: throw new Error(`unexpected ${s[0]} in ${d}`);
    }
  }
  return ops;
}

function readIcon(file) {
  const svg = fs.readFileSync(path.join(srcDir, file), 'utf8');
  const rootAttrs = attrs(svg.match(/<svg\b([^>]*)>/)[1]);
  const base = path.basename(file, '.svg');
  const id = base.replace(/^\d+-/, '');
  const layers = [];
  for (const m of svg.matchAll(/<(path|circle|rect|line|polyline|polygon)\b([^>]*?)\/?>/g)) {
    const a = { ...rootAttrs, ...attrs(m[2]) };
    const ops = toOps(shapeToPath(m[1], a));
    const opacity = num(a.opacity, 1);
    if (a.fill && a.fill !== 'none') {
      layers.push({ style: 'FILL', alpha: num(a['fill-opacity'], 1) * opacity, width: 0, evenOdd: a['fill-rule'] === 'evenodd', ops });
    }
    if (a.stroke && a.stroke !== 'none') {
      layers.push({ style: 'STROKE', alpha: num(a['stroke-opacity'], 1) * opacity, width: num(a['stroke-width'], 1), evenOdd: false, ops });
    }
  }
  return {
    file, id, svg, layers,
    name: rootAttrs['data-name'] ?? id,
    number: base.match(/^(\d+)/)?.[1] ?? '',
    // State icons (filled = on) and the app icon may be heavier, like ON/OFF pairs in the reference.
    inkMax: num(rootAttrs['data-ink-max'], LIMITS.inkMax),
    note: rootAttrs['data-note'] ?? '',
  };
}

const icons = fs.readdirSync(srcDir).filter((f) => f.endsWith('.svg')).sort().map(readIcon);

// ---- Kotlin ----------------------------------------------------------------

const constName = (id) => id.toUpperCase().replace(/[^A-Z0-9]+/g, '_');
const f = (v) => (Number.isInteger(v) ? `${v}f` : `${v}f`);
let kt = `// Generated by tools/icons/build.mjs from icons/*.svg. Edit the SVGs, not this file.
package io.github.quberen.knight.ui

@Suppress("unused")
object Icons {
`;
for (const ic of icons) {
  kt += `    /** ${ic.name} */\n    val ${constName(ic.id)} = Icon(\n        "${ic.id}",\n        arrayOf(\n`;
  for (const l of ic.layers) {
    const flat = l.ops.flat().map(f).join(', ');
    kt += `            Layer(Layer.${l.style}, ${f(Math.round(l.alpha * 100) / 100)}, ${f(l.width)}, ${l.evenOdd}, floatArrayOf(${flat})),\n`;
  }
  kt += `        ),\n    )\n\n`;
}
kt = kt.trimEnd() + '\n}\n';
fs.mkdirSync(path.dirname(ktFile), { recursive: true });
fs.writeFileSync(ktFile, kt);

// ---- launcher icon (Android vector drawables) ------------------------------

function vectorPath(l, color) {
  const d = l.ops.map((o) => ['M', 'L', 'C', 'Z'][o[0]] + o.slice(1).join(' ')).join('');
  if (l.style === 'FILL') {
    return `<path android:fillColor="${color}" android:fillAlpha="${l.alpha}"${l.evenOdd ? ' android:fillType="evenOdd"' : ''} android:pathData="${d}" />`;
  }
  return `<path android:strokeColor="${color}" android:strokeAlpha="${l.alpha}" android:strokeWidth="${l.width}" ` +
    `android:strokeLineCap="round" android:strokeLineJoin="round" android:pathData="${d}" />`;
}

const knight = icons.find((i) => i.id === 'knight');
if (knight) {
  const group = (scale, offset, color) =>
    `    <group android:scaleX="${scale}" android:scaleY="${scale}" android:translateX="${offset}" android:translateY="${offset}">\n` +
    knight.layers.map((l) => '        ' + vectorPath(l, color)).join('\n') + '\n    </group>\n';
  const head = (w, v) => `<?xml version="1.0" encoding="utf-8"?>\n<!-- Generated by tools/icons/build.mjs from icons/33-knight.svg. -->\n` +
    `<vector xmlns:android="http://schemas.android.com/apk/res/android"\n    android:width="${w}dp" android:height="${w}dp"\n    android:viewportWidth="${v}" android:viewportHeight="${v}">\n`;
  // Adaptive icon: 108 viewport, the knight fills the middle 54.
  fs.mkdirSync(path.join(resDir, 'drawable'), { recursive: true });
  fs.writeFileSync(path.join(resDir, 'drawable/ic_launcher_foreground.xml'),
    head(108, 108) + group(2.25, 27, '#FFFFFF') + '</vector>\n');
  fs.writeFileSync(path.join(resDir, 'drawable/ic_launcher_monochrome.xml'),
    head(108, 108) + group(2.25, 27, '#000000') + '</vector>\n');
  // Android 8+: adaptive icon with a themed (monochrome) layer.
  fs.mkdirSync(path.join(resDir, 'drawable-v26'), { recursive: true });
  fs.writeFileSync(path.join(resDir, 'drawable-v26/ic_launcher.xml'),
    `<?xml version="1.0" encoding="utf-8"?>\n<!-- Generated by tools/icons/build.mjs. -->\n` +
    `<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n` +
    `    <background android:drawable="@color/launcher_background" />\n` +
    `    <foreground android:drawable="@drawable/ic_launcher_foreground" />\n` +
    `    <monochrome android:drawable="@drawable/ic_launcher_monochrome" />\n</adaptive-icon>\n`);
  // Older Android: rounded square with the knight.
  fs.writeFileSync(path.join(resDir, 'drawable/ic_launcher.xml'),
    head(48, 24) +
    `    <path android:fillColor="${INK}" android:pathData="M6 1H18A5 5 0 0 1 23 6V18A5 5 0 0 1 18 23H6A5 5 0 0 1 1 18V6A5 5 0 0 1 6 1Z" />\n` +
    group(0.7, 3.6, '#FFFFFF') + '</vector>\n');
}

// ---- measure and draw the review sheet --------------------------------------

fs.mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 2 });
await page.setContent('<canvas id="c"></canvas>');
const measured = await page.evaluate(async ({ list, ink }) => {
  const S = 40, N = 24 * S;
  const c = document.getElementById('c');
  c.width = N; c.height = N;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  const out = [];
  for (const svg of list) {
    const img = new Image();
    img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg.replaceAll('currentColor', ink));
    await img.decode();
    ctx.clearRect(0, 0, N, N);
    ctx.drawImage(img, 0, 0, N, N);
    const d = ctx.getImageData(0, 0, N, N).data;
    let sum = 0, sx = 0, sy = 0, x0 = N, y0 = N, x1 = -1, y1 = -1;
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const a = d[(y * N + x) * 4 + 3] / 255;
      if (!a) continue;
      sum += a; sx += a * (x + 0.5); sy += a * (y + 0.5);
      if (a > 0.05) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
    }
    out.push({ ink: sum / (S * S), cx: sx / sum / S, cy: sy / sum / S, box: [x0 / S, y0 / S, (x1 + 1) / S, (y1 + 1) / S] });
  }
  return out;
}, { list: icons.map((i) => i.svg), ink: INK });

const report = icons.map((ic, i) => {
  const m = measured[i];
  const problems = [];
  if (m.ink < LIMITS.inkMin) problems.push('偏轻');
  if (m.ink > ic.inkMax) problems.push('偏重');
  if (Math.abs(m.cx - 12) > LIMITS.balance || Math.abs(m.cy - 12) > LIMITS.balance) problems.push('重心偏');
  const [x0, y0, x1, y1] = m.box;
  if (x0 < LIMITS.margin || y0 < LIMITS.margin || x1 > 24 - LIMITS.margin || y1 > 24 - LIMITS.margin) problems.push('越界');
  return { ...ic, ...m, problems };
});

fs.writeFileSync(path.join(outDir, 'report.json'), JSON.stringify(report.map(({ svg, layers, ...r }) => r), null, 2));

const card = (r) => `
  <div class="card">
    <div class="top"><span>${r.number}${r.note ? ` <em>${r.note}</em>` : ''}</span><span class="${r.problems.length ? 'bad' : 'ok'}">${r.problems.length ? r.problems.join(' · ') : '已通过'}</span></div>
    <div class="icon">${r.svg.replace('<svg ', '<svg width="64" height="64" ')}</div>
    <div class="name">${r.name}</div>
    <div class="m">着色 ${r.ink.toFixed(2)}</div>
    <div class="s">重心 ${r.cx.toFixed(2)}, ${r.cy.toFixed(2)}</div>
  </div>`;
const html = `<!doctype html><meta charset="utf-8"><style>
  body { margin: 0; padding: 28px; background: #FBFBFC; font-family: Inter, "WenQuanYi Zen Hei", sans-serif; color: ${INK}; }
  h1 { font-size: 20px; font-weight: 600; margin: 0 0 4px; }
  p { margin: 0 0 20px; color: #6B7280; font-size: 13px; }
  .grid { display: grid; grid-template-columns: repeat(8, 220px); gap: 16px; }
  .card { background: #F3F4F6; border-radius: 14px; padding: 14px 16px 18px; text-align: center; }
  .top { display: flex; justify-content: space-between; font-size: 14px; color: #3A4150; }
  .ok { color: #3D8B5A; } .bad { color: #C2410C; } em { font-style: normal; color: #8A92A0; font-size: 12px; }
  .icon { color: ${INK}; height: 72px; display: flex; align-items: center; justify-content: center; margin: 14px 0 10px; }
  .name { font-size: 19px; margin-bottom: 14px; }
  .m { font-size: 16px; margin-bottom: 6px; } .s { font-size: 14px; }
</style>
<h1>Knight 图标检查</h1>
<p>24 网格 · 2px 圆头描边 · 着色 ${LIMITS.inkMin}–${LIMITS.inkMax} · 重心偏差 ≤ ${LIMITS.balance}px · 四周留 ${LIMITS.margin}px</p>
<div class="grid">${report.map(card).join('')}</div>`;
fs.writeFileSync(path.join(outDir, 'sheet.html'), html);
await page.setViewportSize({ width: 8 * 220 + 7 * 16 + 56, height: 600 });
await page.setContent(html);
await page.screenshot({ path: path.join(outDir, 'sheet.png'), fullPage: true });
await browser.close();

const failed = report.filter((r) => r.problems.length);
for (const r of report) {
  console.log(`${r.number} ${r.id.padEnd(18)} ink ${r.ink.toFixed(1).padStart(6)}  c ${r.cx.toFixed(2)},${r.cy.toFixed(2)}  ${r.problems.join(' ') || 'ok'}`);
}
console.log(`${report.length - failed.length}/${report.length} passed`);
