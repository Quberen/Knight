// 主角：移动、跳跃、冲刺、挥砍、重击、下砸、凝神、技能
import { T } from './world.js';
import { moveBody, groundBelow, overlap } from './physics.js';
import { INK, PAPER } from './fx.js';

const P = {
  run: 100, accelGround: 1500, accelAir: 1000,
  grav: 1150, fallMult: 1.35, maxFall: 430,
  jumpV: 400, coyote: 0.09, buffer: 0.13,
  dashV: 350, dashT: 0.18, dashCd: 0.32,
  plungeV: 560,
  atkT: 0.2, atkCd: 0.27, atkActive: 0.1,
  heavyT: 0.3, heavyActive: 0.12,
  pogoV: 330,
  inkHit: 11, inkMax: 99, skillCost: 33,
  focusT: 0.9,
  invuln: 1.1,
};

export class Player {
  constructor(x, y) {
    this.w = 10; this.h = 14;
    this.maxHp = 5; this.hp = 5; this.ink = 0;
    this.reset(x, y);
  }

  reset(x, y) {
    this.x = x - this.w / 2; this.y = y - this.h;
    this.vx = 0; this.vy = 0;
    this.facing = 1;
    this.grounded = false; this.coyote = 0; this.jumpBuf = 0;
    this.dashT = 0; this.dashCd = 0; this.airDash = true;
    this.atk = null; this.atkCd = 0;
    this.plunging = false; this.dropT = 0;
    this.invuln = 0; this.stun = 0;
    this.focusT = 0; this.focusing = false;
    this.safe = { x: this.x, y: this.y };
    this.anim = 0; this.squash = 0; this.wasGrounded = false;
    this.frozen = 0;
  }

  get cx() { return this.x + this.w / 2; }
  get cy() { return this.y + this.h / 2; }

  canFocus() { return this.grounded && this.ink >= P.skillCost && this.hp < this.maxHp && !this.atk && this.dashT <= 0; }

  // ---------- 手势事件 ----------
  handle(ev, g) {
    if (this.stun > 0 || this.frozen > 0) return;
    switch (ev.type) {
      case 'flick':
        if (ev.dir === 'up') this.jumpBuf = P.buffer;
        else if (ev.dir === 'down') this.down(g);
        else if (ev.dir === 'left' || ev.dir === 'right' || ev.dir === 'dash') {
          const d = ev.dir === 'dash' ? this.facing : ev.dir === 'left' ? -1 : 1;
          this.dash(g, d);
        }
        break;
      case 'tap': {
        const [wx, wy] = g.screenToWorld(ev.x, ev.y);
        this.attack(g, this.dirTo(wx, wy), false);
        break;
      }
      case 'keyAttack': {
        const k = g.input.keys;
        const dir = (k.has('ArrowUp') || k.has('w')) ? 'up' : ((k.has('ArrowDown') || k.has('s')) && !this.grounded) ? 'down' : (this.facing > 0 ? 'right' : 'left');
        this.attack(g, dir, false);
        break;
      }
      case 'heavy': {
        const [wx, wy] = g.screenToWorld(ev.x, ev.y);
        const d = this.dirTo(wx, wy);
        this.attack(g, d === 'up' || d === 'down' ? (this.facing > 0 ? 'right' : 'left') : d, true);
        break;
      }
      case 'keyHeavy': this.attack(g, this.facing > 0 ? 'right' : 'left', true); break;
      case 'bolt': this.bolt(g, ev.dx, ev.dy); break;
      case 'keyBolt': this.bolt(g, this.facing, 0); break;
      case 'circle': this.vortex(g); break;
    }
  }

  // 点哪砍哪：点在上/下方的锥形区域里就上挑/下劈，否则左右
  dirTo(wx, wy) {
    const dx = wx - this.cx, dy = wy - this.cy;
    if (dy < 0 && -dy > Math.abs(dx) * 1.1) return 'up';
    if (dy > 0 && dy > Math.abs(dx) * 1.1 && !this.grounded) return 'down';
    if (Math.abs(dx) < 2) return this.facing > 0 ? 'right' : 'left';
    return dx > 0 ? 'right' : 'left';
  }

  down(g) {
    if (this.grounded) {
      const bench = g.benchNear(this);
      if (bench) { g.rest(bench); return; }
      if (groundBelow(this, g.world) === 'oneway') { this.dropT = 0.18; this.y += 1; return; }
    } else if (!this.plunging && this.dashT <= 0) {
      this.plunging = true;
      this.atk = null;
      this.vx = 0; this.vy = P.plungeV;
      g.fx.ink(this.cx, this.cy, 5, 60, { dir: -Math.PI / 2, spread: 1 });
    }
  }

  dash(g, d) {
    if (!g.save.abilities.dash) { g.hintOnce('nodash', '需要先找到「疾影」'); return; }
    if (this.dashCd > 0 || (!this.grounded && !this.airDash) || this.plunging) return;
    this.facing = d;
    this.dashT = P.dashT; this.dashCd = P.dashCd;
    if (!this.grounded) this.airDash = false;
    this.vx = d * P.dashV; this.vy = 0;
    this.atk = null;
    g.fx.ink(this.cx - d * 4, this.cy, 6, 80, { dir: d > 0 ? Math.PI : 0, spread: 0.8, up: 0 });
  }

  attack(g, dir, heavy) {
    if (this.atkCd > 0 || this.dashT > 0 || this.plunging) return;
    if (dir === 'left') this.facing = -1;
    if (dir === 'right') this.facing = 1;
    this.atk = { dir, heavy, t: 0, hit: new Set() };
    this.atkCd = heavy ? P.heavyT + 0.08 : P.atkCd;
    const [sx, sy] = this.slashPos(dir, heavy);
    g.fx.slash(sx, sy, dir, heavy);
    if (heavy) { g.fx.shake(2, 0.12); this.vx = this.facing * 40; }
  }

  slashPos(dir, heavy) {
    const r = heavy ? 6 : 4;
    switch (dir) {
      case 'up': return [this.cx, this.cy - r];
      case 'down': return [this.cx, this.cy + r];
      case 'left': return [this.cx - r, this.cy];
      default: return [this.cx + r, this.cy];
    }
  }

  hitbox() {
    const a = this.atk;
    const big = a.heavy ? 1.35 : 1;
    const L = 22 * big, W = 18 * big;
    switch (a.dir) {
      case 'up': return { x: this.cx - W / 2, y: this.y - L + 4, w: W, h: L };
      case 'down': return { x: this.cx - W / 2, y: this.y + this.h - 4, w: W, h: L };
      case 'left': return { x: this.x - L + 3, y: this.cy - W / 2, w: L, h: W };
      default: return { x: this.x + this.w - 3, y: this.cy - W / 2, w: L, h: W };
    }
  }

  bolt(g, dx, dy) {
    if (!g.save.abilities.bolt) return;
    if (this.ink < P.skillCost) { g.hintOnce('noink', '墨不够了（砍中敌人积攒）'); return; }
    this.ink -= P.skillCost;
    g.spawnBolt(this.cx, this.cy, dx, dy);
    this.vx -= dx * 90; if (!this.grounded) this.vy -= dy * 60;
    if (Math.abs(dx) > 0.3) this.facing = Math.sign(dx);
    g.fx.shake(2.5, 0.15);
  }

  vortex(g) {
    if (!g.save.abilities.vortex) return;
    if (this.ink < P.skillCost) { g.hintOnce('noink', '墨不够了（砍中敌人积攒）'); return; }
    this.ink -= P.skillCost;
    g.spawnVortex(this);
    if (!this.grounded) this.vy = Math.min(this.vy, -60);
  }

  hurt(g, srcX, dmg = 1) {
    if (this.invuln > 0 || this.dashT > 0 || g.dead) return false;
    this.hp -= dmg;
    this.invuln = P.invuln;
    this.stun = 0.25;
    this.focusT = 0;
    this.plunging = false; this.atk = null;
    const d = Math.sign(this.cx - srcX) || -this.facing;
    this.vx = d * 170; this.vy = -230;
    g.hitstop(0.09); g.fx.shake(4, 0.25); g.fx.flash(0.08);
    g.fx.ink(this.cx, this.cy, 14, 160);
    g.vibrate(40);
    if (this.hp <= 0) g.die();
    return true;
  }

  // ---------- 每帧 ----------
  update(dt, g) {
    const input = g.input;
    this.anim += dt;
    if (this.frozen > 0) { this.frozen -= dt; return; }
    this.invuln = Math.max(0, this.invuln - dt);
    this.stun = Math.max(0, this.stun - dt);
    this.dashCd = Math.max(0, this.dashCd - dt);
    this.atkCd = Math.max(0, this.atkCd - dt);
    this.jumpBuf = Math.max(0, this.jumpBuf - dt);
    this.dropT = Math.max(0, this.dropT - dt);
    this.squash = Math.max(0, this.squash - dt * 5);

    // 凝神回血
    this.focusing = input.focusing && this.canFocus() && this.stun <= 0;
    if (this.focusing) {
      this.focusT += dt;
      this.vx *= 0.7;
      if (Math.random() < 0.5) g.fx.ink(this.cx + (Math.random() - 0.5) * 30, this.cy + 10, 1, 20, { dir: -Math.PI / 2, spread: 0.3, gravity: -150, up: 0, white: false, stick: false });
      if (this.focusT >= P.focusT) {
        this.focusT = 0; this.ink -= P.skillCost; this.hp++;
        g.fx.ring(this.cx, this.cy, 4, 26, 0.4, 3); g.fx.flash(0.05);
        g.vibrate(20);
      }
    } else this.focusT = 0;

    // 水平
    const axis = (this.stun > 0 || this.focusing) ? 0 : input.moveAxis;
    if (this.dashT > 0) {
      this.dashT -= dt;
      this.vy = 0;
      if (this.dashT <= 0) this.vx = this.facing * P.run;
      if (Math.random() < 0.6) g.fx.ink(this.cx - this.facing * 5, this.cy + (Math.random() - 0.5) * 8, 1, 30, { gravity: 0, up: 0, size: 1.2 });
    } else if (this.stun <= 0) {
      const target = axis * P.run;
      const acc = this.grounded ? P.accelGround : P.accelAir;
      this.vx = approach(this.vx, target, acc * dt);
      if (axis && !this.atk) this.facing = axis;
    } else {
      this.vx = approach(this.vx, 0, 400 * dt);
    }

    // 竖直
    if (this.dashT <= 0) {
      if (this.plunging) this.vy = P.plungeV;
      else this.vy = Math.min(P.maxFall, this.vy + P.grav * (this.vy > 0 ? P.fallMult : 1) * dt);
    }

    // 跳跃（缓冲 + 土狼时间）
    if (this.jumpBuf > 0 && this.stun <= 0 && !this.plunging) {
      if (this.grounded || this.coyote > 0) {
        this.vy = -P.jumpV; this.grounded = false; this.coyote = 0; this.jumpBuf = 0;
        this.dashT = Math.min(this.dashT, 0);
        this.squash = -1;
        g.fx.dust(this.cx, this.y + this.h, 5);
      }
    }

    const res = moveBody(this, g.world, dt, { drop: this.dropT > 0 });

    this.wasGrounded = this.grounded;
    this.grounded = res.ground;
    if (this.grounded) {
      this.coyote = P.coyote; this.airDash = true;
      if (!this.wasGrounded) {
        this.squash = 1;
        g.fx.dust(this.cx, this.y + this.h, 3);
      }
      if (this.plunging) this.land(g);
    } else this.coyote = Math.max(0, this.coyote - dt);

    // 记录安全落脚点（掉进尖刺后回到这里）
    if (this.grounded && !res.oneWay && !res.spike) {
      const ty = Math.floor((this.y + this.h + 1) / T);
      const l = Math.floor(this.x / T), r = Math.floor((this.x + this.w) / T);
      if (g.world.solid(l, ty) && g.world.solid(r, ty) && !g.world.spike(l - 1, ty - 1) && !g.world.spike(r + 1, ty - 1)) {
        this.safe = { x: this.x, y: this.y };
      }
    }

    // 攻击判定
    if (this.atk) {
      const a = this.atk;
      a.t += dt;
      const active = a.heavy ? P.heavyActive : P.atkActive;
      if (a.t <= active) this.resolveHit(g, this.hitbox(), a);
      if (a.t >= (a.heavy ? P.heavyT : P.atkT)) this.atk = null;
    }

    if (res.spike) this.spiked(g);
  }

  resolveHit(g, box, a) {
    const dmg = a.heavy ? 3 : 1;
    let any = false;
    for (const e of g.enemies) {
      if (e.dead || a.hit.has(e)) continue;
      if (overlap(box, e)) {
        a.hit.add(e);
        const kx = a.dir === 'left' ? -1 : a.dir === 'right' ? 1 : 0;
        const ky = a.dir === 'up' ? -1 : a.dir === 'down' ? 1 : 0;
        e.damage(g, dmg, kx, ky, a.heavy);
        this.ink = Math.min(P.inkMax, this.ink + P.inkHit);
        any = true;
      }
    }
    // 裂墙
    if (a.heavy && !a.broke) {
      const x0 = Math.floor(box.x / T), x1 = Math.floor((box.x + box.w) / T);
      const y0 = Math.floor(box.y / T), y1 = Math.floor((box.y + box.h) / T);
      for (let ty = y0; ty <= y1 && !a.broke; ty++) for (let tx = x0; tx <= x1; tx++) {
        if (g.world.tile(tx, ty) === 'X') { g.breakWall(tx, ty); a.broke = true; break; }
      }
    } else if (!a.heavy && !a.clinked && a.dir !== 'down') {
      const tx = Math.floor((a.dir === 'left' ? box.x : a.dir === 'right' ? box.x + box.w : box.x + box.w / 2) / T);
      const ty = Math.floor((a.dir === 'up' ? box.y : box.y + box.h / 2) / T);
      if (g.world.tile(tx, ty) === 'X') { a.clinked = true; g.fx.ink(tx * T + T / 2, this.cy, 3, 60); g.hintOnce('crack', '裂墙需要重击（长按后松开）'); }
    }
    // 下劈弹跳：敌人或尖刺
    if (a.dir === 'down' && !a.pogo) {
      let pogo = any;
      if (!pogo) {
        const x0 = Math.floor(box.x / T), x1 = Math.floor((box.x + box.w) / T);
        const ty = Math.floor((box.y + box.h - 2) / T);
        for (let tx = x0; tx <= x1; tx++) if (g.world.spike(tx, ty)) pogo = true;
      }
      if (pogo) {
        a.pogo = true;
        this.vy = -P.pogoV; this.airDash = true;
        g.fx.ring(this.cx, this.y + this.h + 6, 2, 12, 0.2);
      }
    }
    if (any) {
      g.hitstop(a.heavy ? 0.08 : 0.045);
      g.fx.shake(a.heavy ? 3 : 1.5, 0.1);
      if (a.heavy) g.fx.flash(0.05);
      if (a.dir === 'left' || a.dir === 'right') this.vx -= this.facing * 70;
      g.vibrate(a.heavy ? 25 : 10);
    }
  }

  land(g) {
    this.plunging = false;
    g.fx.shake(3.5, 0.18);
    g.fx.ring(this.cx, this.y + this.h, 4, 30, 0.3, 3);
    g.fx.ink(this.cx, this.y + this.h, 12, 140, { dir: -Math.PI / 2, spread: 2.6 });
    g.hitstop(0.04);
    const box = { x: this.cx - 26, y: this.y - 6, w: 52, h: this.h + 10 };
    for (const e of g.enemies) {
      if (!e.dead && overlap(box, e)) {
        e.damage(g, 2, Math.sign(e.x + e.w / 2 - this.cx) || 1, -1, true);
        this.ink = Math.min(P.inkMax, this.ink + P.inkHit);
      }
    }
    g.vibrate(20);
  }

  spiked(g) {
    if (g.dead) return;
    this.invuln = 0; this.dashT = 0;
    const ok = this.hurt(g, this.cx);
    if (!ok) return;
    if (this.hp > 0) {
      this.frozen = 0.35;
      g.fadeTo(() => {
        this.x = this.safe.x; this.y = this.safe.y;
        this.vx = this.vy = 0; this.plunging = false; this.stun = 0;
      });
    }
  }

  // ---------- 绘制 ----------
  draw(ctx) {
    if (this.invuln > 0 && Math.floor(this.invuln * 16) % 2 === 0 && this.frozen <= 0) return;
    const cx = Math.round(this.cx), by = Math.round(this.y + this.h);
    const f = this.facing;
    const running = this.grounded && Math.abs(this.vx) > 10;
    const bob = running ? Math.round(Math.abs(Math.sin(this.anim * 14))) : 0;
    let sx = 1, sy = 1;
    if (this.squash > 0) { sx = 1 + this.squash * 0.25; sy = 1 - this.squash * 0.2; }
    if (this.squash < 0) { sx = 0.85; sy = 1.15; }
    if (!this.grounded && this.vy < -50) { sx = 0.9; sy = 1.1; }
    if (this.dashT > 0) { sx = 1.3; sy = 0.8; }
    if (this.focusing) { sy = 0.92; }

    ctx.save();
    ctx.translate(cx, by);
    ctx.scale(sx * f, sy);

    // 斗篷（随速度飘）
    const flow = Math.max(-1, Math.min(1, -Math.abs(this.vx) / P.run)) * 3 + Math.sin(this.anim * 8) * 0.6;
    ctx.fillStyle = INK;
    ctx.beginPath();
    ctx.moveTo(-3, -8 - bob);
    ctx.lineTo(3, -8 - bob);
    ctx.lineTo(4.5, 0);
    ctx.lineTo(-4 + flow, 0);
    ctx.lineTo(-5 + flow * 1.3, -2);
    ctx.closePath();
    ctx.fill();
    // 腿
    if (running) {
      const p = Math.sin(this.anim * 14) * 1.5;
      ctx.fillRect(-2 + p, -1, 1.5, 1);
      ctx.fillRect(1 - p, -1, 1.5, 1);
    }
    // 角
    ctx.beginPath();
    ctx.moveTo(-3, -13 - bob); ctx.lineTo(-4, -17 - bob); ctx.lineTo(-1.5, -14 - bob);
    ctx.moveTo(3, -13 - bob); ctx.lineTo(4, -17 - bob); ctx.lineTo(1.5, -14 - bob);
    ctx.fill();
    // 面具
    ctx.fillStyle = PAPER;
    ctx.strokeStyle = INK; ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.ellipse(0.5, -11 - bob, 3.6, 3.4, 0, 0, Math.PI * 2);
    ctx.fill(); ctx.stroke();
    // 眼
    ctx.fillStyle = INK;
    ctx.fillRect(0.2, -12.5 - bob, 1.1, 2.2);
    ctx.fillRect(2.4, -12.5 - bob, 1.1, 2.2);

    // 剑
    if (this.atk) {
      ctx.strokeStyle = INK; ctx.lineWidth = 1.2;
      ctx.beginPath();
      const d = this.atk.dir;
      if (d === 'up') { ctx.moveTo(2, -8); ctx.lineTo(3, -24); }
      else if (d === 'down') { ctx.moveTo(2, -4); ctx.lineTo(3, 10); }
      else { ctx.moveTo(3, -6); ctx.lineTo(this.atk.heavy ? 20 : 15, -7); }
      ctx.stroke();
    } else if (this.plunging) {
      ctx.strokeStyle = INK; ctx.lineWidth = 1.2;
      ctx.beginPath(); ctx.moveTo(1, -4); ctx.lineTo(1, 9); ctx.stroke();
    }
    ctx.restore();
  }
}

function approach(v, t, d) {
  return v < t ? Math.min(t, v + d) : Math.max(t, v - d);
}

export const PLAYER_CFG = P;
