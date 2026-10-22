// 冒烟测试：手机竖屏视口里跑游戏，用合成的 Pointer 事件模拟手势，并截图
// 用法：先 npm start，再 node tools/smoke.mjs [输出目录]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const out = process.argv[2] || 'shots';
mkdirSync(out, { recursive: true });
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, hasTouch: true });
const errors = [];
page.on('pageerror', e => errors.push(e.message));
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
await page.goto('http://localhost:8080/?test');
await page.evaluate(() => localStorage.clear());
await page.reload();
await page.waitForTimeout(400);
await page.screenshot({ path: `${out}/0-title.png` });

// 在页面里按时间轴派发指针事件：[[dt, type, x, y, id], ...]
const gesture = (steps) => page.evaluate(async (steps) => {
  const c = document.getElementById('game');
  for (const [dt, type, x, y, id = 1] of steps) {
    await new Promise(r => setTimeout(r, dt));
    c.dispatchEvent(new PointerEvent(type, { pointerId: id, clientX: x, clientY: y, bubbles: true, cancelable: true, pointerType: 'touch' }));
  }
}, steps);
const tap = (x, y) => gesture([[0, 'pointerdown', x, y], [60, 'pointerup', x, y]]);
const drag = (x0, dx, ms) => {
  const s = [[0, 'pointerdown', x0, 600]];
  const n = Math.max(2, Math.round(ms / 16));
  for (let i = 1; i <= n; i++) s.push([16, 'pointermove', x0 + Math.min(1, i / 4) * dx, 600]);
  s.push([16, 'pointerup', x0 + dx, 600]);
  return gesture(s);
};
const flick = (x, y, dx, dy) => gesture([
  [0, 'pointerdown', x, y], [16, 'pointermove', x + dx * 0.3, y + dy * 0.3],
  [16, 'pointermove', x + dx * 0.7, y + dy * 0.7], [16, 'pointermove', x + dx, y + dy], [10, 'pointerup', x + dx, y + dy],
]);
const state = () => page.evaluate(() => {
  const g = window.game, p = g.player;
  return { state: g.state, room: g.room.id, x: Math.round(p.cx), y: Math.round(p.cy), hp: p.hp, ink: p.ink, grounded: p.grounded, atk: !!p.atk, vy: Math.round(p.vy) };
});

await tap(200, 400); // 开始
await page.waitForTimeout(300);
console.log('start', await state());

await drag(200, 40, 500);
await page.waitForTimeout(100);
console.log('after drag right', await state());

await flick(200, 600, 0, -60);
await page.waitForTimeout(120);
console.log('after flick up (jump)', await state());
await page.waitForTimeout(600);

await tap(100, 600);
await page.waitForTimeout(30);
console.log('after tap left (attack)', await state());
await page.waitForTimeout(300);
await page.screenshot({ path: `${out}/1-cradle.png` });

// 长按 → 蓄力环截图 → 松开 = 重击
await gesture([[0, 'pointerdown', 200, 600]]);
await page.waitForTimeout(650);
await page.screenshot({ path: `${out}/2-charge.png` });
await gesture([[0, 'pointerup', 200, 600]]);
await page.waitForTimeout(50);
console.log('after heavy', await state());

// 双指轻点 → 地图
await gesture([[0, 'pointerdown', 150, 500, 7], [20, 'pointerdown', 250, 500, 8], [60, 'pointerup', 150, 500, 7], [20, 'pointerup', 250, 500, 8]]);
await page.waitForTimeout(100);
console.log('two-finger tap', (await state()).state);
await page.screenshot({ path: `${out}/3-map.png` });
await tap(200, 400);
await page.waitForTimeout(100);

// 画圈识别（未解锁时不消耗，只检查识别器）
const circled = await page.evaluate(async () => {
  const g = window.game, c = document.getElementById('game');
  const evs = [];
  const orig = g.input.emit.bind(g.input);
  g.input.emit = (e) => { evs.push(e.type + (e.dir ? ':' + e.dir : '')); orig(e); };
  const send = (type, x, y) => c.dispatchEvent(new PointerEvent(type, { pointerId: 3, clientX: x, clientY: y, bubbles: true, pointerType: 'touch' }));
  send('pointerdown', 200 + 50, 500);
  for (let i = 1; i <= 26; i++) {
    await new Promise(r => setTimeout(r, 16));
    const a = i / 24 * Math.PI * 2;
    send('pointermove', 200 + Math.cos(a) * 50, 500 + Math.sin(a) * 50);
  }
  send('pointerup', 250, 500);
  g.input.emit = orig;
  return evs;
});
console.log('circle gesture events', circled);

// 传送到各房间截图
for (const [id, tx, ty] of [['hall', 18, 21], ['shaft', 41, 33], ['tower', 77, 40], ['gallery', 60, 21]]) {
  await page.evaluate(([tx, ty]) => {
    const g = window.game; g.player.x = tx * 16; g.player.y = ty * 16; g.player.vy = 0;
  }, [tx, ty]);
  await page.waitForTimeout(700);
  console.log('teleport', id, (await state()).room);
  await page.screenshot({ path: `${out}/4-${id}.png` });
}

console.log(errors.length ? 'ERRORS:\n' + errors.join('\n') : 'no page errors');
await browser.close();
process.exit(errors.length ? 1 : 0);
