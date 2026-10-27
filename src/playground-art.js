// Outdoor layers are drawn only in the visible region, including views through portals.
import { K, C, V, hash, mix } from './core.js';
import { PixelFont } from './gfx.js';
const T = K.Tile;

export function drawPlayground(r, w, view, drawPortals) {
  const g = r.gfx, day = w.daylight, time = w.time;
  const x0 = Math.floor(view.x / T), x1 = Math.ceil((view.x + view.w) / T);
  const y0 = Math.max(0, Math.floor(view.y / T)), y1 = Math.min(w.h - 1, Math.ceil((view.y + view.h) / T));
  const chunks = w.chunksFor({ x: view.x - 180, w: view.w + 360 });
  const sky = mix(C(11, 18, 46), C(91, 174, 215), day);
  g.rectR(view, sky);
  g.rectGradV(view.x, 0, view.w, 1050, sky, mix(C(45, 42, 72), C(215, 231, 186), day));
  if (day < 0.9) for (let i = 0; i < 65; i++) {
    const span = 1600, sx = hash(i, 1, w.seed) * span;
    const x = Math.floor((view.x * 0.08) / span) * span + sx - view.x * 0.08 + view.x;
    const y = 220 + hash(i, 2, w.seed) * 420;
    g.rect(x, y, i % 7 === 0 ? 4 : 2, 2, C(225, 235, 255, (1 - day) * (150 + 90 * Math.sin(i + time * 0.5))));
  }
  const phase = (w.clock - 6) / 12 * Math.PI;
  const sunX = view.x + view.w * (0.5 - Math.cos(phase) * 0.38);
  const sunY = 750 - Math.abs(Math.sin(phase)) * 400;
  g.circleGrad(sunX, sunY, 110, C(255, 221, 139, day * 85), C(255, 221, 139, 0));
  g.rect(sunX - 20, sunY - 24, 40, 48, mix(C(216, 230, 246), C(255, 240, 172), day));
  g.rect(sunX - 26, sunY - 16, 52, 32, mix(C(216, 230, 246), C(255, 240, 172), day));
  for (let layer = 0; layer < 3; layer++) {
    const col = mix(C(25 + layer * 5, 35 + layer * 5, 61), C(104 - layer * 21, 155 - layer * 9, 143 - layer * 23), day);
    for (let x = x0 - 2; x <= x1 + 2; x++) {
      const ax = x * T, u = ax + view.x * (0.12 + layer * 0.14);
      const y = 650 + layer * 60 + Math.sin(u * 0.002 + layer * 4) * 64 + Math.sin(u * 0.005) * 22;
      g.rect(ax, Math.round(y / 4) * 4, T, 1200, col);
    }
  }
  for (let i = Math.floor((view.x + time * 9) / 460) - 1; i <= Math.ceil((view.x + view.w + time * 9) / 460); i++) {
    const x = i * 460 - time * 9, y = 410 + hash(i, 3, w.seed) * 130;
    const cloud = mix(C(69, 73, 103, 100), C(245, 248, 224, 190), day);
    g.rect(x, y, 150, 20, cloud); g.rect(x + 28, y - 18, 90, 24, cloud); g.rect(x + 50, y - 28, 42, 16, cloud);
  }
  // Earth behind the playable cave spaces; the solid terrain is drawn after the actors.
  for (let x = x0; x <= x1; x++) {
    const ch = w.chunk(Math.floor(x / 80)), top = ch.surface[x - ch.start];
    g.rect(x * T, top * T + 10, T, Math.max(0, view.y + view.h - top * T), C(28, 31, 35));
  }
  for (const ch of chunks) {
    const bx = (ch.start + 8) * T, by = (ch.base - ch.houseH) * T;
    g.rect(bx, by, ch.houseW * T, ch.houseH * T, C(68, 91, 100));
    for (let x = bx + 2 * T; x < bx + (ch.houseW - 2) * T; x += 3 * T) {
      g.rect(x - 4, by + 2 * T - 4, 48, 66, C(34, 51, 64));
      g.rect(x, by + 2 * T, 40, 58, mix(C(249, 195, 98), C(135, 201, 213), day));
      g.rect(x + 18, by + 2 * T, 4, 58, C(45, 65, 72)); g.rect(x, by + 3 * T, 40, 4, C(45, 65, 72));
    }
    PixelFont.draw(g, 'FIELD STATION', V(bx + 48, ch.base * T - 100), 1, C(229, 224, 192));
    g.rect((ch.start + 29) * T, ch.base * T - 42, 7 * T, 6, C(232, 174, 76));
    PixelFont.draw(g, 'LAB / LIFT', V((ch.start + 29) * T, ch.base * T - 66), 1, C(244, 224, 166));
    g.rect((ch.start + 7) * T, (ch.roomY + 1) * T, 38 * T, 13 * T, C(42, 53, 64));
    for (let x = ch.start + 7; x < ch.start + 45; x += 2) {
      g.rect(x * T, (ch.roomY + 1) * T, 2, 13 * T, C(58, 70, 79));
      g.rect(x * T, (ch.roomY + 11) * T, T, 6, C(77, 88, 93));
    }
    PixelFont.draw(g, `SUBSURFACE ${Math.abs(ch.index) + 1}`, V((ch.start + 9) * T, (ch.roomY + 3) * T), 2, C(123, 164, 178));
    for (const tree of ch.trees) {
      const x = tree.x * T, y = tree.y * T, h = tree.size * T;
      g.rect(x + 10, y - h, 14, h, C(105, 77, 52)); g.rect(x + 14, y - h, 4, h, C(138, 99, 58));
      g.rect(x - 42, y - h - 36, 120, 54, C(46, 104, 66));
      g.rect(x - 26, y - h - 64, 88, 42, C(63, 135, 73));
      g.rect(x - 6, y - h - 80, 50, 36, C(88, 153, 78));
    }
  }
  for (const c of w.cubes) r.drawWithClones(w, c, () => r.drawCube(c));
  r.drawWithClones(w, w.player, () => r.drawPlayer(w.player));
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
    const tile = w.at(x, y), px = x * T, py = y * T;
    if (!w.isSolid(x, y)) continue;
    const n = hash(x, y, w.seed), exposed = !w.isSolid(x, y - 1);
    const ch = w.chunk(Math.floor(x / 80));
    if (tile === 'X') {
      const soil = y < ch.surface[x - ch.start] + 4;
      g.rect(px, py, T, T, soil ? C(111 + n * 15, 78 + n * 12, 51) : C(66 + n * 13, 69 + n * 12, 72 + n * 10));
      g.rect(px + 4 + Math.floor(n * 8) * 2, py + 8, 8, 4, soil ? C(138, 99, 60) : C(86, 92, 94));
      if (exposed && y === ch.surface[x - ch.start]) {
        g.rect(px, py, T, 6, C(107, 177, 71)); g.rect(px, py + 6, T, 4, C(65, 129, 57));
        g.rect(px + 6, py - 6, 2, 8, C(105, 165, 66)); g.rect(px + 22, py - 4, 4, 6, C(126, 180, 70));
        if (n > 0.8) { g.rect(px + 12, py - 10, 2, 10, C(74, 137, 60)); g.rect(px + 10, py - 12, 6, 4, C(244, 212, 125)); }
      } else if (exposed) g.rect(px, py, T, 3, C(105, 116, 117));
    } else {
      const roof = tile === '#' && y === ch.base - ch.houseH && x >= ch.start + 8 && x < ch.start + 8 + ch.houseW;
      const col = roof ? C(159, 87, 61) : tile === 'J' ? C(64, 146, 232) : tile === 'G' ? C(245, 151, 51) : tile === 'Q' ? C(217, 191, 78) : C(182, 191, 183);
      g.rect(px, py, T, T, col); g.rect(px + 2, py + 2, T - 4, 2, C(228, 229, 208, 110));
      g.rect(px + T - 2, py, 2, T, C(64, 79, 82, 100)); g.rect(px, py + T - 2, T, 2, C(64, 79, 82, 100));
    }
  }
  r.applyLighting(w, view, x0, y0, x1, y1);
  r.drawLampFixtures(w, view);
  for (const b of w.beams) r.drawBeam(w, b);
  // Translucency tints submerged actors without treating blue water as the test chambers' toxic goo.
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) if (w.at(x, y) === ':') {
    g.rect(x * T, y * T, T, T, C(39, 133, 175, 115));
    if (w.at(x, y - 1) !== ':') {
      const wave = Math.round(Math.sin(x * 0.7 + time * 2) * 2) * 2;
      g.rect(x * T, y * T + wave, T, 4, C(163, 226, 224, 190));
      g.rect(x * T + 6, y * T + 10 + wave, 12, 2, C(171, 229, 233, 100));
    }
  }
  r.drawParticles(w);
  for (const p of w.fading) r.drawPortal(p, w.linked);
  if (drawPortals) for (const p of w.portals) if (p) r.drawPortal(p, w.linked);
}
