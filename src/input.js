// 手势识别（第二版）：「指令 + 反射 + 多指」
//
// 单指：轻点 = 指令（去哪 / 打谁）；长按松开 = 重击指令；快划 = 反射动作（闪避 / 跳 / 下砸）
// 双指：按住 = 凝神；拧转 = 墨旋；一起划 = 墨弹
// 三指：轻点 = 地图
//
// 几乎同时按下（multiGap 内）的手指归为一「组」，组的手指数决定是几指手势。
// 产出离散事件（poll() 取走）和持续状态（focusing）。

export const CFG = {
  multiGap: 140,     // 这段时间内落下的手指算同一组
  tapMove: 14,       // 轻点允许的最大位移（CSS px）
  holdTime: 380,     // 超过它松开算长按（重击）
  swipeDist: 30,     // 快划最小位移
  swipeTime: 260,    // 快划必须在按下后这么久内完成
  focusHold: 280,    // 双指按住多久开始凝神
  focusDrift: 18,    // 凝神时双指允许的漂移
  twistAngle: 1.0,   // 双指拧转多少弧度触发墨旋（约 57°）
  boltDist: 45,      // 双指一起划的最小位移
  boltTime: 320,
  multiTapMax: 350,  // 多指轻点的最长时间
};

export class Gestures {
  constructor(el) {
    this.el = el;
    this.touches = new Map();   // pointerId → touch
    this.groups = new Set();
    this.queue = [];
    this.focusing = false;
    this.trails = [];
    this.keys = new Set();

    const opts = { passive: false };
    el.addEventListener('pointerdown', e => this._down(e), opts);
    el.addEventListener('pointermove', e => this._move(e), opts);
    el.addEventListener('pointerup', e => this._up(e), opts);
    el.addEventListener('pointercancel', e => this._up(e, true), opts);
    el.addEventListener('contextmenu', e => e.preventDefault());
    addEventListener('keydown', e => this._key(e, true));
    addEventListener('keyup', e => this._key(e, false));
  }

  poll() { const q = this.queue; this.queue = []; return q; }
  emit(ev) { this.queue.push(ev); }

  _down(e) {
    e.preventDefault();
    try { this.el.setPointerCapture(e.pointerId); } catch (err) { /* 合成事件没有活动指针 */ }
    const now = performance.now();
    const t = { id: e.pointerId, x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY, t0: now, pts: [{ x: e.clientX, y: e.clientY, t: now }] };
    let g = null;
    for (const o of this.groups) {
      if (!o.done && !o.closed && now - o.t0 < CFG.multiGap && o.members.length < 3) { g = o; break; }
    }
    if (!g) { g = { members: [], t0: now, done: false }; this.groups.add(g); }
    g.members.push(t);
    t.group = g;
    if (g.members.length === 2) this._initPair(g);
    this.touches.set(e.pointerId, t);
  }

  _initPair(g) {
    const [a, b] = g.members;
    g.a0 = Math.atan2(b.y - a.y, b.x - a.x);
    g.rot = 0; g.lastA = g.a0;
    g.c0 = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    g.d0 = Math.hypot(b.x - a.x, b.y - a.y);
  }

  _move(e) {
    const t = this.touches.get(e.pointerId);
    if (!t) return;
    e.preventDefault();
    const now = performance.now();
    t.x = e.clientX; t.y = e.clientY;
    t.pts.push({ x: t.x, y: t.y, t: now });
    if (t.pts.length > 30) t.pts.shift();
    const g = t.group;
    if (g.done) return;
    const n = g.members.length;

    if (n === 1) {
      const dx = t.x - t.x0, dy = t.y - t.y0, d = Math.hypot(dx, dy);
      if (d > CFG.swipeDist) {
        // 还在等第二根手指的窗口内也照样触发：快划要快
        if (now - t.t0 < CFG.swipeTime) this.emit({ type: 'swipe', dx: dx / d, dy: dy / d, dir: dirOf(dx, dy) });
        g.done = true; // 慢慢拖动不算任何手势
      }
    } else if (n === 2) {
      const [a, b] = g.members;
      const ang = Math.atan2(b.y - a.y, b.x - a.x);
      g.rot += angDiff(ang, g.lastA); g.lastA = ang;
      const cx = (a.x + b.x) / 2 - g.c0.x, cy = (a.y + b.y) / 2 - g.c0.y;
      const cd = Math.hypot(cx, cy);
      if (Math.abs(g.rot) > CFG.twistAngle) {
        this.emit({ type: 'twist', dir: Math.sign(g.rot) });
        g.done = true;
      } else if (cd > CFG.boltDist && now - g.t0 < CFG.boltTime + CFG.multiGap && !g.focus) {
        this.emit({ type: 'bolt', dx: cx / cd, dy: cy / cd });
        g.done = true;
      } else if (g.focus && (cd > CFG.focusDrift * 2)) {
        g.focus = false; g.done = true;
      }
    }
  }

  _up(e, cancelled = false) {
    const t = this.touches.get(e.pointerId);
    if (!t) return;
    e.preventDefault?.();
    this.touches.delete(e.pointerId);
    const now = performance.now();
    if (t.pts.length > 2) this.trails.push({ pts: t.pts, t: now });
    const g = t.group;
    g.closed = true; // 有手指抬起后，这组不再接收新手指
    const lifted = (g.lifted = (g.lifted || 0) + 1);
    const allUp = lifted === g.members.length;
    if (allUp) this.groups.delete(g);
    if (cancelled || g.done) { if (g.focus) g.focus = false; return; }

    const n = g.members.length;
    const held = now - g.t0;
    const still = g.members.every(m => Math.hypot(m.x - m.x0, m.y - m.y0) < CFG.tapMove);
    if (n === 1) {
      if (!still) return;
      this.emit({ type: held >= CFG.holdTime ? 'heavy' : 'tap', x: t.x, y: t.y });
      g.done = true;
    } else if (n === 2) {
      if (g.focus) { g.focus = false; g.done = true; return; }
      if (still && held < CFG.multiTapMax) this.emit({ type: 'twoTap' });
      g.done = true;
    } else if (n === 3) {
      if (still && held < CFG.multiTapMax + 150) this.emit({ type: 'map' });
      g.done = true;
    }
  }

  // 每帧：更新持续状态
  update() {
    const now = performance.now();
    let focusing = false;
    for (const g of this.groups) {
      if (g.members.length !== 2 || g.done || g.lifted) continue;
      const [a, b] = g.members;
      const drift = Math.max(Math.hypot(a.x - a.x0, a.y - a.y0), Math.hypot(b.x - b.x0, b.y - b.y0));
      if (!g.focus && now - g.t0 > CFG.focusHold && drift < CFG.focusDrift && Math.abs(g.rot) < 0.35) g.focus = true;
      if (g.focus) focusing = true;
    }
    if (this.keys.has('f')) focusing = true;
    this.focusing = focusing;
    this.trails = this.trails.filter(tr => now - tr.t < 260);
  }

  // 给渲染层：单指长按中的触点（蓄力环）
  charges() {
    const now = performance.now();
    const out = [];
    for (const g of this.groups) {
      if (g.members.length !== 1 || g.done) continue;
      const t = g.members[0];
      if (!this.touches.has(t.id)) continue;
      if (Math.hypot(t.x - t.x0, t.y - t.y0) < CFG.tapMove) out.push({ x: t.x, y: t.y, held: now - t.t0 });
    }
    return out;
  }

  // 给渲染层：双指组（凝神 / 拧转进度）
  pairs() {
    const out = [];
    for (const g of this.groups) {
      if (g.members.length !== 2 || g.done) continue;
      const [a, b] = g.members;
      out.push({ a, b, rot: g.rot, focus: !!g.focus });
    }
    return out;
  }

  // 键盘（桌面调试）
  keyAxis() {
    const k = this.keys;
    return (k.has('ArrowRight') || k.has('d') ? 1 : 0) - (k.has('ArrowLeft') || k.has('a') ? 1 : 0);
  }
  _key(e, down) {
    const k = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (!down) { this.keys.delete(k); return; }
    if (this.keys.has(k)) return;
    this.keys.add(k);
    const act = {
      ArrowUp: 'jump', w: 'jump', k: 'jump', ' ': 'jump', ArrowDown: 'down', s: 'down',
      j: 'attack', u: 'heavy', l: 'dash', Shift: 'dash', o: 'twist', i: 'bolt', m: 'map', Escape: 'map',
    }[k];
    if (act) this.emit({ type: 'key', act });
  }
}

function dirOf(dx, dy) {
  // 上划给更宽的角度：斜上方也算跳
  const a = Math.atan2(dy, dx);
  if (a < -Math.PI * 0.2 && a > -Math.PI * 0.8) return 'up';
  if (a > Math.PI * 0.3 && a < Math.PI * 0.7) return 'down';
  return dx > 0 ? 'right' : 'left';
}

function angDiff(a, b) {
  let d = a - b;
  while (d > Math.PI) d -= Math.PI * 2;
  while (d < -Math.PI) d += Math.PI * 2;
  return d;
}
