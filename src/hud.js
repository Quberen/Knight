// 屏幕空间 UI（CSS 像素坐标）：生命/墨、提示、横幅、地图、手势反馈
import { CW, CH } from './world.js';
import { INK, PAPER } from './fx.js';
import { CFG } from './input.js';

const FONT = '"PingFang SC","Hiragino Sans GB","Noto Sans SC","Microsoft YaHei",sans-serif';

export function drawHUD(ctx, g, W, H, safeTop) {
  const p = g.player;
  const top = safeTop + 14;
  // 墨瓶
  const vx = 30, vy = top + 16, vr = 15;
  ctx.save();
  ctx.beginPath(); ctx.arc(vx, vy, vr, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(233,229,219,0.85)'; ctx.fill();
  ctx.clip();
  const k = p.ink / 99;
  ctx.fillStyle = INK;
  const wave = Math.sin(performance.now() / 300) * 1.5;
  ctx.beginPath();
  ctx.moveTo(vx - vr, vy + vr);
  ctx.lineTo(vx - vr, vy + vr - k * vr * 2 + wave);
  ctx.quadraticCurveTo(vx, vy + vr - k * vr * 2 - wave * 2, vx + vr, vy + vr - k * vr * 2 + wave);
  ctx.lineTo(vx + vr, vy + vr);
  ctx.fill();
  ctx.restore();
  ctx.strokeStyle = INK; ctx.lineWidth = 2.5;
  ctx.beginPath(); ctx.arc(vx, vy, vr, 0, Math.PI * 2); ctx.stroke();
  // 33 墨刻度
  ctx.lineWidth = 1;
  for (const f of [1 / 3, 2 / 3]) {
    const y = vy + vr - f * vr * 2;
    ctx.beginPath(); ctx.moveTo(vx + vr - 4, y); ctx.lineTo(vx + vr + 2, y); ctx.stroke();
  }

  // 面具
  for (let i = 0; i < p.maxHp; i++) {
    const mx = 58 + i * 17, my = top + 8;
    drawMask(ctx, mx, my, i < p.hp, p.focusing && i === p.hp);
  }

  // 房间名（刚进入时）
  if (g.roomBanner && g.roomBanner.t < 2.2) {
    const a = Math.min(1, g.roomBanner.t * 3, (2.2 - g.roomBanner.t) * 2);
    ctx.globalAlpha = a;
    ctx.fillStyle = INK;
    ctx.font = `600 18px ${FONT}`;
    ctx.textAlign = 'center';
    ctx.fillText(g.roomBanner.name, W / 2, top + 64);
    ctx.fillRect(W / 2 - 30, top + 72, 60, 1.5);
    ctx.globalAlpha = 1;
  }
}

function drawMask(ctx, x, y, full, pulsing) {
  ctx.save();
  ctx.translate(x, y);
  const s = pulsing ? 1 + Math.sin(performance.now() / 80) * 0.08 : 1;
  ctx.scale(s, s);
  ctx.beginPath();
  ctx.moveTo(-6, -2);
  ctx.lineTo(-7, -9); ctx.lineTo(-3.5, -5);
  ctx.lineTo(3.5, -5); ctx.lineTo(7, -9); ctx.lineTo(6, -2);
  ctx.quadraticCurveTo(6, 9, 0, 10);
  ctx.quadraticCurveTo(-6, 9, -6, -2);
  ctx.closePath();
  ctx.lineWidth = 2; ctx.strokeStyle = INK;
  if (full) {
    ctx.fillStyle = INK; ctx.fill();
    ctx.fillStyle = PAPER;
    ctx.fillRect(-3.5, -1, 2, 4); ctx.fillRect(1.5, -1, 2, 4);
  } else {
    ctx.fillStyle = 'rgba(233,229,219,0.6)'; ctx.fill();
    ctx.globalAlpha = 0.6; ctx.stroke();
  }
  ctx.restore();
}

// 世界里的教学提示（当前房间）
export function drawHints(ctx, g, room) {
  if (!room || !room.hints) return;
  ctx.font = `13px ${FONT}`;
  ctx.textAlign = 'center';
  for (const h of room.hints) {
    const wx = room.px + h.x * 16, wy = room.py + h.y * 16;
    const [sx, sy] = g.worldToScreen(wx, wy);
    const d = Math.hypot(g.player.cx - wx, g.player.cy - wy);
    const a = Math.max(0.25, Math.min(0.85, 1.2 - d / 160));
    ctx.fillStyle = `rgba(17,17,17,${a})`;
    ctx.fillText(h.text, sx, sy);
  }
}

export function drawToasts(ctx, g, W, H) {
  // 获得能力的大横幅
  if (g.banner) {
    const b = g.banner;
    const a = Math.min(1, b.t * 2.5, (b.life - b.t) * 2);
    ctx.globalAlpha = Math.max(0, a);
    ctx.fillStyle = 'rgba(17,17,17,0.9)';
    const y = H * 0.36;
    ctx.fillRect(0, y - 46, W, 96);
    ctx.fillStyle = PAPER;
    ctx.textAlign = 'center';
    ctx.font = `12px ${FONT}`;
    ctx.fillText(b.kicker || '获得', W / 2, y - 22);
    ctx.font = `700 26px ${FONT}`;
    ctx.fillText(b.title, W / 2, y + 8);
    ctx.font = `14px ${FONT}`;
    ctx.fillText(b.sub || '', W / 2, y + 34);
    ctx.globalAlpha = 1;
  }
  // 底部小提示
  if (g.toast) {
    const t = g.toast;
    const a = Math.min(1, t.t * 4, (2.2 - t.t) * 3);
    ctx.globalAlpha = Math.max(0, a);
    ctx.font = `14px ${FONT}`;
    ctx.textAlign = 'center';
    const w = ctx.measureText(t.text).width + 28;
    ctx.fillStyle = INK;
    roundRect(ctx, W / 2 - w / 2, H - 96, w, 32, 16); ctx.fill();
    ctx.fillStyle = PAPER;
    ctx.fillText(t.text, W / 2, H - 75);
    ctx.globalAlpha = 1;
  }
}

// 手势反馈：轨迹残影、蓄力环、拖动锚点
export function drawGestures(ctx, input) {
  const now = performance.now();
  ctx.lineCap = 'round'; ctx.lineJoin = 'round';
  const trail = (pts, alpha) => {
    if (pts.length < 2) return;
    ctx.strokeStyle = `rgba(17,17,17,${alpha})`;
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (const p of pts) ctx.lineTo(p.x, p.y);
    ctx.stroke();
  };
  for (const tr of input.trails) trail(tr.pts, 0.22 * (1 - (now - tr.t) / 260));
  for (const t of input.touches.values()) {
    if (t.state === 'steer') {
      trail(t.full.filter(p => now - p.t < 200), 0.18);
      ctx.fillStyle = 'rgba(17,17,17,0.12)';
      ctx.beginPath(); ctx.arc(t.ax, t.y0, 22, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = 'rgba(17,17,17,0.3)';
      ctx.beginPath(); ctx.arc(t.x, t.y, 9, 0, Math.PI * 2); ctx.fill();
    }
  }
  for (const c of input.charges()) {
    const k1 = Math.min(1, (c.held - CFG.chargeShow) / (CFG.heavyTime - CFG.chargeShow));
    const k2 = Math.max(0, Math.min(1, (c.held - CFG.heavyTime) / (CFG.focusTime - CFG.heavyTime)));
    ctx.lineWidth = 4;
    ctx.strokeStyle = 'rgba(17,17,17,0.15)';
    ctx.beginPath(); ctx.arc(c.x, c.y, 34, 0, Math.PI * 2); ctx.stroke();
    ctx.strokeStyle = k1 >= 1 ? INK : 'rgba(17,17,17,0.6)';
    ctx.beginPath(); ctx.arc(c.x, c.y, 34, -Math.PI / 2, -Math.PI / 2 + k1 * Math.PI * 2); ctx.stroke();
    if (k2 > 0) {
      ctx.lineWidth = 2;
      ctx.strokeStyle = 'rgba(17,17,17,0.7)';
      ctx.beginPath(); ctx.arc(c.x, c.y, 44, -Math.PI / 2, -Math.PI / 2 + k2 * Math.PI * 2); ctx.stroke();
    }
    if (k1 >= 1) {
      ctx.fillStyle = INK;
      ctx.beginPath(); ctx.arc(c.x, c.y, 5, 0, Math.PI * 2); ctx.fill();
    }
  }
}

export function drawMap(ctx, g, W, H, safeTop) {
  ctx.fillStyle = 'rgba(233,229,219,0.96)';
  ctx.fillRect(0, 0, W, H);
  const w = g.world;
  const cell = Math.min((W - 40) / w.cellsW, (H * 0.6) / (w.cellsH * 2));
  const cw = cell, ch = cell * 2;
  const ox = (W - cw * w.cellsW) / 2, oy = safeTop + 90;
  ctx.fillStyle = INK;
  ctx.textAlign = 'center';
  ctx.font = `700 22px ${FONT}`;
  ctx.fillText('地 图', W / 2, safeTop + 50);
  ctx.font = `12px ${FONT}`;
  ctx.fillStyle = 'rgba(17,17,17,0.6)';
  ctx.fillText('双指轻点 关闭', W / 2, safeTop + 70);

  const benches = g.world.spawns.filter(s => s.type === 'B');
  for (const r of w.rooms) {
    if (!g.save.visited.includes(r.id)) continue;
    const x = ox + r.cx * cw + 3, y = oy + r.cy * ch + 3, rw = r.w * cw - 6, rh = r.h * ch - 6;
    ctx.fillStyle = r === g.room ? 'rgba(17,17,17,0.18)' : 'rgba(17,17,17,0.06)';
    ctx.fillRect(x, y, rw, rh);
    ctx.strokeStyle = INK; ctx.lineWidth = 2;
    ctx.strokeRect(x, y, rw, rh);
    ctx.fillStyle = INK;
    ctx.font = `12px ${FONT}`;
    ctx.fillText(r.name, x + rw / 2, y + 16);
    for (const b of benches) if (b.room === r.id) {
      const bx = ox + (b.x / 16 / CW) * cw, by = oy + (b.y / 16 / CH) * ch;
      ctx.fillRect(bx - 5, by - 4, 10, 2.5); ctx.fillRect(bx - 4, by - 2, 1.5, 3); ctx.fillRect(bx + 2.5, by - 2, 1.5, 3);
    }
  }
  const p = g.player;
  const px = ox + (p.cx / 16 / CW) * cw, py = oy + (p.cy / 16 / CH) * ch;
  const pulse = 4 + Math.sin(performance.now() / 150) * 1.5;
  ctx.fillStyle = INK;
  ctx.beginPath(); ctx.arc(px, py, pulse, 0, Math.PI * 2); ctx.fill();

  // 能力一览
  const ab = g.save.abilities;
  const lines = [
    ['挥砍', '轻点（点哪砍哪）', true],
    ['跳跃', '向上快划', true],
    ['重击', '长按后松开', true],
    ['凝神', '长按不放（33 墨）', true],
    ['下砸', '空中向下快划', true],
    ['疾影', '左 / 右快划', ab.dash],
    ['墨旋', '画一个圈', ab.vortex],
    ['墨弹', '蓄力后甩出', ab.bolt],
  ];
  let y = oy + ch * w.cellsH + 40;
  ctx.textAlign = 'left';
  for (const [name, how, has] of lines) {
    ctx.fillStyle = has ? INK : 'rgba(17,17,17,0.3)';
    ctx.font = `600 14px ${FONT}`;
    ctx.fillText(has ? name : '？？', W / 2 - 110, y);
    ctx.font = `14px ${FONT}`;
    ctx.fillText(has ? how : '未解锁', W / 2 - 50, y);
    y += 24;
  }
}

export function drawTitle(ctx, W, H, t) {
  ctx.fillStyle = PAPER;
  ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = INK;
  // 墨团
  ctx.beginPath();
  for (let i = 0; i <= 40; i++) {
    const a = i / 40 * Math.PI * 2;
    const r = 70 + Math.sin(a * 5 + t) * 6 + Math.sin(a * 3 - t * 0.7) * 5;
    const x = W / 2 + Math.cos(a) * r, y = H * 0.36 + Math.sin(a) * r;
    i ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
  }
  ctx.fill();
  ctx.fillStyle = PAPER;
  ctx.textAlign = 'center';
  ctx.font = `700 30px ${FONT}`;
  ctx.fillText('墨影骑士', W / 2, H * 0.36 + 4);
  ctx.font = `12px ${FONT}`;
  ctx.fillText('INK  KNIGHT', W / 2, H * 0.36 + 26);
  ctx.fillStyle = INK;
  ctx.globalAlpha = 0.5 + Math.sin(t * 3) * 0.3;
  ctx.font = `16px ${FONT}`;
  ctx.fillText('轻点开始', W / 2, H * 0.68);
  ctx.globalAlpha = 0.5;
  ctx.font = `12px ${FONT}`;
  ctx.fillText('全程手势操作 · 建议竖屏', W / 2, H * 0.68 + 26);
  ctx.globalAlpha = 1;
}

function roundRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}
