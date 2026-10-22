// 校验房间尺寸、门洞对齐、出生点：node tools/check-world.mjs
import { ROOMS, World, CW, CH } from '../src/world.js';

let errors = 0;
const err = (m) => { errors++; console.error('✗ ' + m); };

for (const r of ROOMS) {
  const W = r.w * CW, H = r.h * CH;
  if (r.map.length !== H) err(`${r.id}: 行数 ${r.map.length}，应为 ${H}`);
  r.map.forEach((row, i) => {
    if (row.length !== W) err(`${r.id}: 第 ${i} 行宽 ${row.length}，应为 ${W}`);
  });
}

// 单元不能重叠
const used = new Map();
for (const r of ROOMS) {
  for (let y = r.cy; y < r.cy + r.h; y++) for (let x = r.cx; x < r.cx + r.w; x++) {
    const k = `${x},${y}`;
    if (used.has(k)) err(`单元 ${k} 同时属于 ${used.get(k)} 和 ${r.id}`);
    used.set(k, r.id);
  }
}

const world = new World(ROOMS);
const open = (tx, ty) => !world.solid(tx, ty);

// 房间边缘的空格，外侧必须是另一个房间的空格
for (const r of ROOMS) {
  const x0 = r.cx * CW, y0 = r.cy * CH, x1 = x0 + r.w * CW - 1, y1 = y0 + r.h * CH - 1;
  const check = (tx, ty, nx, ny, side) => {
    if (!open(tx, ty)) return;
    const other = world.roomGrid[ny * world.tw + nx];
    if (nx < 0 || ny < 0 || nx >= world.tw || ny >= world.th || !other) {
      err(`${r.id}: ${side} 边 (${tx},${ty}) 开口通向世界外`);
    } else if (!open(nx, ny)) {
      err(`${r.id}: ${side} 边 (${tx},${ty}) 开口对着 ${other.id} 的墙`);
    }
  };
  for (let ty = y0; ty <= y1; ty++) {
    check(x0, ty, x0 - 1, ty, '左');
    check(x1, ty, x1 + 1, ty, '右');
  }
  for (let tx = x0; tx <= x1; tx++) {
    check(tx, y0, tx, y0 - 1, '上');
    check(tx, y1, tx, y1 + 1, '下');
  }
}

if (!world.spawns.some(s => s.type === 'P')) err('没有出生点 P');

// ---------- 可达性：按跳跃能力在「可站立格」上做 BFS ----------
// 跳高约 4.3 格；上升 4 格时水平最多 3 列，上升 ≤2 格或平跳最多 5 列；冲刺额外 +4 列
const standable = (x, y) => !world.solid(x, y) && !world.spike(x, y) && !world.solid(x, y - 1) &&
  (world.solid(x, y + 1) || world.oneWay(x, y + 1));
const fallFrom = (x, y) => { // 从 (x,y) 往下掉，落到哪个可站立格（掉进尖刺返回 null）
  for (let yy = y; yy < world.th; yy++) {
    if (world.solid(x, yy)) return null;
    if (world.spike(x, yy)) return null;
    if (standable(x, yy)) return [x, yy];
  }
  return null;
};
function reach(dash) {
  const start = world.spawns.find(s => s.type === 'P');
  const seen = new Set();
  const q = [[start.tx, start.ty]];
  const key = (x, y) => x + ',' + y;
  seen.add(key(...q[0]));
  const push = (p) => { if (p && !seen.has(key(...p))) { seen.add(key(...p)); q.push(p); } };
  while (q.length) {
    const [x, y] = q.shift();
    // 走 / 走下边缘
    for (const d of [-1, 1]) if (!world.solid(x + d, y) && !world.solid(x + d, y - 1)) push(fallFrom(x + d, y));
    // 穿下单向平台
    if (world.oneWay(x, y + 1)) push(fallFrom(x, y + 2));
    // 跳：目标格上升 dy (≤4)，水平 dx
    for (let dy = -4; dy <= 8; dy++) {
      const up = -dy;
      const maxDx = (up >= 4 ? 3 : up >= 3 ? 4 : 5) + (dash ? 4 : 0);
      for (let dx = -maxDx; dx <= maxDx; dx++) {
        const tx = x + dx, ty = y + dy;
        if (!standable(tx, ty)) continue;
        // 粗略检查：起跳列向上、目标列向下的通道不被实心挡住
        let clear = true;
        const top = Math.min(y, ty);
        for (let yy = y - 1; yy >= top; yy--) if (world.solid(x, yy)) clear = false;
        for (let yy = top - 1; yy <= ty; yy++) if (world.solid(tx, yy)) clear = false;
        const sx = Math.sign(dx);
        for (let xx = x; xx !== tx; xx += sx) if (world.solid(xx, top)) clear = false;
        if (clear) push([tx, ty]);
      }
    }
  }
  return seen;
}
// 站在该格，或站在它正下方一格（浮空的道具跳一下就能吃到）
const can = (set, sp) => set.has(sp.tx + ',' + sp.ty) || set.has(sp.tx + ',' + (sp.ty + 1));
const noDash = reach(false), withDash = reach(true);
if (process.env.DEBUG_REACH) for (let y = 0; y < world.th; y++) { let r = ""; for (let x = 0; x < world.tw; x++) r += withDash.has(x + "," + y) ? "o" : world.tile(x, y); console.log(String(y).padStart(2), r); }
const dashOrb = world.spawns.find(s => s.type === 'D');
if (dashOrb && !can(noDash, dashOrb)) err(`不冲刺够不到疾影 ${dashOrb.id}`);
for (const sp of world.spawns) {
  if (!'BDSKH'.includes(sp.type)) continue;
  if (!can(withDash, sp)) err(`够不到 ${sp.id}（${world.roomById[sp.room].name}）`);
}
const vortexOrb = world.spawns.find(s => s.type === 'S');
if (vortexOrb && can(noDash, vortexOrb)) err('不冲刺也能拿到墨旋，刺渊的门槛没起作用');

const counts = {};
for (const s of world.spawns) counts[s.type] = (counts[s.type] || 0) + 1;
console.log(`房间 ${ROOMS.length} 个，世界 ${world.tw}×${world.th} 格，实体`, counts);
if (errors) { console.error(`共 ${errors} 处错误`); process.exit(1); }
console.log('✓ 世界校验通过');
