// 导航：把世界看成「可站立格」组成的图，边用真实物理模拟验证过，
// 所以寻路给出的每一跳，骑士按同样的操控方式执行一定跳得过去。
import { T } from './world.js';
import { moveBody } from './physics.js';
import { PLAYER_CFG as P } from './player.js';

const DT = 1 / 60;
const PW = 10, PH = 14;

// 导航和模拟共用的水平操控：离目标越近越慢，避免冲过头
export function steerVx(dx) {
  return Math.max(-P.run, Math.min(P.run, dx * 10));
}

export function approach(v, t, d) {
  return v < t ? Math.min(t, v + d) : Math.max(t, v - d);
}

export const cellCenter = (x) => x * T + T / 2;

export class Nav {
  constructor(world, abilities) {
    this.world = world;
    this.ab = abilities;
    this.cache = new Map();
  }

  invalidate() { this.cache.clear(); }

  standable(x, y) {
    const w = this.world;
    return !w.solid(x, y) && !w.spike(x, y) && !w.oneWay(x, y) && (w.solid(x, y + 1) || w.oneWay(x, y + 1));
  }

  // 用玩家的物理参数模拟一次移动，返回落地格（掉进尖刺或超时返回 null）
  // mode: walk（走下边缘）/ drop（穿下平台）/ jump / dash（跳起后在最高点冲刺）
  sim(sx, sy, steerX, mode) {
    const w = this.world;
    const b = { x: cellCenter(sx) - PW / 2, y: (sy + 1) * T - PH, w: PW, h: PH, vx: 0, vy: 0 };
    const goal = cellCenter(steerX);
    let air = false, dashT = 0, dashed = false, dir = 1;
    for (let i = 0; i < 110; i++) {
      const dx = goal - (b.x + PW / 2);
      if (mode === 'dash' && !dashed && air && b.vy >= 0) {
        dashed = true; dashT = P.dashT; dir = Math.sign(dx) || 1;
        b.vx = dir * P.dashV; b.vy = 0;
      }
      if (dashT > 0) {
        dashT -= DT; b.vy = 0;
        if (dashT <= 0) b.vx = dir * P.run;
      } else {
        b.vx = approach(b.vx, steerVx(dx), (air ? P.accelAir : P.accelGround) * DT);
        b.vy = Math.min(P.maxFall, b.vy + P.grav * (b.vy > 0 ? P.fallMult : 1) * DT);
      }
      if (i === 0 && (mode === 'jump' || mode === 'dash')) { b.vy = -P.jumpV; air = true; }
      const r = moveBody(b, w, DT, { drop: mode === 'drop' && i < 11 });
      if (r.spike) return null;
      if (!r.ground) air = true;
      else if (air) return [Math.floor((b.x + PW / 2) / T), Math.floor((b.y + PH - 1) / T)];
      if (mode === 'walk' && !air && i > 40) return null;
    }
    return null;
  }

  // 某格出发的所有边（懒计算 + 缓存）
  edges(x, y) {
    const key = x + ',' + y;
    const hit = this.cache.get(key);
    if (hit) return hit;
    const w = this.world, out = [];
    const add = (tx, ty, type, steer, cost) => out.push({ x: tx, y: ty, type, steer, cost, fx: x, fy: y });

    for (const d of [-1, 1]) {
      if (w.solid(x + d, y)) continue;
      if (this.standable(x + d, y)) add(x + d, y, 'walk', x + d, 1);
      else {
        const r = this.sim(x, y, x + d, 'walk');
        if (r) add(r[0], r[1], 'fall', x + d, 1.5 + Math.abs(r[1] - y) * 0.2);
      }
    }
    if (w.oneWay(x, y + 1)) {
      const r = this.sim(x, y, x, 'drop');
      if (r) add(r[0], r[1], 'drop', x, 2);
    }

    const reach = this.ab.dash ? 9 : 5;
    for (let ty = y - 4; ty <= y + 8; ty++) {
      for (let tx = x - reach; tx <= x + reach; tx++) {
        if ((tx === x && ty === y) || !this.standable(tx, ty)) continue;
        if (ty === y && this.walkable(x, tx, y)) continue; // 能走过去就不跳
        const up = y - ty, adx = Math.abs(tx - x);
        const maxDx = up >= 4 ? 3 : up >= 3 ? 4 : 5;
        let ok = false;
        if (adx <= maxDx) {
          const r = this.sim(x, y, tx, 'jump');
          if (r && r[0] === tx && r[1] === ty) { add(tx, ty, 'jump', tx, 3 + adx * 0.3); ok = true; }
        }
        if (!ok && this.ab.dash && adx >= 3 && adx <= maxDx + 4) {
          const r = this.sim(x, y, tx, 'dash');
          if (r && r[0] === tx && r[1] === ty) add(tx, ty, 'dash', tx, 6 + adx * 0.3);
        }
      }
    }
    this.cache.set(key, out);
    return out;
  }

  walkable(x0, x1, y) {
    const s = Math.sign(x1 - x0);
    for (let x = x0; x !== x1; x += s) if (!this.standable(x + s, y)) return false;
    return true;
  }

  // 点在世界坐标上 → 最合适的落脚格（优先正下方的地面）
  nearestCell(wx, wy, bounds) {
    let tx = Math.floor(wx / T), ty = Math.floor(wy / T);
    for (let k = 0; k < 4 && this.world.solid(tx, ty); k++) ty--;
    if (!this.world.solid(tx, ty)) {
      for (let y = ty; y < ty + 12 && !this.world.solid(tx, y); y++) {
        if (this.standable(tx, y) && inBounds(bounds, tx, y)) return [tx, y];
      }
    }
    let best = null, bd = Infinity;
    for (let y = ty - 4; y <= ty + 6; y++) for (let x = tx - 5; x <= tx + 5; x++) {
      if (!inBounds(bounds, x, y) || !this.standable(x, y)) continue;
      const d = Math.hypot(x - tx, (y - ty) * (y < ty ? 1.5 : 1));
      if (d < bd) { bd = d; best = [x, y]; }
    }
    return best;
  }

  // Dijkstra；到不了目标就去离目标最近的可达格。返回边列表
  path(sx, sy, gx, gy, bounds, maxNodes = 2500) {
    const key = (x, y) => x + ',' + y;
    const dist = new Map([[key(sx, sy), 0]]);
    const prev = new Map();
    const heap = new Heap();
    heap.push(0, [sx, sy]);
    let best = [sx, sy], bestH = Math.hypot(sx - gx, sy - gy), n = 0;
    while (heap.size && n++ < maxNodes) {
      const [d, [x, y]] = heap.pop();
      if (d > dist.get(key(x, y))) continue;
      const h = Math.hypot(x - gx, y - gy);
      if (h < bestH) { bestH = h; best = [x, y]; }
      if (x === gx && y === gy) break;
      for (const e of this.edges(x, y)) {
        if (!inBounds(bounds, e.x, e.y)) continue;
        const nd = d + e.cost, k = key(e.x, e.y);
        if (nd < (dist.get(k) ?? Infinity)) { dist.set(k, nd); prev.set(k, e); heap.push(nd, [e.x, e.y]); }
      }
    }
    const out = [];
    let k = key(...best);
    while (prev.has(k)) { const e = prev.get(k); out.unshift(e); k = key(e.fx, e.fy); }
    return { edges: out, reached: best[0] === gx && best[1] === gy, end: best };
  }

  // 从起点能到的所有格（给地图校验用）
  reachable(sx, sy, bounds) {
    const seen = new Set([sx + ',' + sy]);
    const q = [[sx, sy]];
    while (q.length) {
      const [x, y] = q.pop();
      for (const e of this.edges(x, y)) {
        const k = e.x + ',' + e.y;
        if (!seen.has(k) && inBounds(bounds, e.x, e.y)) { seen.add(k); q.push([e.x, e.y]); }
      }
    }
    return seen;
  }
}

function inBounds(b, x, y) {
  return !b || (x >= b.x0 && x <= b.x1 && y >= b.y0 && y <= b.y1);
}

class Heap {
  constructor() { this.a = []; }
  get size() { return this.a.length; }
  push(p, v) {
    const a = this.a; a.push([p, v]);
    let i = a.length - 1;
    while (i > 0) { const j = (i - 1) >> 1; if (a[j][0] <= a[i][0]) break; [a[i], a[j]] = [a[j], a[i]]; i = j; }
  }
  pop() {
    const a = this.a, top = a[0], last = a.pop();
    if (a.length) {
      a[0] = last;
      let i = 0;
      for (;;) {
        const l = i * 2 + 1, r = l + 1;
        let m = i;
        if (l < a.length && a[l][0] < a[m][0]) m = l;
        if (r < a.length && a[r][0] < a[m][0]) m = r;
        if (m === i) break;
        [a[i], a[m]] = [a[m], a[i]]; i = m;
      }
    }
    return top;
  }
}
