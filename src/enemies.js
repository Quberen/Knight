// 敌人：爬虫 c、墨蝠 b、跳跳 h
import { T } from './world.js';
import { moveBody, overlap } from './physics.js';
import { INK, PAPER } from './fx.js';

class Enemy {
  constructor(sp, w, h, hp) {
    this.spawn = sp;
    this.w = w; this.h = h;
    this.x = sp.x - w / 2; this.y = sp.y - h;
    this.vx = 0; this.vy = 0;
    this.hp = hp; this.flash = 0; this.dead = false;
    this.kb = 0; this.t = Math.random() * 10;
    this.facing = Math.random() < 0.5 ? -1 : 1;
  }
  get cx() { return this.x + this.w / 2; }
  get cy() { return this.y + this.h / 2; }

  damage(g, dmg, kx, ky, heavy) {
    this.hp -= dmg;
    this.flash = 0.12;
    this.vx = kx * (heavy ? 200 : 130);
    if (ky) this.vy = ky * 120;
    this.kb = 0.18;
    const dir = Math.atan2(ky, kx || 0.0001);
    g.fx.ink(this.cx, this.cy, heavy ? 14 : 8, heavy ? 180 : 130, { dir, spread: 1.6 });
    if (this.hp <= 0) this.die(g);
  }

  die(g) {
    this.dead = true;
    g.fx.ink(this.cx, this.cy, 22, 200);
    g.fx.ring(this.cx, this.cy, 2, 20, 0.3, 3);
    g.fx.shake(2.5, 0.15);
    g.hitstop(0.06);
  }

  common(dt, g) {
    this.t += dt;
    this.flash = Math.max(0, this.flash - dt);
    this.kb = Math.max(0, this.kb - dt);
    const p = g.player;
    if (!g.dead && overlap(this, p)) p.hurt(g, this.cx);
  }

  eyes(ctx, x, y, f, white = true) {
    ctx.fillStyle = white ? PAPER : INK;
    ctx.fillRect(x + f * 1 - 0.5, y, 1.5, 1.5);
    ctx.fillRect(x + f * 3.5 - 0.5, y, 1.5, 1.5);
  }
}

export class Crawler extends Enemy {
  constructor(sp) { super(sp, 12, 8, 3); this.speed = 28; }
  update(dt, g) {
    this.common(dt, g);
    if (this.kb <= 0) {
      this.vx = this.facing * this.speed;
      // 前方没有地面就掉头
      const ahead = this.facing > 0 ? this.x + this.w + 1 : this.x - 1;
      const tyBelow = Math.floor((this.y + this.h + 2) / T);
      const tx = Math.floor(ahead / T);
      const w = g.world;
      if (!w.solid(tx, tyBelow) && !w.oneWay(tx, tyBelow) || w.spike(tx, tyBelow - 1)) this.facing *= -1;
    } else this.vx *= 0.9;
    this.vy = Math.min(400, this.vy + 1100 * dt);
    const r = moveBody(this, g.world, dt);
    if (r.wallL) this.facing = 1;
    if (r.wallR) this.facing = -1;
  }
  draw(ctx) {
    const x = Math.round(this.x), y = Math.round(this.y), f = this.facing;
    ctx.fillStyle = this.flash > 0 ? PAPER : INK;
    ctx.strokeStyle = INK; ctx.lineWidth = 1;
    // 壳
    ctx.beginPath();
    ctx.moveTo(x, y + this.h - 1);
    ctx.quadraticCurveTo(x + this.w / 2, y - 5, x + this.w, y + this.h - 1);
    ctx.closePath();
    ctx.fill(); if (this.flash > 0) ctx.stroke();
    // 壳纹
    ctx.strokeStyle = this.flash > 0 ? INK : PAPER;
    ctx.beginPath();
    ctx.moveTo(x + this.w / 2 - 2, y + 2); ctx.lineTo(x + this.w / 2 - 3, y + this.h - 2);
    ctx.moveTo(x + this.w / 2 + 2, y + 2); ctx.lineTo(x + this.w / 2 + 3, y + this.h - 2);
    ctx.stroke();
    // 腿
    ctx.fillStyle = INK;
    const l = Math.sin(this.t * 18) > 0 ? 1 : 0;
    for (let i = 0; i < 3; i++) ctx.fillRect(x + 2 + i * 3.5 + l, y + this.h - 1, 1, 1.5);
    // 眼
    this.eyes(ctx, f > 0 ? x + this.w - 6 : x + 1, y + this.h - 5, 1, this.flash <= 0);
  }
}

export class Bat extends Enemy {
  constructor(sp) {
    super(sp, 10, 8, 2);
    this.y -= 4;
    this.homeX = this.x; this.homeY = this.y;
    this.awake = false;
  }
  update(dt, g) {
    this.common(dt, g);
    const p = g.player;
    const dx = p.cx - this.cx, dy = p.cy - this.cy;
    const d = Math.hypot(dx, dy);
    if (!this.awake && d < 90) this.awake = true;
    if (this.kb > 0) {
      this.vx *= 0.9; this.vy *= 0.9;
    } else if (this.awake) {
      const sp = 52;
      const tx = dx / (d || 1) * sp, ty = dy / (d || 1) * sp + Math.sin(this.t * 5) * 40;
      this.vx += (tx - this.vx) * Math.min(1, dt * 2.5);
      this.vy += (ty - this.vy) * Math.min(1, dt * 2.5);
      this.facing = dx > 0 ? 1 : -1;
    } else {
      this.vx = 0; this.vy = Math.sin(this.t * 2) * 6;
    }
    moveBody(this, g.world, dt);
  }
  draw(ctx) {
    const cx = Math.round(this.cx), cy = Math.round(this.cy);
    const flap = this.awake ? Math.sin(this.t * 22) : Math.sin(this.t * 3) * 0.3 - 0.6;
    ctx.fillStyle = this.flash > 0 ? PAPER : INK;
    ctx.strokeStyle = INK; ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.ellipse(cx, cy, 4, 3.5, 0, 0, Math.PI * 2);
    ctx.fill(); if (this.flash > 0) ctx.stroke();
    ctx.fillStyle = INK;
    for (const s of [-1, 1]) {
      ctx.beginPath();
      ctx.moveTo(cx + s * 3, cy - 1);
      ctx.lineTo(cx + s * 10, cy - 1 - flap * 5);
      ctx.lineTo(cx + s * 8, cy + 2 - flap * 2);
      ctx.lineTo(cx + s * 5, cy + 1);
      ctx.closePath();
      ctx.fill();
    }
    // 耳
    ctx.beginPath();
    ctx.moveTo(cx - 3, cy - 2); ctx.lineTo(cx - 2, cy - 6); ctx.lineTo(cx - 0.5, cy - 3);
    ctx.moveTo(cx + 3, cy - 2); ctx.lineTo(cx + 2, cy - 6); ctx.lineTo(cx + 0.5, cy - 3);
    ctx.fill();
    this.eyes(ctx, cx - 2.2 + this.facing * 0.5, cy - 1, 1, this.flash <= 0);
  }
}

export class Hopper extends Enemy {
  constructor(sp) { super(sp, 12, 11, 4); this.cool = 1 + Math.random(); this.grounded = false; this.sq = 0; }
  update(dt, g) {
    this.common(dt, g);
    const p = g.player;
    this.sq = Math.max(0, this.sq - dt * 4);
    if (this.grounded && this.kb <= 0) {
      this.vx *= 0.8;
      this.cool -= dt;
      if (this.cool <= 0 && Math.abs(p.cx - this.cx) < 130 && Math.abs(p.cy - this.cy) < 80) {
        this.facing = p.cx > this.cx ? 1 : -1;
        this.vx = this.facing * (50 + Math.random() * 30);
        this.vy = -300;
        this.cool = 1.1 + Math.random() * 0.6;
      }
    }
    this.vy = Math.min(400, this.vy + 1000 * dt);
    const was = this.grounded;
    const r = moveBody(this, g.world, dt);
    this.grounded = r.ground;
    if (this.grounded && !was) { this.sq = 1; g.fx.dust(this.cx, this.y + this.h, 3); }
    if (r.wallL || r.wallR) this.vx = -this.vx * 0.5;
  }
  draw(ctx) {
    const cx = Math.round(this.cx), by = Math.round(this.y + this.h);
    let sx = 1, sy = 1;
    if (this.sq > 0) { sx = 1 + this.sq * 0.3; sy = 1 - this.sq * 0.3; }
    else if (!this.grounded) { sx = 0.85; sy = 1.15; }
    else if (this.cool < 0.25) { sx = 1.15; sy = 0.85; }
    ctx.save();
    ctx.translate(cx, by); ctx.scale(sx, sy);
    ctx.fillStyle = this.flash > 0 ? PAPER : INK;
    ctx.strokeStyle = INK; ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.ellipse(0, -5.5, 6, 5.5, 0, 0, Math.PI * 2);
    ctx.fill(); if (this.flash > 0) ctx.stroke();
    // 头顶的墨滴
    ctx.fillStyle = INK;
    ctx.beginPath(); ctx.moveTo(-1, -10); ctx.quadraticCurveTo(0, -15, 2, -10); ctx.fill();
    // 单眼
    ctx.fillStyle = this.flash > 0 ? INK : PAPER;
    ctx.beginPath(); ctx.arc(this.facing * 2, -6, 1.8, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = this.flash > 0 ? PAPER : INK;
    ctx.fillRect(this.facing * 2 + this.facing * 0.5 - 0.5, -6.5, 1, 1);
    ctx.restore();
  }
}

export function makeEnemy(sp) {
  switch (sp.type) {
    case 'c': return new Crawler(sp);
    case 'b': return new Bat(sp);
    case 'h': return new Hopper(sp);
  }
  return null;
}
