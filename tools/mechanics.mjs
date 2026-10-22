// 机制测试：尖刺回位、长椅存档、拾取能力、冲刺过坑、重击碎墙、死亡复活、技能耗墨
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
const put = (tx, ty) => page.evaluate(([x, y]) => {
  const p = window.game.player; p.x = x; p.y = y; p.vx = p.vy = 0;
}, [tx * T + 3, (ty + 1) * T - 14]);
const st = () => page.evaluate(() => {
  const g = window.game, p = g.player;
  return { room: g.room.id, tx: +(p.cx / 16).toFixed(1), ty: +((p.y + p.h) / 16).toFixed(1), hp: p.hp, ink: p.ink, grounded: p.grounded, save: g.save };
});
const ev = (e) => page.evaluate((e) => window.game.input.emit(e), e);
const key = async (k, ms) => { await page.keyboard.down(k); await page.waitForTimeout(ms); await page.keyboard.up(k); };
let fails = 0;
const check = (name, ok, info) => { console.log(`${ok ? '✓' : '✗'} ${name}`, ok ? '' : JSON.stringify(info)); if (!ok) fails++; };

// 1. 尖刺：长廊坑（列 9-11 → 世界 21-23，行 22）
await put(19, 21); await page.waitForTimeout(400);
await put(22, 19); await page.waitForTimeout(1200);
let s = await st();
check('尖刺扣血并回到安全点', s.hp === 4 && s.grounded && (s.tx < 21 || s.tx > 24), s);

// 2. 长椅：摇篮 B 在 (6,21)
await put(6, 21); await page.waitForTimeout(400);
await ev({ type: 'flick', dir: 'down', held: false }); await page.waitForTimeout(100);
s = await st();
check('长椅休息：回血 + 存档', s.hp === 5 && s.save.bench === 'B@6,21', s);

// 3. 拾取疾影：疾影祠 D 在 (14,45)
await put(14, 45); await page.waitForTimeout(500);
s = await st();
check('拾取疾影', s.save.abilities.dash, s);
await page.waitForTimeout(3300);

// 4. 冲刺过刺渊：坑在世界列 56-61，从坑边起跳
await put(54, 45); await page.waitForTimeout(400);
await page.keyboard.down('ArrowRight');
await page.waitForTimeout(150);
await ev({ type: 'flick', dir: 'up', held: true });
await page.waitForTimeout(260);
await ev({ type: 'flick', dir: 'right', held: false });
await page.waitForTimeout(700);
await page.keyboard.up('ArrowRight');
s = await st();
check('跳 + 冲刺越过 6 格尖刺坑', s.hp === 5 && s.tx > 62, s);

// 不冲刺应该过不去
await put(54, 45); await page.waitForTimeout(400);
await page.keyboard.down('ArrowRight');
await page.waitForTimeout(150);
await ev({ type: 'flick', dir: 'up', held: true });
await page.waitForTimeout(900);
await page.keyboard.up('ArrowRight');
await page.waitForTimeout(800);
s = await st();
check('只跳过不去（掉坑扣血）', s.hp === 4, s);

// 5. 重击碎墙：竖井裂墙在世界 (46, 9-11)，站在 (44, 11)
await put(44, 11); await page.waitForTimeout(400);
await page.evaluate(() => { window.game.player.facing = 1; });
await ev({ type: 'keyAttack' }); await page.waitForTimeout(400);
s = await st();
check('普通攻击打不碎裂墙', s.save.broken.length === 0, s);
await ev({ type: 'keyHeavy' }); await page.waitForTimeout(400);
s = await st();
check('重击打碎裂墙', s.save.broken.length === 3, s.save.broken);

// 6. 技能：拿墨旋后画圈耗墨
await put(81, 11); await page.waitForTimeout(500);
s = await st();
check('拾取墨旋并补满墨', s.save.abilities.vortex && s.ink === 99, s);
await page.waitForTimeout(3300);
await ev({ type: 'circle' }); await page.waitForTimeout(100);
s = await st();
check('画圈释放墨旋耗 33 墨', s.ink === 66, s);

// 7. 凝神：扣血后长按回血
await page.evaluate(() => { window.game.player.hp = 3; });
await page.keyboard.down('f'); await page.waitForTimeout(1100); await page.keyboard.up('f');
s = await st();
check('凝神回 1 血耗 33 墨', s.hp === 4 && s.ink === 33, s);

// 8. 死亡回长椅
await page.evaluate(() => { const g = window.game; g.player.hp = 1; g.player.invuln = 0; g.player.hurt(g, 0); });
await page.waitForTimeout(2200);
s = await st();
check('死亡后回到长椅满血', s.room === 'cradle' && s.hp === 5, s);

// 9. 存档持久化
await page.reload(); await page.waitForTimeout(300);
s = await st();
check('刷新后读取存档', s.save.abilities.dash && s.save.abilities.vortex && s.save.broken.length === 3 && s.room === 'cradle', s);

console.log(errors.length ? 'ERRORS:\n' + errors.join('\n') : 'no page errors');
await browser.close();
process.exit(fails || errors.length ? 1 : 0);
