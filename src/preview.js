// CPU-only composite of a level, used for the thumbnails in the map list. Port of Render/Preview.Level.
import { K, C } from './core.js';
import { Pix, PixelFont } from './gfx.js';
import { PP, Sprites, LevelArt } from './art.js';
import { World, emptyInput } from './world.js';

const P = 16;

export function levelPreview(w) {
  w.update(K.SimDt, emptyInput());
  const art = new LevelArt(w);
  const o = new Pix(art.back.w, art.back.h);
  o.draw(art.back, 0, 0);
  if (w.hasExit) {
    const ex = w.exit.x * P + P / 2 - 14, ey = (w.exit.y + 1) * P - 44;
    o.draw(Sprites.exitDoor, ex, ey);
    o.rect(ex + 1, ey - 12, 26, 10, C(20, 24, 28));
    PixelFont.drawPix(o, 'EXIT', ex + 3, ey - 10, 1, PP.Exit);
  }
  for (const b of w.buttons) {
    const cx = b.x * P + P / 2, fy = (b.y + 1) * P;
    o.rect(cx - 14, fy - 3, 28, 3, C(43, 47, 58));
    o.hLine(cx - 14, fy - 3, 28, C(74, 81, 99));
    o.rect(cx - 9, fy - 7, 18, 4, C(217, 69, 58));
    o.hLine(cx - 8, fy - 7, 16, C(255, 138, 122));
  }
  for (const c of w.cubes) o.draw(Sprites.cube, Math.trunc(c.pos.x * K.Px) - 8, Math.trunc(c.pos.y * K.Px) - 8);
  if (w.player) {
    const frame = new Pix(16, 20);
    for (let y = 0; y < 20; y++) for (let x = 0; x < 16; x++) frame.set(x, y, Sprites.playerSheet.get(x, y));
    o.draw(frame, Math.trunc(w.player.pos.x * K.Px) - 8, Math.trunc((w.player.pos.y + w.player.half.y) * K.Px) - 20);
  }
  o.draw(art.front, 0, 0);
  for (let ty = 0; ty < w.h; ty++) for (let tx = 0; tx < w.w; tx++) {
    const ch = w.at(tx, ty), ox = tx * P, oy = ty * P;
    if (ch === '~') {
      const surf = w.at(tx, ty - 1) !== '~';
      for (let x = 0; x < P; x++) {
        const top = surf ? 3 + Math.round(Math.sin((ox + x) * 0.45) * 1.2) : 0;
        for (let y = top; y < P; y++) {
          const col = !surf ? PP.Goo4 : y === top ? PP.GooTop : y === top + 1 ? PP.Goo1 : y < 9 ? PP.Goo2 : PP.Goo3;
          o.set(ox + x, oy + y, col);
        }
      }
    } else if (ch === '|') {
      o.blendRect(ox + 3, oy, 10, P, C(42, 111, 191, 60));
      for (const lx of [4, 8, 12]) o.blendRect(ox + lx, oy, 1, P, C(143, 211, 255, 150));
    } else if (World.isDoor(ch)) {
      o.rect(ox + 1, oy, 14, P, C(29, 31, 38));
      o.rect(ox + 3, oy, 10, P, C(93, 102, 124));
      o.vLine(ox + 4, oy, P, C(122, 131, 153));
      for (let y = 3; y < P; y += 4) o.hLine(ox + 3, oy + y, 10, C(70, 76, 92));
    }
  }
  for (const s of w.lasers) {
    const ax = Math.trunc(Math.min(s.a.x, s.b.x) * K.Px), ay = Math.trunc(Math.min(s.a.y, s.b.y) * K.Px);
    const bx = Math.trunc(Math.max(s.a.x, s.b.x) * K.Px), by = Math.trunc(Math.max(s.a.y, s.b.y) * K.Px);
    if (ay === by) { o.rect(ax, ay - 1, bx - ax, 3, PP.Laser); o.hLine(ax, ay, bx - ax, PP.LaserCore); }
    else { o.rect(ax - 1, ay, 3, by - ay, PP.Laser); o.vLine(ax, ay, by - ay, PP.LaserCore); }
  }
  return o;
}
