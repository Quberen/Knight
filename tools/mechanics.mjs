// 机制测试（第二版交互）：用「指令」通关整条路线 + 手势识别 + 战斗/存档
// 用法：先 npm start，再 node tools/mechanics.mjs
import { chromium } from 'playwright';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
const errors = [];
page.on('pageerror', e => errors.push(e.message));
await page.goto('http://localhost:8080/');
await page.evaluate(() => localStorage.clear());
await page.reload();
await page.waitForTimeout(300);
await page.evaluate(() => { window.game.state = 'play'; });

const T = 16;
let fails = 0;
const check = (name, ok, info) => { console.log(`${ok ? '✓' : '✗'} ${name}`, ok ? '' : JSON.stringify(info)); if (!ok) fails++; };
const st = () => page.evaluate(() => {
  const g = window.game, p = g.player;
  return { room: g.room.id, tx: Math.floor(p.cx / 16), ty: Math.floor((p.y + p.h - 1) / 16), hp: p.hp, ink: p.ink, grounded: p.grounded,
    cmd: g.pilot.cmd?.kind ?? null, abilities: g.save.abilities, bench: g.save.bench, broken: g.save.broken.length, state: g.state, enemies: g.enemies.length };
});
const noEnemies = () => page.evaluate(() => { window.game.enemies = []; window.game.resetEnemies = () => { window.game.enemies = []; }; });
// 下达「去 (tx,ty) 格」指令并等到执行完
const go = async (tx, ty, timeout = 12000) => {
  await page.evaluate(([x, y]) => window.game.pilot.goto(x, y), [tx * T + 8, (ty + 1) * T - 4]);
  const t0 = Date.now();
  while (Date.now() - t0 < timeout) {
    await page.waitForTimeout(100);
    const s = await st();
    if (!s.cmd && s.grounded) return s;
  }
  return { ...(await st()), timeout: true };
};

// ---------- 1. 用指令通关路线（先清掉敌人，只测导航） ----------
await noEnemies();
let s = await go(8, 17);
check('摇篮：点高处平台 → 自动跳上去', s.tx === 8 && s.ty === 17, s);
s = await go(2, 13);
check('摇篮：再点更高的平台', s.tx === 2 && s.ty === 13, s);
s = await go(11, 21);
check('点门口 → 自动走进长廊', s.room === 'hall', s);
s = await go(33, 21);
check('长廊：自动跳过尖刺坑走到另一头', s.room === 'hall' && s.tx === 33 && s.hp === 5, s);
s = await go(36, 21);
check('进入竖井', s.room === 'shaft', s);
s = await go(41, 45);
check('竖井：一路落到底（含穿平台）', s.room === 'shaft' && s.ty === 45, s);
s = await go(36, 45);
s = await go(26, 45);
check('疾影祠：走到尖刺坑前', s.room === 'shrine' && s.tx === 26, s);
s = await go(14, 45);
check('跳过祠内尖刺拿到疾影', s.abilities.dash && s.hp === 5, s);
await page.waitForTimeout(3300);
s = await go(35, 45);   // 疾影祠右侧门口 → 进竖井
s = await go(47, 45);   // 竖井右侧门口 → 进刺渊
s = await go(53, 45);
check('进入刺渊', s.room === 'abyss', s);
s = await go(66, 45);
check('刺渊：自动冲刺跳过 6 格尖刺', s.tx === 66 && s.hp === 5, s);
s = await go(75, 45);
check('进入高塔', s.room === 'tower', s);
s = await go(81, 11, 25000);
await page.waitForTimeout(200);
s = await st();
check('高塔：一路爬到顶拿到墨旋', s.abilities.vortex && s.hp === 5, s);
await page.waitForTimeout(3300);
s = await go(70, 11);
check('进入回廊', s.room === 'gallery', s);
s = await go(59, 19);
await page.waitForTimeout(300);
s = await st();
check('回廊：跳上平台拿到墨弹', s.abilities.bolt, s);
await page.waitForTimeout(3300);
s = await go(49, 11);
await page.evaluate(() => window.game.command(...window.game.worldToScreen(46 * 16 + 8, 10 * 16 + 8), false));
await page.waitForTimeout(1500);
s = await st();
check('点裂墙（普通）：敲不碎', s.broken === 0, s);
await page.evaluate(() => window.game.command(...window.game.worldToScreen(46 * 16 + 8, 10 * 16 + 8), true));
await page.waitForTimeout(1500);
s = await st();
check('长按裂墙：走过去重击打碎', s.broken === 3, s);
s = await go(43, 11);
check('穿过捷径回到竖井', s.room === 'shaft', s);

// ---------- 2. 战斗：点敌人自动追击 ----------
await page.evaluate(() => {
  const g = window.game;
  delete g.resetEnemies; g.resetEnemies();
  const p = g.player; p.x = 14 * 16; p.y = 21 * 16 - 14 + 16; p.vx = p.vy = 0; p.ink = 0; p.hp = p.maxHp;
});
await page.waitForTimeout(500);
const killed = await page.evaluate(async () => {
  const g = window.game;
  const e = g.enemies.find(e => e.constructor.name === 'Crawler' && g.world.roomAtPx(e.cx, e.cy) === g.room);
  g.command(...g.worldToScreen(e.cx, e.cy), false);
  const t0 = performance.now();
  while (!e.dead && performance.now() - t0 < 6000) await new Promise(r => setTimeout(r, 50));
  return { dead: e.dead, ink: g.player.ink };
});
check('点爬虫 → 追上去砍死，并积攒墨', killed.dead && killed.ink >= 33, killed);

// ---------- 3. 手势识别（合成多指 Pointer 事件） ----------
const gesture = (steps) => page.evaluate(async (steps) => {
  const c = document.getElementById('game');
  for (const [dt, type, x, y, id = 1] of steps) {
    if (dt) await new Promise(r => setTimeout(r, dt));
    c.dispatchEvent(new PointerEvent(type, { pointerId: id, clientX: x, clientY: y, bubbles: true, cancelable: true, pointerType: 'touch' }));
  }
}, steps);
const spy = () => page.evaluate(() => {
  const g = window.game; g._evs = [];
  const orig = g.input.emit.bind(g.input);
  g.input.emit = (e) => { g._evs.push(e.type + (e.dir ? ':' + e.dir : '')); orig(e); };
});
const evs = () => page.evaluate(() => window.game._evs.splice(0));
await noEnemies(); // 手势测试不让敌人打断（受击会中断凝神）
await spy();
await page.evaluate(() => { window.game.player.dashCd = 0; });

await gesture([[0, 'pointerdown', 200, 500], [16, 'pointermove', 215, 500], [16, 'pointermove', 245, 502], [16, 'pointerup', 250, 502]]);
await page.waitForTimeout(50);
check('单指右划 → 闪身', (await evs()).includes('swipe:right'));
await gesture([[0, 'pointerdown', 200, 500], [16, 'pointermove', 210, 480], [16, 'pointermove', 225, 455], [16, 'pointerup', 230, 450]]);
await page.waitForTimeout(50);
check('单指斜上划 → 跳', (await evs()).includes('swipe:up'));

await page.evaluate(() => { window.game.player.hp = 3; window.game.player.ink = 66; });
await page.waitForTimeout(800);
await gesture([[0, 'pointerdown', 150, 500, 11], [30, 'pointerdown', 250, 500, 12]]);
await page.waitForTimeout(1300);
const focus = await page.evaluate(() => ({ hp: window.game.player.hp, focusing: window.game.input.focusing }));
await gesture([[0, 'pointerup', 150, 500, 11], [10, 'pointerup', 250, 500, 12]]);
check('双指按住 → 凝神回血', focus.focusing && focus.hp === 4, focus);

const twist = [[0, 'pointerdown', 150, 500, 21], [20, 'pointerdown', 250, 500, 22]];
for (let i = 1; i <= 8; i++) {
  const a = i / 8 * 1.4;
  twist.push([16, 'pointermove', 200 - Math.cos(a) * 50, 500 - Math.sin(a) * 50, 21], [0, 'pointermove', 200 + Math.cos(a) * 50, 500 + Math.sin(a) * 50, 22]);
}
twist.push([16, 'pointerup', 0, 0, 21], [0, 'pointerup', 0, 0, 22]);
await page.evaluate(() => { window.game.player.ink = 99; });
await gesture(twist);
await page.waitForTimeout(80);
s = await st();
check('双指拧转 → 墨旋（耗 33 墨）', (await evs()).some(e => e.startsWith('twist')) && s.ink === 66, s);

await gesture([[0, 'pointerdown', 150, 500, 31], [20, 'pointerdown', 200, 500, 32],
  [16, 'pointermove', 170, 500, 31], [0, 'pointermove', 220, 500, 32],
  [16, 'pointermove', 210, 498, 31], [0, 'pointermove', 260, 498, 32],
  [16, 'pointerup', 210, 498, 31], [0, 'pointerup', 260, 498, 32]]);
await page.waitForTimeout(80);
s = await st();
check('双指一起右划 → 墨弹（耗 33 墨）', s.ink === 33, s);
await evs();

await gesture([[0, 'pointerdown', 120, 500, 41], [20, 'pointerdown', 200, 500, 42], [20, 'pointerdown', 280, 500, 43],
  [60, 'pointerup', 120, 500, 41], [10, 'pointerup', 200, 500, 42], [10, 'pointerup', 280, 500, 43]]);
await page.waitForTimeout(80);
check('三指轻点 → 地图', (await st()).state === 'map');
await gesture([[0, 'pointerdown', 200, 400], [50, 'pointerup', 200, 400]]);
await page.waitForTimeout(80);
check('轻点关闭地图', (await st()).state === 'play');

// ---------- 4. 长椅 / 尖刺 / 死亡 / 存档 ----------
await noEnemies();
await page.evaluate(() => { const p = window.game.player; p.x = 20 * 16; p.y = 21 * 16 - 14 + 16; p.hp = 3; });
await page.waitForTimeout(400);
await page.evaluate(() => { const g = window.game; g.command(...g.worldToScreen(6 * 16 + 8, 21 * 16 + 8), false); });
await page.waitForTimeout(4000);
s = await st();
check('点长椅 → 走过去休息存档', s.bench === 'B@6,21' && s.hp === s.hp && s.room === 'cradle' && s.hp >= 5, s);

await page.evaluate(() => { const p = window.game.player; p.x = 22 * 16; p.y = 19 * 16; p.vx = p.vy = 0; p.invuln = 0; });
await page.waitForTimeout(1300);
s = await st();
check('掉进尖刺扣血并回到安全点', s.hp === 4 && s.grounded, s);

await page.evaluate(() => { const g = window.game; g.player.hp = 1; g.player.invuln = 0; g.player.hurt(g, 0); });
await page.waitForTimeout(2200);
s = await st();
check('死亡后回到长椅满血', s.room === 'cradle' && s.hp >= 5, s);

await page.reload(); await page.waitForTimeout(300);
s = await st();
check('刷新后读取存档', s.abilities.dash && s.abilities.vortex && s.abilities.bolt && s.broken === 3, s);

console.log(errors.length ? 'ERRORS:\n' + errors.join('\n') : 'no page errors');
await browser.close();
process.exit(fails || errors.length ? 1 : 0);
