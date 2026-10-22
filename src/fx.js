// 特效：墨点、斩击弧、冲击环、震屏、反色闪
import { T } from './world.js';

export const INK = '#111';
export const PAPER = '#e9e5db';

export class FX {
  constructor() {
    this.parts = [];
    this.slashes = [];
    this.rings = [];
    this.shakeT = 0; this.shakeMag = 0;
    this.flashT = 0;
  }

  shake(mag, t = 0.18) { this.shakeMag = Math.max(this.shakeMag, mag); this.shakeT = Math.max(this.shakeT, t); }
  flash(t = 0.06) { this.flashT = Math.max(this.flashT, t); }

  ink(x, y, n = 8, speed = 120, opts = {}) {
    for (let i = 0; i < n; i++) {
      const a = opts.dir !== undefined ? opts.dir + (Math.random() - 0.5) * (opts.spread ?? 1.4) : Math.random() * Math.PI * 2;
      const s = speed * (0.35 + Math.random() * 0.8);
      this.parts.push({
        x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s - (opts.up ?? 30),
        r: (opts.size ?? 1.6) * (0.5 + Math.random()), life: 0.4 + Math.random() * 0.5, t: 0,
        g: opts.gravity ?? 500, white: !!opts.white, stick: opts.stick ?? true,
      });
    }
  }

  dust(x, y, n = 4) {
    for (let i = 0; i < n; i++) {
      this.parts.push({
        x: x + (Math.random() - 0.5) * 8, y, vx: (Math.random() - 0.5) * 60, vy: -Math.random() * 30,
        r: 1 + Math.random() * 1.5, life: 0.35, t: 0, g: -20, gray: true,
      });
    }
  }

  slash(x, y, dir, big = false) {
    this.slashes.push({ x, y, dir, big, t: 0, life: big ? 0.2 : 0.14 });
  }

  ring(x, y, r0, r1, life = 0.3, width = 2) {
    this.rings.push({ x, y, r0, r1, life, t: 0, width });
  }

  update(dt, world) {
    if (this.shakeT > 0) { this.shakeT -= dt; if (this.shakeT <= 0) this.shakeMag = 0; }
    if (this.flashT > 0) this.flashT -= dt;
    for (const p of this.parts) {
      p.t += dt;
      if (p.stuck) continue;
      p.vy += p.g * dt;
      p.x += p.vx * dt; p.y += p.vy * dt;
      if (p.stick && world.solid(Math.floor(p.x / T), Math.floor(p.y / T))) {
        p.stuck = true; p.life = Math.max(p.life, p.t + 1.2);
      }
    }
    this.parts = this.parts.filter(p => p.t < p.life);
    for (const s of this.slashes) s.t += dt;
    this.slashes = this.slashes.filter(s => s.t < s.life);
    for (const r of this.rings) r.t += dt;
    this.rings = this.rings.filter(r => r.t < r.life);
  }

  shakeOffset() {
    if (this.shakeT <= 0) return [0, 0];
    const m = this.shakeMag * Math.min(1, this.shakeT / 0.1);
    return [(Math.random() - 0.5) * 2 * m, (Math.random() - 0.5) * 2 * m];
  }

  draw(ctx) {
    for (const p of this.parts) {
      const k = 1 - p.t / p.life;
      ctx.fillStyle = p.white ? PAPER : p.gray ? `rgba(17,17,17,${0.35 * k})` : INK;
      ctx.beginPath();
      ctx.arc(p.x, p.y, Math.max(0.4, p.r * (p.stuck ? 1 : 0.6 + 0.4 * k)), 0, Math.PI * 2);
      ctx.fill();
    }
    for (const s of this.slashes) drawSlash(ctx, s);
    for (const r of this.rings) {
      const k = r.t / r.life;
      ctx.strokeStyle = `rgba(17,17,17,${1 - k})`;
      ctx.lineWidth = r.width * (1 - k) + 0.5;
      ctx.beginPath();
      ctx.arc(r.x, r.y, r.r0 + (r.r1 - r.r0) * easeOut(k), 0, Math.PI * 2);
      ctx.stroke();
    }
  }
}

const DIR_ANGLE = { right: 0, down: Math.PI / 2, left: Math.PI, up: -Math.PI / 2 };

// 新月形斩击：外弧 + 内弧围成的墨色月牙
function drawSlash(ctx, s) {
  const k = s.t / s.life;
  const a = DIR_ANGLE[s.dir];
  const R = s.big ? 22 : 16;
  const sweep = s.big ? 2.4 : 2.0;
  const a0 = a - sweep / 2, a1 = a + sweep / 2;
  const grow = easeOut(Math.min(1, k * 2.2));
  const cut = Math.max(0, (k - 0.4) / 0.6);
  ctx.save();
  ctx.translate(s.x, s.y);
  ctx.fillStyle = INK;
  ctx.globalAlpha = 1 - cut * 0.9;
  const start = a0 + (a1 - a0) * cut, end = a0 + (a1 - a0) * grow;
  if (end > start) {
    ctx.beginPath();
    ctx.arc(0, 0, R, start, end);
    ctx.arc(-R * 0.3 * Math.cos(a), -R * 0.3 * Math.sin(a), R * 0.92, end, start, true);
    ctx.closePath();
    ctx.fill();
  }
  ctx.restore();
}

export const easeOut = k => 1 - (1 - k) * (1 - k);
