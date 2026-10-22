// 手势识别：整屏都是手势区，不用摇杆、不用按钮。
// 产出两类信息：
//   1) 持续状态：moveAxis（-1/0/1）、focusing（长按凝神）、可视化用的 touches
//   2) 离散事件：tap / flick / heavy / bolt / circle / twoTap（用 poll() 取走）

export const CFG = {
  steerStart: 10,      // 位移超过它就进入「拖动」（CSS px）
  steerDead: 8,        // 拖动死区
  steerLeash: 46,      // 手指离锚点超过它时锚点跟随（浮动摇杆）
  flickWindow: 120,    // 快划检测的时间窗（ms）
  flickDist: 34,       // 时间窗内的最小位移
  flickMaxTurn: 0.7,   // 时间窗内允许的最大累计转角（弧度），过滤画圈
  heldFlickGap: 220,   // 按住期间两次快划的最小间隔
  tapMaxMove: 10,
  chargeShow: 180,     // 开始显示蓄力环
  heavyTime: 550,      // 松手算重击的最短按压时间
  focusTime: 1000,     // 按住超过它开始凝神
  chargeDrift: 22,     // 蓄力时允许的漂移，超过就转为拖动
  circleTurn: Math.PI * 1.65,
  circleMinLen: 130,
  circleMinSize: 34,
  circleWindow: 900,
  twoTapGap: 160,      // 两指按下的最大间隔
  twoTapMax: 380,
};

export class Gestures {
  constructor(el) {
    this.el = el;
    this.touches = new Map();
    this.queue = [];
    this.moveAxis = 0;
    this.focusing = false;
    this.trails = [];     // 已松开手指的残影
    this.keys = new Set();
    this._lastSteer = null;

    const opts = { passive: false };
    el.addEventListener('pointerdown', e => this._down(e), opts);
    el.addEventListener('pointermove', e => this._move(e), opts);
    el.addEventListener('pointerup', e => this._up(e), opts);
    el.addEventListener('pointercancel', e => this._up(e, true), opts);
    el.addEventListener('contextmenu', e => e.preventDefault());
    // 桌面调试用键盘
    addEventListener('keydown', e => this._key(e, true));
    addEventListener('keyup', e => this._key(e, false));
  }

  poll() { const q = this.queue; this.queue = []; return q; }
  emit(ev) { this.queue.push(ev); }

  _down(e) {
    e.preventDefault();
    try { this.el.setPointerCapture(e.pointerId); } catch (err) { /* 合成事件没有活动指针 */ }
    const now = performance.now();
    const t = {
      id: e.pointerId, x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY,
      t0: now, state: 'pending', path: [{ x: e.clientX, y: e.clientY, t: now }],
      ax: e.clientX, lastFlick: 0, full: [{ x: e.clientX, y: e.clientY, t: now }],
    };
    // 两指几乎同时按下 → 候选双指轻点
    for (const o of this.touches.values()) {
      if (o.state === 'pending' && now - o.t0 < CFG.twoTapGap) {
        o.state = 'multi'; t.state = 'multi'; t.partner = o; o.partner = t;
        break;
      }
    }
    this.touches.set(e.pointerId, t);
  }

  _move(e) {
    const t = this.touches.get(e.pointerId);
    if (!t) return;
    e.preventDefault();
    const now = performance.now();
    t.x = e.clientX; t.y = e.clientY;
    t.path.push({ x: t.x, y: t.y, t: now });
    t.full.push({ x: t.x, y: t.y, t: now });
    while (t.path.length > 2 && now - t.path[0].t > CFG.circleWindow) t.path.shift();
    while (t.full.length > 40) t.full.shift();
    const dist = Math.hypot(t.x - t.x0, t.y - t.y0);
    const held = now - t.t0;

    if (t.state === 'multi') return;

    if (t.state === 'pending') {
      if (dist > CFG.steerStart) {
        if (held >= CFG.chargeShow) { t.state = 'charge'; }
        else { t.state = 'steer'; t.ax = t.x0; this._lastSteer = t; }
      }
    }

    if (t.state === 'charge') {
      // 蓄力中甩出 → 墨弹
      const f = this._flick(t, now);
      if (f && held >= CFG.heavyTime) {
        this.emit({ type: 'bolt', dx: f.dx, dy: f.dy, dir: f.dir });
        t.state = 'done';
        return;
      }
      if (dist > CFG.chargeDrift && !this.focusing) {
        t.state = 'steer'; t.ax = t.x0; this._lastSteer = t;
      }
    }

    if (t.state === 'steer') {
      // 浮动锚点
      const dx = t.x - t.ax;
      if (Math.abs(dx) > CFG.steerLeash) t.ax = t.x - Math.sign(dx) * CFG.steerLeash;
      // 按住时只认上下快划（左右就是移动本身）
      if (now - t.lastFlick > CFG.heldFlickGap) {
        const f = this._flick(t, now);
        if (f && (f.dir === 'up' || f.dir === 'down')) {
          this.emit({ type: 'flick', dir: f.dir, held: true });
          t.lastFlick = now; // 不清空轨迹，画圈仍可继续识别
          return;
        }
      }
      // 画圈
      if (this._circle(t)) {
        this.emit({ type: 'circle' });
        t.path = [{ x: t.x, y: t.y, t: now }];
      }
    }
  }

  _up(e, cancelled = false) {
    const t = this.touches.get(e.pointerId);
    if (!t) return;
    e.preventDefault?.();
    this.touches.delete(e.pointerId);
    const now = performance.now();
    const held = now - t.t0;
    if (this._lastSteer === t) this._lastSteer = null;
    if (t.full.length > 2) this.trails.push({ pts: t.full, t: now });
    if (cancelled) return;

    switch (t.state) {
      case 'multi': {
        const p = t.partner;
        // 最后一根手指抬起时判定
        if (p && !this.touches.has(p.id) && held < CFG.twoTapMax) this.emit({ type: 'twoTap' });
        break;
      }
      case 'pending':
      case 'charge': {
        if (t.focusOwner) break; // 凝神过就不再触发攻击
        if (held >= CFG.heavyTime) this.emit({ type: 'heavy', x: t.x, y: t.y });
        else this.emit({ type: 'tap', x: t.x, y: t.y });
        break;
      }
      case 'steer': {
        const f = this._flick(t, now + 1, true);
        if (f) this.emit({ type: 'flick', dir: f.dir, held: false });
        break;
      }
    }
  }

  // 最近 flickWindow 内：位移够大且够直 → 快划
  _flick(t, now, lifting = false) {
    const pts = t.path;
    if (pts.length < 2) return null;
    const end = pts[pts.length - 1];
    if (lifting && now - end.t > 60) return null; // 停住之后再松手不算
    let i = pts.length - 1;
    while (i > 0 && end.t - pts[i - 1].t <= CFG.flickWindow && pts[i - 1].t > t.lastFlick) i--;
    const start = pts[i];
    const dx = end.x - start.x, dy = end.y - start.y;
    const d = Math.hypot(dx, dy);
    if (d < CFG.flickDist) return null;
    let turn = 0, prevA = null;
    for (let k = i + 1; k < pts.length; k++) {
      const sx = pts[k].x - pts[k - 1].x, sy = pts[k].y - pts[k - 1].y;
      if (sx * sx + sy * sy < 4) continue;
      const a = Math.atan2(sy, sx);
      if (prevA !== null) turn += Math.abs(angDiff(a, prevA));
      prevA = a;
    }
    if (turn > CFG.flickMaxTurn) return null;
    const dir = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up');
    return { dx: dx / d, dy: dy / d, dir };
  }

  // 轨迹累计转角 + 长度 + 尺寸 → 画圈
  _circle(t) {
    const pts = t.path;
    if (pts.length < 8) return false;
    let turn = 0, len = 0, prevA = null;
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    let last = pts[0];
    for (let k = 1; k < pts.length; k++) {
      const p = pts[k];
      const sx = p.x - last.x, sy = p.y - last.y;
      const sl = Math.hypot(sx, sy);
      if (sl < 3) continue;
      len += sl;
      const a = Math.atan2(sy, sx);
      if (prevA !== null) turn += angDiff(a, prevA);
      prevA = a; last = p;
      minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x);
      minY = Math.min(minY, p.y); maxY = Math.max(maxY, p.y);
    }
    return Math.abs(turn) > CFG.circleTurn && len > CFG.circleMinLen &&
      Math.min(maxX - minX, maxY - minY) > CFG.circleMinSize;
  }

  // 每帧调用：更新持续状态
  update(canFocus) {
    const now = performance.now();
    let axis = 0;
    const s = this._lastSteer;
    if (s && this.touches.has(s.id) && s.state === 'steer') {
      const dx = s.x - s.ax;
      if (Math.abs(dx) > CFG.steerDead) axis = Math.sign(dx);
    }
    let focusing = false;
    for (const t of this.touches.values()) {
      if (t.state === 'pending' && now - t.t0 >= CFG.chargeShow) t.state = 'charge';
      if (t.state === 'charge' && now - t.t0 >= CFG.focusTime && canFocus) {
        focusing = true; t.focusOwner = true;
      }
    }
    // 键盘
    if (this.keys.has('ArrowLeft') || this.keys.has('a')) axis = -1;
    if (this.keys.has('ArrowRight') || this.keys.has('d')) axis = 1;
    if (this.keys.has('f')) focusing = canFocus;
    this.moveAxis = axis;
    this.focusing = focusing;
    this.trails = this.trails.filter(tr => now - tr.t < 260);
  }

  // 给渲染层：正在蓄力的触点
  charges() {
    const now = performance.now();
    const out = [];
    for (const t of this.touches.values()) {
      if (t.state === 'charge') out.push({ x: t.x, y: t.y, held: now - t.t0 });
    }
    return out;
  }

  _key(e, down) {
    const k = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (down) {
      if (this.keys.has(k)) return;
      this.keys.add(k);
      const map = {
        ArrowUp: ['flick', 'up'], w: ['flick', 'up'], k: ['flick', 'up'], ' ': ['flick', 'up'],
        ArrowDown: ['flick', 'down'], s: ['flick', 'down'],
      };
      if (map[k]) this.emit({ type: map[k][0], dir: map[k][1], held: true });
      if (k === 'l' || k === 'Shift') this.emit({ type: 'flick', dir: 'dash', held: false });
      if (k === 'j') this.emit({ type: 'keyAttack' });
      if (k === 'u') this.emit({ type: 'keyHeavy' });
      if (k === 'i') this.emit({ type: 'keyBolt' });
      if (k === 'o') this.emit({ type: 'circle' });
      if (k === 'm' || k === 'Escape') this.emit({ type: 'twoTap' });
    } else {
      this.keys.delete(k);
    }
  }
}

function angDiff(a, b) {
  let d = a - b;
  while (d > Math.PI) d -= Math.PI * 2;
  while (d < -Math.PI) d += Math.PI * 2;
  return d;
}
