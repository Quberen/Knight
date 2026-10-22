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
const state = () => page.evaluate(() => {
  const g = window.game, p = g.player;
  return { state: g.state, room: g.room.id, x: Math.round(p.cx), y: Math.round(p.cy), hp: p.hp, cmd: g.pilot.cmd?.kind ?? null };
});

await tap(200, 400); // 开始
await page.waitForTimeout(300);
console.log('start', await state());

// 点高处平台：截一张寻路途中的图
const [px, py] = await page.evaluate(() => window.game.worldToScreen(8 * 16 + 8, 17 * 16 + 8));
await tap(px, py);
await page.waitForTimeout(350);
await page.screenshot({ path: `${out}/1-goto.png` });
await page.waitForTimeout(1500);
console.log('after tap platform', await state());

// 长按 → 蓄力环
await gesture([[0, 'pointerdown', 200, 600]]);
await page.waitForTimeout(450);
await page.screenshot({ path: `${out}/2-charge.png` });
await gesture([[0, 'pointerup', 200, 600]]);
await page.waitForTimeout(300);

// 双指按住 → 凝神（连线 + 凝神圈）
await gesture([[0, 'pointerdown', 140, 560, 5], [20, 'pointerdown', 260, 560, 6]]);
await page.waitForTimeout(500);
await page.screenshot({ path: `${out}/3-focus.png` });
await gesture([[0, 'pointerup', 140, 560, 5], [0, 'pointerup', 260, 560, 6]]);

// 三指轻点 → 地图
await gesture([[0, 'pointerdown', 120, 500, 7], [20, 'pointerdown', 200, 500, 8], [20, 'pointerdown', 280, 500, 9],
  [60, 'pointerup', 120, 500, 7], [10, 'pointerup', 200, 500, 8], [10, 'pointerup', 280, 500, 9]]);
await page.waitForTimeout(100);
console.log('three-finger tap', (await state()).state);
await page.screenshot({ path: `${out}/4-map.png` });
await tap(200, 400);
await page.waitForTimeout(100);

// 长廊：点敌人 → 锁定追击
await page.evaluate(() => { const p = window.game.player; p.x = 14 * 16; p.y = 21 * 16 - 14 + 16; p.vx = p.vy = 0; });
await page.waitForTimeout(700);
await page.evaluate(() => {
  const g = window.game;
  const e = g.enemies.find(e => g.world.roomAtPx(e.cx, e.cy) === g.room);
  g.command(...g.worldToScreen(e.cx, e.cy), false);
});
await page.waitForTimeout(500);
await page.screenshot({ path: `${out}/5-engage.png` });
console.log('engage', await state());

// 传送到各房间截图
for (const [id, tx, ty] of [['hall', 18, 21], ['shaft', 41, 33], ['tower', 77, 40], ['gallery', 60, 21]]) {
  await page.evaluate(([tx, ty]) => {
    const g = window.game; g.player.x = tx * 16; g.player.y = ty * 16; g.player.vy = 0;
  }, [tx, ty]);
  await page.waitForTimeout(700);
  console.log('teleport', id, (await state()).room);
  await page.screenshot({ path: `${out}/6-${id}.png` });
}

console.log(errors.length ? 'ERRORS:\n' + errors.join('\n') : 'no page errors');
await browser.close();
process.exit(errors.length ? 1 : 0);
