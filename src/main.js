// 入口：循环、相机、存档、实体管理
import { World, T } from './world.js';
import { Gestures } from './input.js';
import { Player } from './player.js';
import { makeEnemy } from './enemies.js';
import { FX, INK, PAPER } from './fx.js';
import { overlap } from './physics.js';
import { RES, buildTileCache, repaintTiles, drawBackground, drawVignette } from './render.js';
import { drawHUD, drawHints, drawToasts, drawGestures, drawMap, drawTitle } from './hud.js';

const VW = 192;           // 逻辑宽度：12 格
const STEP = 1 / 60;
const SAVE_KEY = 'ink-knight-save-v1';

const ITEMS = {
  D: { ability: 'dash', title: '疾影冲刺', sub: '左 / 右快划 → 冲刺' },
  S: { ability: 'vortex', title: '墨旋', sub: '画一个圈 → 身边卷起墨旋（33 墨）' },
  K: { ability: 'bolt', title: '墨弹', sub: '长按蓄力后甩出 → 射出墨弹（33 墨）' },
  H: { title: '面具碎片', sub: '生命上限 +1' },
};

class Game {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.buf = document.createElement('canvas');
    this.bctx = this.buf.getContext('2d');
    this.input = new Gestures(canvas);
    this.fx = new FX();
    this.world = new World();
    this.save = this.loadSave();
    for (const k of this.save.broken) { const [x, y] = k.split(',').map(Number); this.world.breakWall(x, y); }
    this.tiles = buildTileCache(this.world);

    this.player = new Player(0, 0);
    this.player.maxHp = this.save.maxHp;
    this.player.hp = this.player.maxHp;
    this.enemies = []; this.bolts = []; this.vortexes = [];
    this.respawnAtBench(true);

    this.state = 'title';
    this.hitstopT = 0;
    this.fade = { a: 0, dir: 0, cb: null };
    this.hinted = new Set();
    this.time = 0;
    this.safeTop = 0;

    addEventListener('resize', () => this.resize());
    // 第一次触摸时尝试全屏 + 锁竖屏（安卓有效，iOS 请「添加到主屏幕」）
    canvas.addEventListener('pointerup', () => {
      document.documentElement.requestFullscreen?.().catch(() => {});
      screen.orientation?.lock?.('portrait').catch(() => {});
    }, { once: true });
    this.resize();
    this.last = performance.now();
    this.acc = 0;
    requestAnimationFrame(t => this.frame(t));
  }

  // ---------- 存档 ----------
  loadSave() {
    const def = { bench: null, abilities: { dash: false, vortex: false, bolt: false }, got: [], maxHp: 5, broken: [], visited: [] };
    try {
      const s = JSON.parse(localStorage.getItem(SAVE_KEY));
      if (s) return { ...def, ...s, abilities: { ...def.abilities, ...s.abilities } };
    } catch (e) { /* 无存档或无法访问 */ }
    return def;
  }
  writeSave() {
    try { localStorage.setItem(SAVE_KEY, JSON.stringify(this.save)); } catch (e) { /* 隐私模式等 */ }
  }

  // ---------- 尺寸 ----------
  resize() {
    const dpr = Math.min(3, devicePixelRatio || 1);
    this.dpr = dpr;
    this.W = innerWidth; this.H = innerHeight;
    this.canvas.width = Math.round(this.W * dpr);
    this.canvas.height = Math.round(this.H * dpr);
    this.scale = this.W / VW;
    this.VH = this.H / this.scale;
    this.buf.width = VW * RES;
    this.buf.height = Math.ceil(this.VH * RES);
    const probe = document.getElementById('safe');
    this.safeTop = probe ? parseFloat(getComputedStyle(probe).paddingTop) || 0 : 0;
    this.snapCamera();
  }

  screenToWorld(x, y) { return [this.camX + x / this.scale, this.camY + y / this.scale]; }
  worldToScreen(x, y) { return [(x - this.camX) * this.scale, (y - this.camY) * this.scale]; }

  // ---------- 生成 ----------
  respawnAtBench(first = false) {
    const spawns = this.world.spawns;
    const at = spawns.find(s => s.id === this.save.bench) || spawns.find(s => s.type === 'P');
    this.player.reset(at.x, at.y);
    this.player.hp = this.player.maxHp;
    this.resetEnemies();
    this.room = this.world.roomAtPx(this.player.cx, this.player.cy);
    this.visit(this.room, !first);
    this.snapCamera();
  }

  resetEnemies() {
    this.enemies = this.world.spawns.map(makeEnemy).filter(Boolean);
    this.bolts = []; this.vortexes = [];
  }

  visit(room, banner = true) {
    if (!room) return;
    if (!this.save.visited.includes(room.id)) {
      this.save.visited.push(room.id);
      this.writeSave();
      if (banner) this.roomBanner = { name: room.name, t: 0 };
    }
  }

  // ---------- 供 player 调用 ----------
  hitstop(t) { this.hitstopT = Math.max(this.hitstopT, t); }
  vibrate(ms) { try { navigator.vibrate?.(ms); } catch (e) { /* 不支持 */ } }
  hintOnce(key, text) { if (this.hinted.has(key)) return; this.hinted.add(key); this.toast = { text, t: 0 }; }

  benchNear(p) {
    return this.world.spawns.find(s => s.type === 'B' && Math.abs(s.x - p.cx) < 14 && Math.abs(s.y - (p.y + p.h)) < 4);
  }

  rest(bench) {
    const p = this.player;
    p.hp = p.maxHp;
    p.x = bench.x - p.w / 2; p.vx = 0;
    p.frozen = 0.7;
    this.save.bench = bench.id;
    this.writeSave();
    this.resetEnemies();
    this.fx.ring(bench.x, bench.y - 6, 4, 40, 0.6, 3);
    this.banner = { kicker: '长椅', title: '休 息', sub: '生命回满 · 已存档', t: 0, life: 1.6 };
  }

  die() {
    if (this.dead) return;
    this.dead = true;
    this.hitstop(0.25);
    this.fx.ink(this.player.cx, this.player.cy, 40, 260);
    setTimeout(() => this.fadeTo(() => { this.dead = false; this.respawnAtBench(); }, 0.7), 450);
  }

  fadeTo(cb, speed = 0.25) {
    this.fade = { a: this.fade.a, dir: 1, cb, speed };
  }

  breakWall(tx, ty) {
    const cells = this.world.breakWall(tx, ty);
    if (!cells.length) return;
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const [x, y] of cells) {
      this.save.broken.push(`${x},${y}`);
      x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y);
      this.fx.ink(x * T + T / 2, y * T + T / 2, 10, 160);
    }
    this.writeSave();
    repaintTiles(this.tiles, this.world, x0, y0, x1, y1);
    this.fx.shake(5, 0.3); this.fx.flash(0.08);
    this.hitstop(0.1);
    this.toast = { text: '捷径已打通', t: 0 };
  }

  spawnBolt(x, y, dx, dy) {
    this.bolts.push({ x, y, vx: dx * 340, vy: dy * 340, t: 0, hit: new Set(), trail: [] });
  }
  spawnVortex(p) {
    this.vortexes.push({ t: 0, life: 0.6, hits: new Map() });
    this.fx.ring(p.cx, p.cy, 6, 34, 0.35, 4);
    this.fx.shake(2.5, 0.2);
  }

  // ---------- 循环 ----------
  frame(now) {
    const dt = Math.min(0.1, (now - this.last) / 1000);
    this.last = now;
    this.time += dt;
    this.acc += dt;
    while (this.acc >= STEP) { this.tick(STEP); this.acc -= STEP; }
    this.draw();
    requestAnimationFrame(t => this.frame(t));
  }

  tick(dt) {
    const input = this.input;
    input.update(this.player.canFocus());
    const events = input.poll();

    if (this.state === 'title') {
      if (events.some(e => e.type === 'tap' || e.type === 'heavy' || e.type === 'keyAttack' || e.type === 'flick')) {
        this.state = 'play';
        this.roomBanner = { name: this.room.name, t: 0 };
      }
      return;
    }
    if (this.state === 'map') {
      if (events.some(e => e.type === 'twoTap' || e.type === 'tap')) this.state = 'play';
      return;
    }

    // UI 计时器（顿帧也走）
    if (this.toast) { this.toast.t += dt; if (this.toast.t > 2.2) this.toast = null; }
    if (this.banner) { this.banner.t += dt; if (this.banner.t > this.banner.life) this.banner = null; }
    if (this.roomBanner) this.roomBanner.t += dt;
    if (this.fade.dir) {
      this.fade.a += this.fade.dir * dt / (this.fade.speed || 0.25);
      if (this.fade.a >= 1) { this.fade.a = 1; this.fade.dir = -1; this.fade.cb?.(); this.snapCamera(); }
      if (this.fade.a <= 0) { this.fade.a = 0; this.fade.dir = 0; }
    }

    if (this.hitstopT > 0) { this.hitstopT -= dt; return; }

    for (const ev of events) {
      if (ev.type === 'twoTap') { this.state = 'map'; return; }
      if (!this.dead) this.player.handle(ev, this);
    }

    const p = this.player;
    if (!this.dead) p.update(dt, this);

    // 只更新当前房间附近的敌人
    for (const e of this.enemies) {
      if (e.dead) continue;
      const r = this.world.roomAtPx(e.cx, e.cy);
      if (r !== this.room) continue;
      e.update(dt, this);
    }
    this.enemies = this.enemies.filter(e => !e.dead);

    this.updateBolts(dt);
    this.updateVortexes(dt);
    this.updatePickups();

    // 换房间
    const r = this.world.roomAtPx(p.cx, p.cy);
    if (r && r !== this.room) { this.room = r; this.visit(r); }

    this.fx.update(dt, this.world);
    this.updateCamera(dt);
  }

  updateBolts(dt) {
    for (const b of this.bolts) {
      b.t += dt;
      b.trail.push([b.x, b.y]); if (b.trail.length > 8) b.trail.shift();
      b.x += b.vx * dt; b.y += b.vy * dt;
      const box = { x: b.x - 5, y: b.y - 5, w: 10, h: 10 };
      for (const e of this.enemies) {
        if (e.dead || b.hit.has(e) || !overlap(box, e)) continue;
        b.hit.add(e);
        e.damage(this, 3, Math.sign(b.vx), Math.sign(b.vy) * 0.5, true);
        this.hitstop(0.05);
      }
      if (this.world.solid(Math.floor(b.x / T), Math.floor(b.y / T)) || b.t > 1.2) {
        b.done = true;
        this.fx.ink(b.x, b.y, 14, 150);
        this.fx.ring(b.x, b.y, 2, 14, 0.25);
        if (this.world.tile(Math.floor(b.x / T), Math.floor(b.y / T)) === 'X') this.hintOnce('crack', '裂墙需要重击（长按后松开）');
      }
    }
    this.bolts = this.bolts.filter(b => !b.done);
  }

  updateVortexes(dt) {
    const p = this.player;
    for (const v of this.vortexes) {
      v.t += dt;
      for (const e of this.enemies) {
        if (e.dead) continue;
        const d = Math.hypot(e.cx - p.cx, e.cy - p.cy);
        if (d > 34) continue;
        const last = v.hits.get(e) ?? -1;
        if (v.t - last < 0.15) continue;
        v.hits.set(e, v.t);
        e.damage(this, 1, Math.sign(e.cx - p.cx) || 1, -0.5, false);
        this.hitstop(0.03);
      }
      if (Math.random() < 0.8) {
        const a = v.t * 18 + Math.random();
        this.fx.ink(p.cx + Math.cos(a) * 28, p.cy + Math.sin(a) * 28, 1, 40, { gravity: 100, stick: false });
      }
    }
    this.vortexes = this.vortexes.filter(v => v.t < v.life);
  }

  updatePickups() {
    const p = this.player;
    for (const s of this.world.spawns) {
      const item = ITEMS[s.type];
      if (!item || this.save.got.includes(s.id)) continue;
      const box = { x: s.x - 7, y: s.y - 18, w: 14, h: 14 };
      if (!overlap(p, box)) continue;
      this.save.got.push(s.id);
      if (item.ability) this.save.abilities[item.ability] = true;
      if (s.type === 'H') { this.save.maxHp++; p.maxHp = this.save.maxHp; p.hp = p.maxHp; }
      if (item.ability !== 'dash') p.ink = 99;
      this.writeSave();
      this.banner = { title: item.title, sub: item.sub, t: 0, life: 3.2 };
      this.fx.ring(s.x, s.y - 10, 4, 60, 0.7, 4);
      this.fx.flash(0.1); this.fx.shake(3, 0.3);
      this.hitstop(0.2);
      this.vibrate(60);
    }
  }

  // ---------- 相机 ----------
  cameraTarget() {
    const p = this.player, r = this.room;
    let tx = p.cx + p.facing * 10 - VW / 2;
    let ty = p.cy - 8 - this.VH / 2;
    if (r) {
      tx = r.pw <= VW ? r.px + (r.pw - VW) / 2 : clamp(tx, r.px, r.px + r.pw - VW);
      ty = r.ph <= this.VH ? r.py + (r.ph - this.VH) / 2 : clamp(ty, r.py, r.py + r.ph - this.VH);
    }
    return [tx, ty];
  }
  snapCamera() {
    if (!this.player || this.VH === undefined) return;
    [this.camX, this.camY] = this.cameraTarget();
  }
  updateCamera(dt) {
    const [tx, ty] = this.cameraTarget();
    const k = Math.min(1, dt * 7);
    this.camX += (tx - this.camX) * k;
    this.camY += (ty - this.camY) * Math.min(1, dt * 9);
  }

  // ---------- 绘制 ----------
  draw() {
    const ctx = this.ctx, W = this.W, H = this.H;
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    if (this.state === 'title') { drawTitle(ctx, W, H, this.time); return; }

    const b = this.bctx;
    const [sx, sy] = this.fx.shakeOffset();
    const camX = Math.round((this.camX + sx) * RES) / RES, camY = Math.round((this.camY + sy) * RES) / RES;
    b.setTransform(RES, 0, 0, RES, -camX * RES, -camY * RES);
    b.imageSmoothingEnabled = false;

    drawBackground(b, this.world.rooms, camX, camY, VW, this.VH);
    // 地形（只拷贝可见部分）
    const srcX = Math.max(0, camX * RES), srcY = Math.max(0, camY * RES);
    const sw = Math.min(this.buf.width, this.tiles.width - srcX), sh = Math.min(this.buf.height, this.tiles.height - srcY);
    if (sw > 0 && sh > 0) b.drawImage(this.tiles, srcX, srcY, sw, sh, srcX / RES, srcY / RES, sw / RES, sh / RES);

    this.drawProps(b);
    for (const e of this.enemies) if (!e.dead) e.draw(b);
    if (!this.dead) this.player.draw(b);
    this.drawSkills(b);
    this.fx.draw(b);

    // 缓冲区放大到屏幕
    b.setTransform(1, 0, 0, 1, 0, 0);
    drawVignette(b, this.buf.width, this.buf.height);
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(this.buf, 0, 0, this.buf.width, this.buf.height, 0, 0, W, this.buf.height / RES * this.scale);

    if (this.fx.flashT > 0) {
      ctx.globalCompositeOperation = 'difference';
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, W, H);
      ctx.globalCompositeOperation = 'source-over';
    }

    drawHints(ctx, this, this.room);
    drawGestures(ctx, this.input);
    drawHUD(ctx, this, W, H, this.safeTop);
    drawToasts(ctx, this, W, H);
    if (this.fade.a > 0) {
      ctx.fillStyle = `rgba(17,17,17,${this.fade.a})`;
      ctx.fillRect(0, 0, W, H);
    }
    if (this.state === 'map') drawMap(ctx, this, W, H, this.safeTop);
  }

  drawProps(b) {
    const t = this.time;
    for (const s of this.world.spawns) {
      if (s.type === 'B') {
        const x = s.x, y = s.y;
        b.fillStyle = INK;
        b.fillRect(x - 10, y - 7, 20, 2.5);           // 座面
        b.fillRect(x - 10, y - 13, 20, 2);            // 靠背
        b.fillRect(x - 9, y - 13, 1.5, 13);
        b.fillRect(x + 7.5, y - 13, 1.5, 13);
        b.fillRect(x - 4, y - 13, 1, 6); b.fillRect(x + 3, y - 13, 1, 6);
        continue;
      }
      if (!ITEMS[s.type] || this.save.got.includes(s.id)) continue;
      const x = s.x, y = s.y - 10 + Math.sin(t * 2.5) * 2;
      if (s.type === 'H') {
        b.fillStyle = PAPER; b.strokeStyle = INK; b.lineWidth = 1.2;
        b.beginPath(); b.moveTo(x - 4, y - 3); b.lineTo(x + 4, y - 3); b.quadraticCurveTo(x + 4, y + 5, x, y + 6); b.quadraticCurveTo(x - 4, y + 5, x - 4, y - 3); b.fill(); b.stroke();
        b.fillStyle = INK; b.fillRect(x - 2, y - 1, 1, 2); b.fillRect(x + 1, y - 1, 1, 2);
      } else {
        // 能力之核：旋转的墨环 + 中心字形
        b.strokeStyle = INK; b.lineWidth = 1.2;
        for (let i = 0; i < 3; i++) {
          const a = t * (1.5 + i * 0.7) + i * 2;
          b.beginPath(); b.arc(x, y, 7 + i * 2.5, a, a + Math.PI * 1.2); b.stroke();
        }
        b.fillStyle = INK;
        b.beginPath(); b.arc(x, y, 4 + Math.sin(t * 6) * 0.6, 0, Math.PI * 2); b.fill();
        b.fillStyle = PAPER;
        b.beginPath(); b.arc(x - 1, y - 1, 1.2, 0, Math.PI * 2); b.fill();
      }
    }
  }

  drawSkills(b) {
    const p = this.player;
    b.fillStyle = INK;
    for (const bo of this.bolts) {
      bo.trail.forEach(([x, y], i) => {
        b.globalAlpha = i / bo.trail.length * 0.6;
        b.beginPath(); b.arc(x, y, 1 + i * 0.4, 0, Math.PI * 2); b.fill();
      });
      b.globalAlpha = 1;
      b.beginPath(); b.arc(bo.x, bo.y, 4.5, 0, Math.PI * 2); b.fill();
    }
    for (const v of this.vortexes) {
      const k = v.t / v.life;
      b.strokeStyle = INK;
      for (let i = 0; i < 4; i++) {
        const a = v.t * 16 + i * Math.PI / 2;
        const r = 14 + i * 4 + k * 8;
        b.lineWidth = 3 * (1 - k) + 0.5;
        b.beginPath(); b.arc(p.cx, p.cy, r, a, a + 1.6); b.stroke();
      }
    }
  }
}

function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }

window.game = new Game(document.getElementById('game'));
