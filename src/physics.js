import { T } from './world.js';

// AABB 对瓦片网格：先 X 后 Y，大位移拆成小步避免穿墙
export function moveBody(b, world, dt, opts = {}) {
  const res = { ground: false, wallL: false, wallR: false, ceil: false, spike: false, oneWay: false };
  let mx = b.vx * dt, my = b.vy * dt;
  const steps = Math.max(1, Math.ceil(Math.max(Math.abs(mx), Math.abs(my)) / 6));
  mx /= steps; my /= steps;
  for (let i = 0; i < steps; i++) {
    stepX(b, world, mx, res);
    stepY(b, world, my, res, opts);
  }
  // 尖刺：只算瓦片下半部分
  const x0 = Math.floor((b.x + 1) / T), x1 = Math.floor((b.x + b.w - 1) / T);
  const y0 = Math.floor((b.y + 1) / T), y1 = Math.floor((b.y + b.h - 0.5) / T);
  for (let ty = y0; ty <= y1; ty++) for (let tx = x0; tx <= x1; tx++) {
    if (world.spike(tx, ty) && b.y + b.h > ty * T + T * 0.45) res.spike = true;
  }
  return res;
}

function stepX(b, world, mx, res) {
  if (!mx) return;
  b.x += mx;
  const y0 = Math.floor(b.y / T), y1 = Math.floor((b.y + b.h - 0.01) / T);
  if (mx > 0) {
    const tx = Math.floor((b.x + b.w) / T);
    for (let ty = y0; ty <= y1; ty++) if (world.solid(tx, ty)) {
      b.x = tx * T - b.w; b.vx = 0; res.wallR = true; return;
    }
  } else {
    const tx = Math.floor(b.x / T);
    for (let ty = y0; ty <= y1; ty++) if (world.solid(tx, ty)) {
      b.x = (tx + 1) * T; b.vx = 0; res.wallL = true; return;
    }
  }
}

function stepY(b, world, my, res, opts) {
  if (!my) return;
  const prevBottom = b.y + b.h;
  b.y += my;
  const x0 = Math.floor(b.x / T), x1 = Math.floor((b.x + b.w - 0.01) / T);
  if (my > 0) {
    const ty = Math.floor((b.y + b.h) / T);
    for (let tx = x0; tx <= x1; tx++) {
      const top = ty * T;
      const hitSolid = world.solid(tx, ty);
      const hitOneWay = !opts.drop && world.oneWay(tx, ty) && prevBottom <= top + 0.5;
      if (hitSolid || hitOneWay) {
        b.y = top - b.h; b.vy = 0; res.ground = true;
        if (hitOneWay && !hitSolid) res.oneWay = true;
        return;
      }
    }
  } else {
    const ty = Math.floor(b.y / T);
    for (let tx = x0; tx <= x1; tx++) if (world.solid(tx, ty)) {
      b.y = (ty + 1) * T; b.vy = 0; res.ceil = true; return;
    }
  }
}

// 脚下贴地检测（不移动）
export function groundBelow(b, world) {
  const ty = Math.floor((b.y + b.h + 1) / T);
  const x0 = Math.floor(b.x / T), x1 = Math.floor((b.x + b.w - 0.01) / T);
  for (let tx = x0; tx <= x1; tx++) {
    if (world.solid(tx, ty)) return 'solid';
    if (world.oneWay(tx, ty) && Math.abs(b.y + b.h - ty * T) < 1) return 'oneway';
  }
  return null;
}

export function overlap(a, b) {
  return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
}
