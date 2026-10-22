// 指令执行：把「去那里」「打它」变成每帧的操控（水平速度 / 起跳 / 冲刺 / 穿平台 / 出招）
// 执行方式和 nav.sim 完全一致，所以导航图里的每一跳都能复现。
import { T } from './world.js';
import { steerVx, cellCenter } from './nav.js';
import { overlap } from './physics.js';

const REPLAN_ENGAGE = 0.35;

export class Pilot {
  constructor(g) {
    this.g = g;
    this.cmd = null;
    this.edges = [];
    this.idx = 0;
    this.phase = 'ground';
    this.manualVx = null;   // 斜上划跳起后，空中保持的水平速度
  }

  cellOf(p) { return [Math.floor(p.cx / T), Math.floor((p.y + p.h - 1) / T)]; }

  bounds() {
    const r = this.g.room;
    const x0 = r.px / T, y0 = r.py / T;
    return { x0: x0 - 2, y0: y0 - 2, x1: x0 + r.pw / T + 1, y1: y0 + r.ph / T + 1 };
  }

  cancel() { this.cmd = null; this.edges = []; this.idx = 0; this.phase = 'ground'; }

  // ---------- 指令 ----------
  goto(wx, wy, then = null) {
    const nav = this.g.nav, b = this.bounds();
    let cell = nav.nearestCell(wx, wy, b);
    if (!cell) return false;
    cell = this.throughDoor(cell);
    this.manualVx = null;
    this.cmd = { kind: 'goto', goal: cell, goalX: cell[0] === Math.floor(wx / T) ? wx : cellCenter(cell[0]), then, tries: 0 };
    this.replan();
    return true;
  }

  // 目标在房间边缘的门口：再往外走两格，直接进入下一个房间
  throughDoor([x, y]) {
    const r = this.g.room, nav = this.g.nav;
    const x0 = r.px / T, x1 = x0 + r.pw / T - 1;
    for (const [edge, d] of [[x0, -1], [x1, 1]]) {
      if (x === edge && nav.standable(x + d, y) && nav.standable(x + d * 2, y)) return [x + d * 2, y];
    }
    return [x, y];
  }

  engage(target, heavy = false) {
    this.manualVx = null;
    this.cmd = { kind: 'engage', target, heavy, replanT: 0, tries: 0, swings: 0 };
    this.edges = []; this.idx = 0; this.phase = 'ground';
  }

  // ---------- 每帧 ----------
  update(dt) {
    const g = this.g, p = g.player;
    p.ctrlVx = 0;

    if (this.manualVx !== null) {
      if (p.grounded && p.vy >= 0 && this.manualT > 0.05) this.manualVx = null;
      else { this.manualT += dt; p.ctrlVx = this.manualVx; return; }
    }
    if (!this.cmd || p.frozen > 0) return;
    if (this.cmd.kind === 'engage') this.engageTick(dt);
    else this.follow(dt, this.cmd.goalX, () => this.finish());
  }

  finish() {
    const then = this.cmd?.then;
    this.cancel();
    then?.();
  }

  replan() {
    const p = this.g.player;
    if (!p.grounded) { this.edges = []; this.needPlan = true; return; }
    this.needPlan = false;
    const [x, y] = this.cellOf(p);
    const [gx, gy] = this.cmd.goal;
    const res = this.g.nav.path(x, y, gx, gy, this.bounds());
    this.edges = res.edges; this.idx = 0; this.phase = 'ground';
    this.stuckT = 0;
    if (!res.reached) {
      // 到不了就去最近的地方，最后一段只走到那一格
      this.cmd.goal = res.end;
      this.cmd.goalX = cellCenter(res.end[0]);
      if (this.cmd.kind === 'goto' && !this.cmd.warned) { this.cmd.warned = true; this.g.toastMsg('够不到那里'); }
    }
  }

  // 沿边执行；走完后走到 finalX（容差内）就调用 done
  follow(dt, finalX, done, tol = 1.5) {
    const g = this.g, p = g.player;
    if (this.needPlan) { if (p.grounded) this.replan(); else return; }
    this.stuckT = (this.stuckT || 0) + dt;
    if (this.stuckT > 2.5) {
      if (++this.cmd.tries > 3) { this.cancel(); return; }
      this.replan();
    }
    const e = this.edges[this.idx];

    if (this.phase === 'air') {
      this.airT += dt;
      if (!p.grounded) this.leftGround = true;
      if (!this.leftGround && this.airT > 0.6) { this.replan(); return; }
      p.ctrlVx = steerVx(cellCenter(e.steer) - p.cx);
      if (e.type === 'dash' && !this.dashed && !p.grounded && p.vy >= 0) {
        this.dashed = true;
        p.dash(g, Math.sign(cellCenter(e.steer) - p.cx) || p.facing);
      }
      if (p.grounded && this.leftGround) {
        const [cx, cy] = this.cellOf(p);
        if (cx === e.x && cy === e.y) { this.idx++; this.phase = 'ground'; this.stuckT = 0; }
        else this.replan();
      }
      return;
    }

    if (!p.grounded) return; // 被击飞等，落地再说
    if (!e) {
      const dx = finalX - p.cx;
      if (Math.abs(dx) <= tol) { p.ctrlVx = 0; done(); }
      else p.ctrlVx = steerVx(dx);
      return;
    }
    const [cx, cy] = this.cellOf(p);
    if (cy !== e.fy && !(cx === e.x && cy === e.y)) { this.replan(); return; }

    if (e.type === 'walk') {
      if (cx === e.x) { this.idx++; this.stuckT = 0; return this.follow(0, finalX, done, tol); }
      p.ctrlVx = steerVx(cellCenter(e.x) - p.cx) || Math.sign(e.x - cx) * 40;
      // 走的时候不减速：目标格在前方就全速
      p.ctrlVx = Math.sign(p.ctrlVx) * Math.max(Math.abs(p.ctrlVx), 60);
      return;
    }
    if (e.type === 'fall') {
      p.ctrlVx = steerVx(cellCenter(e.steer) - p.cx);
      if (!p.grounded || Math.floor(p.cx / T) === e.steer) this.enterAir();
      return;
    }
    // jump / dash / drop：先站到起跳格中心并停稳（和模拟的起始状态一致）
    const dx = cellCenter(e.fx) - p.cx;
    if (cx !== e.fx || Math.abs(dx) > 1.5 || Math.abs(p.vx) > 25) { p.ctrlVx = steerVx(dx); return; }
    if (e.type === 'drop') { p.dropT = 0.18; p.y += 1; }
    else p.jump();
    this.enterAir();
    p.ctrlVx = steerVx(cellCenter(e.steer) - p.cx);
  }

  enterAir() { this.phase = 'air'; this.airT = 0; this.dashed = false; this.leftGround = false; }

  // ---------- 交战 ----------
  engageTick(dt) {
    const g = this.g, p = g.player, c = this.cmd, t = c.target;
    if (t.kind === 'wall') return this.wallTick(dt);
    if (t.dead || !g.enemies.includes(t)) { this.cancel(); return; }

    // 能砍到就砍（自动选方向）
    if (!p.atk && p.atkCd <= 0 && p.dashT <= 0) {
      const dir = this.attackDir(t, c.heavy);
      if (dir) { p.attack(g, dir, c.heavy); c.heavy = false; return; }
    }
    const dx = t.cx - p.cx, dy = t.cy - p.cy;
    // 正上方的飞行敌人：跳起来上挑
    if (p.grounded && Math.abs(dx) < 18 && dy < -14 && dy > -78) { p.jump(); p.ctrlVx = steerVx(dx); return; }

    if (!p.grounded && this.phase !== 'air') { p.ctrlVx = steerVx(dx); return; }

    c.replanT -= dt;
    const [pcx, pcy] = this.cellOf(p);
    const goal = g.nav.nearestCell(t.cx, t.y + t.h, this.bounds());
    if (!goal) { this.cancel(); return; }
    // 已经在同一层：直接贴上去，保持一点距离
    if (goal[1] === pcy && g.nav.walkable(pcx, goal[0], pcy) && this.phase !== 'air') {
      this.edges = []; this.idx = 0;
      const stand = t.cx - Math.sign(dx || p.facing) * 12;
      p.ctrlVx = Math.abs(dx) > 14 ? steerVx(stand - p.cx) : 0;
      if (Math.abs(dx) <= 14) p.facing = Math.sign(dx) || p.facing;
      return;
    }
    if (c.replanT <= 0 && this.phase !== 'air') {
      c.replanT = REPLAN_ENGAGE;
      c.goal = goal; c.goalX = cellCenter(goal[0]);
      this.replan();
    }
    this.follow(dt, c.goalX, () => {}, 6);
  }

  attackDir(t, heavy) {
    const p = this.g.player;
    const side = t.cx >= p.cx ? 'right' : 'left';
    const dirs = p.grounded ? [side, 'up'] : ['down', side, 'up'];
    for (const d of dirs) if (overlap(p.hitbox(d, heavy), t)) return d;
    return null;
  }

  // 裂墙：走到墙边，重击（普通攻击只敲一下提示）
  wallTick(dt) {
    const g = this.g, p = g.player, c = this.cmd, t = c.target;
    const tiles = t.tiles.filter(([x, y]) => g.world.tile(x, y) === 'X');
    if (!tiles.length) { this.cancel(); return; }
    if (!c.spot) {
      const b = this.bounds();
      let best = null, bd = Infinity;
      for (const [x, y] of tiles) for (const d of [-1, 1]) {
        for (const yy of [y, y + 1, y - 1]) {
          if (!g.nav.standable(x + d, yy)) continue;
          const dist = Math.hypot(x + d - p.cx / T, yy - p.cy / T);
          if (dist < bd) { bd = dist; best = { cell: [x + d, yy], dir: -d }; }
        }
      }
      if (!best) { this.cancel(); return; }
      c.spot = best;
      c.goal = best.cell;
      c.goalX = cellCenter(best.cell[0]) + best.dir * 3;
      this.replan();
    }
    this.follow(dt, c.goalX, () => {
      if (p.atk || p.atkCd > 0) return;
      const dir = c.spot.dir > 0 ? 'right' : 'left';
      p.attack(g, dir, c.heavy);
      this.cancel();
    }, 2);
  }
}
