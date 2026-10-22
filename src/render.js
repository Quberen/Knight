// 地形缓存 + 远景
import { T } from './world.js';
import { INK, PAPER } from './fx.js';

export const RES = 2; // 游戏缓冲区相对逻辑像素的倍数

// 确定性随机（同一格每次画出来一样）
function hash(x, y, s = 0) {
  let h = (x * 374761393 + y * 668265263 + s * 2147483647) | 0;
  h = (h ^ (h >>> 13)) * 1274126177 | 0;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

export function buildTileCache(world) {
  const c = document.createElement('canvas');
  c.width = world.tw * T * RES; c.height = world.th * T * RES;
  const ctx = c.getContext('2d');
  ctx.scale(RES, RES);
  paintTiles(ctx, world, 0, 0, world.tw, world.th);
  return c;
}

// 局部重绘（裂墙碎掉后）
export function repaintTiles(cache, world, x0, y0, x1, y1) {
  const ctx = cache.getContext('2d');
  ctx.save();
  ctx.setTransform(RES, 0, 0, RES, 0, 0);
  const pad = 1;
  ctx.clearRect((x0 - pad) * T, (y0 - pad) * T, (x1 - x0 + 1 + pad * 2) * T, (y1 - y0 + 1 + pad * 2) * T);
  ctx.beginPath();
  ctx.rect((x0 - pad) * T, (y0 - pad) * T, (x1 - x0 + 1 + pad * 2) * T, (y1 - y0 + 1 + pad * 2) * T);
  ctx.clip();
  paintTiles(ctx, world, x0 - pad - 1, y0 - pad - 1, x1 + pad + 2, y1 + pad + 2);
  ctx.restore();
}

function paintTiles(ctx, w, x0, y0, x1, y1) {
  const air = (x, y) => !w.solid(x, y);
  for (let ty = Math.max(0, y0); ty < Math.min(w.th, y1); ty++) {
    for (let tx = Math.max(0, x0); tx < Math.min(w.tw, x1); tx++) {
      const ch = w.tile(tx, ty);
      const px = tx * T, py = ty * T;
      if (ch === '#' || ch === 'X') {
        ctx.fillStyle = INK;
        ctx.fillRect(px, py, T, T);
        // 上表面：草和毛刺
        if (air(tx, ty - 1)) {
          for (let i = 0; i < 6; i++) {
            const r = hash(tx, ty, i);
            const gx = px + r * T, gh = 1 + hash(tx, ty, i + 9) * 3.5;
            ctx.beginPath();
            ctx.moveTo(gx - 1, py + 0.5);
            ctx.lineTo(gx + (hash(tx, ty, i + 3) - 0.5) * 2, py - gh);
            ctx.lineTo(gx + 1, py + 0.5);
            ctx.fill();
          }
        }
        // 下表面：垂下的墨
        if (air(tx, ty + 1) && hash(tx, ty, 21) < 0.5) {
          const gx = px + 3 + hash(tx, ty, 22) * (T - 6), gh = 2 + hash(tx, ty, 23) * 4;
          ctx.beginPath();
          ctx.moveTo(gx - 1.5, py + T); ctx.lineTo(gx, py + T + gh); ctx.lineTo(gx + 1.5, py + T);
          ctx.fill();
        }
        // 侧面：凹凸
        for (const [dx, side] of [[-1, 0], [1, T]]) {
          if (air(tx + dx, ty)) {
            for (let i = 0; i < 2; i++) {
              const gy = py + 2 + hash(tx, ty, 30 + i + dx) * (T - 4);
              const s = 0.8 + hash(tx, ty, 40 + i + dx) * 1.6;
              ctx.fillRect(px + side - (dx < 0 ? s : 0), gy, s, 2 + hash(tx, ty, 50 + i) * 3);
            }
          }
        }
        if (ch === 'X') {
          ctx.strokeStyle = PAPER; ctx.lineWidth = 0.8;
          ctx.beginPath();
          const r = hash(tx, ty, 60);
          ctx.moveTo(px + 3 + r * 4, py);
          ctx.lineTo(px + 8, py + 6 + r * 3);
          ctx.lineTo(px + 5, py + 10);
          ctx.lineTo(px + 9 + r * 3, py + T);
          ctx.moveTo(px + 8, py + 6 + r * 3);
          ctx.lineTo(px + 13, py + 5);
          ctx.stroke();
        }
      } else if (ch === '=') {
        ctx.fillStyle = INK;
        ctx.fillRect(px, py, T, 3);
        if (!w.oneWay(tx - 1, ty) || !w.oneWay(tx + 1, ty) || hash(tx, ty, 70) < 0.3) {
          ctx.fillRect(px + 4, py + 3, 1, 4 + hash(tx, ty, 71) * 3);
          ctx.fillRect(px + 11, py + 3, 1, 2 + hash(tx, ty, 72) * 3);
        }
        ctx.fillStyle = PAPER;
        ctx.fillRect(px + 7 + hash(tx, ty, 73) * 3, py + 1, 1, 1);
      } else if (ch === '^') {
        ctx.fillStyle = INK;
        for (let i = 0; i < 4; i++) {
          const sx = px + i * 4, h = 6 + hash(tx, ty, 80 + i) * 4;
          ctx.beginPath();
          ctx.moveTo(sx, py + T); ctx.lineTo(sx + 2, py + T - h); ctx.lineTo(sx + 4, py + T);
          ctx.fill();
        }
      }
    }
  }
}

// 每个房间的远景：淡灰色的柱子、拱门和垂下的藤
const bgCache = new Map();
function roomBg(room) {
  if (bgCache.has(room.id)) return bgCache.get(room.id);
  const shapes = [];
  const seed = [...room.id].reduce((a, c) => a + c.charCodeAt(0), 0);
  const n = Math.ceil(room.pw / 60) + 2;
  for (let i = 0; i < n; i++) {
    const r = hash(i, seed, 1);
    shapes.push({
      kind: r < 0.55 ? 'pillar' : r < 0.8 ? 'arch' : 'vine',
      x: i * 60 + hash(i, seed, 2) * 40 - 20,
      w: 8 + hash(i, seed, 3) * 14,
      top: hash(i, seed, 4) * room.ph * 0.4,
      h: room.ph * (0.5 + hash(i, seed, 5) * 0.6),
      depth: hash(i, seed, 6) < 0.5 ? 0.35 : 0.6,
    });
  }
  bgCache.set(room.id, shapes);
  return shapes;
}

// 世界外是墨；视口内每个房间各自铺纸色和远景（换房间时相机平移也不会穿帮）
export function drawBackground(ctx, rooms, camX, camY, vw, vh) {
  ctx.fillStyle = INK;
  ctx.fillRect(camX, camY, vw, vh);
  for (const room of rooms) {
    if (room.px > camX + vw || room.px + room.pw < camX || room.py > camY + vh || room.py + room.ph < camY) continue;
    drawRoomBg(ctx, room, camX, camY);
  }
}

function drawRoomBg(ctx, room, camX, camY) {
  ctx.save();
  ctx.beginPath();
  ctx.rect(room.px, room.py, room.pw, room.ph);
  ctx.clip();
  ctx.fillStyle = PAPER;
  ctx.fillRect(room.px, room.py, room.pw, room.ph);
  const shapes = roomBg(room);
  for (const s of shapes) {
    const k = s.depth;
    // 视差：相对房间原点按 k 比例平移
    const ox = room.px + (camX - room.px) * (1 - k);
    const oy = room.py + (camY - room.py) * (1 - k);
    ctx.fillStyle = k < 0.5 ? '#d9d4c8' : '#cbc5b8';
    const x = ox + s.x * (0.6 + k * 0.4), y = oy + s.top;
    if (s.kind === 'pillar') {
      ctx.fillRect(x, y, s.w, s.h);
      ctx.fillRect(x - 3, y, s.w + 6, 3);
    } else if (s.kind === 'arch') {
      ctx.beginPath();
      ctx.arc(x + 20, y + 30, 22, Math.PI, 0);
      ctx.lineTo(x + 42, y + s.h); ctx.lineTo(x + 36, y + s.h); ctx.lineTo(x + 36, y + 30);
      ctx.arc(x + 20, y + 30, 16, 0, Math.PI, true);
      ctx.lineTo(x + 4, y + s.h); ctx.lineTo(x - 2, y + s.h);
      ctx.closePath();
      ctx.fill();
    } else {
      ctx.fillRect(x, room.py, 1.5, s.top + 40);
      ctx.beginPath(); ctx.arc(x + 0.75, room.py + s.top + 42, 3, 0, Math.PI * 2); ctx.fill();
    }
  }
  ctx.restore();
}

export function drawVignette(ctx, w, h) {
  const g = ctx.createRadialGradient(w / 2, h / 2, Math.min(w, h) * 0.35, w / 2, h / 2, Math.max(w, h) * 0.75);
  g.addColorStop(0, 'rgba(17,17,17,0)');
  g.addColorStop(1, 'rgba(17,17,17,0.35)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);
}
