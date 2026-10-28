// Playground rendering: sky, parallax hills, biome terrain, props, relics, lava and water. Everything is drawn only in
// the visible region (including the views seen through portals).
import { K, C, V, hash, mix, withAlpha } from './core.js';
import { PixelFont } from './gfx.js';
import { CHUNK_W, SEA } from './playground.js';
const T = K.Tile;

const GROUND = {
  // [grass top, grass edge, soil, soil speck]
  wetland: [C(88, 150, 96), C(52, 110, 76), C(86, 76, 58), C(110, 98, 74)],
  meadow: [C(107, 177, 71), C(65, 129, 57), C(111, 78, 51), C(138, 99, 60)],
  forest: [C(72, 142, 62), C(44, 100, 52), C(96, 70, 46), C(122, 90, 56)],
  desert: [C(226, 196, 124), C(200, 162, 96), C(196, 156, 96), C(220, 182, 118)],
  mountain: [C(128, 134, 144), C(96, 102, 112), C(92, 96, 104), C(118, 124, 134)],
};
const kindOf = (b) => (b < 0.25 ? 'wetland' : b < 0.43 ? 'meadow' : b < 0.6 ? 'forest' : b < 0.76 ? 'desert' : 'mountain');

export function drawPlayground(r, w, view, drawPortals) {
  const g = r.gfx, day = w.daylight, time = w.time;
  const x0 = Math.floor(view.x / T), x1 = Math.ceil((view.x + view.w) / T);
  const y0 = Math.max(0, Math.floor(view.y / T)), y1 = Math.min(w.h - 1, Math.ceil((view.y + view.h) / T));
  const chunks = w.chunksFor({ x: view.x - 240, w: view.w + 480 });
  r.extraLights = w.glowLights(view);
  const colAt = (ax) => { const ch = w.chunk(Math.floor(ax / CHUNK_W)); const i = ax - ch.start; return { ch, i }; };

  // ---- sky
  const sky = mix(C(11, 18, 46), C(91, 174, 215), day);
  g.rectR(view, sky);
  g.rectGradV(view.x, 0, view.w, 1250, sky, mix(C(45, 42, 72), C(222, 233, 190), day));
  if (day < 0.9) for (let i = 0; i < 70; i++) {
    const span = 1600, sx = hash(i, 1, w.seed) * span;
    const x = Math.floor((view.x * 0.08) / span) * span + sx - view.x * 0.08 + view.x;
    g.rect(x, 140 + hash(i, 2, w.seed) * 520, i % 7 === 0 ? 4 : 2, 2, C(225, 235, 255, (1 - day) * (150 + 90 * Math.sin(i + time * 0.5))));
  }
  const phase = (w.clock - 6) / 12 * Math.PI;
  const sunX = view.x + view.w * (0.5 - Math.cos(phase) * 0.38), sunY = 820 - Math.abs(Math.sin(phase)) * 520;
  g.circleGrad(sunX, sunY, 130, C(255, 221, 139, day * 95), C(255, 221, 139, 0));
  g.rect(sunX - 20, sunY - 24, 40, 48, mix(C(216, 230, 246), C(255, 240, 172), day));
  g.rect(sunX - 26, sunY - 16, 52, 32, mix(C(216, 230, 246), C(255, 240, 172), day));
  // far ranges with a snow line, three layers of parallax
  for (let layer = 0; layer < 3; layer++) {
    const col = mix(C(25 + layer * 5, 35 + layer * 5, 61), C(112 - layer * 22, 150 - layer * 8, 156 - layer * 22), day);
    const snow = mix(C(120, 130, 160), C(244, 248, 252), day);
    for (let x = x0 - 2; x <= x1 + 2; x++) {
      const ax = x * T, u = ax + view.x * (0.12 + layer * 0.14);
      const y = Math.round((620 + layer * 70 + Math.sin(u * 0.002 + layer * 4) * 70 + Math.sin(u * 0.0057 + layer) * 28 + Math.sin(u * 0.0013) * 40) / 4) * 4;
      g.rect(ax, y, T, 1400, col);
      if (layer === 0 && y < 560) g.rect(ax, y, T, Math.min(24, 580 - y), snow);
    }
  }
  for (let i = Math.floor((view.x + time * 9) / 460) - 1; i <= Math.ceil((view.x + view.w + time * 9) / 460); i++) {
    const x = i * 460 - time * 9, y = 330 + hash(i, 3, w.seed) * 220;
    const cloud = mix(C(69, 73, 103, 100), C(246, 249, 226, 200), day);
    g.rect(x, y, 150, 20, cloud); g.rect(x + 28, y - 18, 90, 24, cloud); g.rect(x + 50, y - 28, 42, 16, cloud);
  }

  // ---- earth behind the playable cave spaces (darker with depth), then the structures' back walls
  for (let x = x0; x <= x1; x++) {
    const { ch, i } = colAt(x), top = ch.surface[i];
    const y = top * T + 10, h = Math.max(0, view.y + view.h - y);
    if (h > 0) { g.rect(x * T, y, T, h, C(30, 33, 37)); g.rectGradV(x * T, y, T, Math.min(h, 640), C(0, 0, 0, 0), C(0, 0, 0, 90)); }
  }
  for (const ch of chunks) {
    for (const s of ch.structures) {
      if (s.kind !== 'lab') continue;
      const bx = (ch.start + s.x0) * T, by = (s.fy - 8) * T;
      g.rect(bx, by, 18 * T, 8 * T, C(62, 84, 94));
      for (let x = bx + 2 * T; x < bx + 16 * T; x += 4 * T) {
        g.rect(x, by + 2 * T, 46, 64, C(34, 51, 64));
        g.rect(x + 3, by + 2 * T + 3, 40, 58, mix(C(249, 195, 98), C(135, 201, 213), day));
        g.rect(x + 21, by + 2 * T + 3, 4, 58, C(45, 65, 72));
      }
    }
    for (const c of ch.chambers) g.circleGrad(c.x * T, c.y * T, c.rx * T * 1.2, C(40, 56, 80, 70), C(40, 56, 80, 0));
  }
  // ---- props behind the actors
  for (const ch of chunks) {
    for (const p of ch.props) {
      const x = p.x * T, y = p.y * T, sway = Math.sin(time * 1.6 + p.x * 0.3) * w.wind * 8;
      if (p.kind === 'tree') {
        const h = p.size * T, forest = ch.biomes[Math.max(0, Math.min(79, p.x - ch.start))] >= 0.43;
        const leaf = forest ? C(46, 112, 62) : C(88, 153, 78), hi = forest ? C(66, 142, 76) : C(116, 178, 94);
        g.rect(x + 10 + sway * 0.3, y - h, 14, h, C(105, 77, 52)); g.rect(x + 14 + sway * 0.3, y - h, 4, h, C(138, 99, 58));
        g.rect(x - 42 + sway, y - h - 36, 120, 54, leaf); g.rect(x - 26 + sway, y - h - 64, 88, 42, hi); g.rect(x - 6 + sway, y - h - 80, 50, 36, leaf);
      } else if (p.kind === 'pine') {
        const h = p.size * T;
        g.rect(x + 12, y - h * 0.4, 8, h * 0.4, C(90, 66, 48));
        for (let k = 0; k < 5; k++) { const wdt = 74 - k * 13; g.rect(x + 16 - wdt / 2 + sway * (k / 5), y - h * 0.4 - k * (h / 7) - 24, wdt, 26, k % 2 ? C(40, 86, 70) : C(54, 106, 82)); }
        g.rect(x + 10 + sway, y - h * 0.4 - 5 * (h / 7) - 26, 12, 20, C(236, 244, 250));
      } else if (p.kind === 'cactus') {
        const h = p.size * T * 0.8;
        g.rect(x + 8, y - h, 14, h, C(70, 140, 82)); g.rect(x + 10, y - h, 4, h, C(96, 168, 98));
        if (p.size > 2) { g.rect(x - 6, y - h * 0.65, 14, 8, C(70, 140, 82)); g.rect(x - 6, y - h * 0.65 - 18, 8, 22, C(70, 140, 82)); g.rect(x + 22, y - h * 0.5, 14, 8, C(70, 140, 82)); g.rect(x + 28, y - h * 0.5 - 16, 8, 20, C(70, 140, 82)); }
      } else if (p.kind === 'reed') {
        for (let k = 0; k < 3; k++) { const hh = p.size * 14 + k * 4; g.rect(x + 6 + k * 8 + sway * 0.5, y - hh, 3, hh, C(96, 130, 74)); g.rect(x + 5 + k * 8 + sway * 0.5, y - hh - 6, 5, 8, C(120, 90, 56)); }
      } else if (p.kind === 'flower') {
        const fc = Math.floor(hash(p.x, p.y, w.seed) * 3) === 0 ? C(244, 110, 170) : Math.floor(hash(p.x, p.y, w.seed * 2) * 2) === 0 ? C(246, 196, 60) : C(255, 255, 255);
        g.rect(x + 14, y - 4, 2, 4, C(105, 77, 52)); g.rect(x + 12, y - 6, 4, 2, fc); g.rect(x + 14, y - 8, 2, 2, fc);
      } else if (p.kind === 'rock') {
        const s = p.size * T * 0.7;
        g.rect(x + 16 - s / 2, y - s, s, s, C(74, 78, 90)); g.rect(x + 18 - s / 2, y - s + 2, s - 4, 2, C(96, 102, 116));
      } else if (p.kind === 'mushroom') {
        const h = p.size * 10;
        g.rect(x + 14, y - h, 4, h, C(220, 210, 190)); g.rect(x + 6, y - h - 8, 20, 8, C(210, 70, 90)); g.rect(x + 10, y - h - 10, 4, 4, C(255, 240, 240));
      }
    }
    for (const s of ch.signs) {
      const m = PixelFont.measure(s.text, 1), x = (s.x + 0.5) * T - m.x / 2 - 6, y = s.y * T - 22;
      g.rect(x + m.x / 2 + 4, y + 18, 4, s.y * T - y - 18, C(105, 77, 52));
      g.rect(x, y, m.x + 12, 20, C(138, 99, 58)); g.rect(x, y, m.x + 12, 2, C(168, 124, 76)); g.rect(x, y + 18, m.x + 12, 2, C(86, 62, 40));
      PixelFont.draw(g, s.text, V(x + 6, y + 6), 1, C(40, 28, 18));
    }
  }
  for (const c of w.cubes) r.drawWithClones(w, c, () => r.drawCube(c));
  r.drawWithClones(w, w.player, () => r.drawPlayer(w.player));

  // ---- terrain and panels
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
    const tile = w.at(x, y), px = x * T, py = y * T;
    if (tile === ' ' || tile === ':' || tile === '~') continue;
    const n = hash(x, y, w.seed), exposed = w.at(x, y - 1) === ' ' || w.at(x, y - 1) === ':';
    const { ch, i } = colAt(x), surfY = ch.surface[i];
    if (tile === 'X') {
      const kind = kindOf(ch.biomes[i]), pal = GROUND[kind];
      const depth = y - surfY, soil = depth < 4;
      const base = soil ? pal[2] : C(64 + n * 12, 67 + n * 11, 72 + n * 10);
      g.rect(px, py, T, T, C(base.r + n * 12, base.g + n * 10, base.b + n * 8));
      g.rect(px + 4 + Math.floor(n * 8) * 2, py + 8, 8, 4, soil ? pal[3] : C(84, 90, 94));
      if (!soil && n > 0.93) g.rect(px + 10, py + 14, 6, 6, C(120, 150, 190));          // a vein of crystal ore
      if (exposed && y === surfY) {
        const snowy = kind === 'mountain' && y < 24;
        g.rect(px, py, T, 6, snowy ? C(240, 246, 252) : pal[0]); g.rect(px, py + 6, T, 4, snowy ? C(200, 214, 230) : pal[1]);
        if (kind === 'meadow' || kind === 'forest') { g.rect(px + 6, py - 6, 2, 8, pal[0]); g.rect(px + 22, py - 4, 4, 6, pal[0]); }
        if (kind === 'wetland') g.rect(px + 12, py - 4, 4, 6, C(70, 120, 84));
      } else if (exposed) g.rect(px, py, T, 3, C(105, 116, 117));
    } else if (tile === 'B') {
      g.rect(px, py, T, T, C(46, 52, 64)); g.rect(px + 2, py + 2, T - 4, 6, PP_LIFT); g.rect(px + 6, py + 4, T - 12, 3, C(190, 230, 255));
      g.rect(px + 2, py + T - 4, T - 4, 4, C(30, 34, 42));
    } else {
      const col = tile === 'J' ? C(64, 146, 232) : tile === 'G' ? C(245, 151, 51) : tile === 'Q' ? C(217, 191, 78) : C(182, 191, 183);
      g.rect(px, py, T, T, col); g.rect(px + 2, py + 2, T - 4, 2, C(228, 229, 208, 110));
      g.rect(px + T - 2, py, 2, T, C(64, 79, 82, 100)); g.rect(px, py + T - 2, T, 2, C(64, 79, 82, 100));
      if (tile === 'J' || tile === 'G') { g.rect(px + 6, py + 8, 4, 4, C(255, 255, 255, 120)); g.rect(px + 18, py + 14, 4, 4, C(255, 255, 255, 90)); }
      if (tile === '#' && n > 0.8) g.rect(px + 8, py + 8, 6, 2, C(120, 130, 125, 120));
    }
  }
  r.applyLighting(w, view, x0, y0, x1, y1);
  r.drawLampFixtures(w, view);
  for (const b of w.beams) r.drawBeam(w, b);

  // ---- emissive: lava, crystals, relics
  g.additive = false;
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
    if (w.at(x, y) !== '~') continue;
    const px = x * T, py = y * T, top = w.at(x, y - 1) !== '~';
    const wob = Math.round(Math.sin(x * 0.8 + time * 2.4) * 2) * 2;
    g.rect(px, py + (top ? 6 + wob : 0), T, top ? T - 6 - wob : T, top ? C(255, 150, 40) : C(220, 70, 20));
    if (top) { g.rect(px, py + 6 + wob, T, 4, C(255, 220, 110)); g.additive = true; g.circleGrad(px + 16, py, 34, C(255, 140, 40, 90), C(255, 140, 40, 0)); g.additive = false; }
    else if (Math.round((time * 3 + x + y * 1.7) % 5) === 0) g.rect(px + 8, py + 10, 8, 4, C(255, 190, 70));
  }
  g.additive = true;
  for (const ch of chunks) {
    for (const c of ch.crystals) {
      const hue = [C(120, 160, 255), C(200, 120, 255), C(110, 255, 200)][c.hue], x = c.x * T, y = c.y * T + T;
      const pulse = 0.75 + 0.25 * Math.sin(time * 2 + c.x);
      g.circleGrad(x + 16, y - 14, 34, withAlpha(hue, 0.35 * pulse), withAlpha(hue, 0));
      g.additive = false;
      g.rect(x + 10, y - 20, 6, 20, hue); g.rect(x + 16, y - 28, 6, 28, mix(hue, C(255, 255, 255), 0.4)); g.rect(x + 22, y - 14, 5, 14, hue);
      g.additive = true;
    }
    for (const rl of ch.relics) {
      if (w.got.has(rl.id)) continue;
      const bob = Math.sin(time * 2.4 + rl.x * 0.01) * 4, cx = Math.round(rl.x), cy = Math.round(rl.y + bob);
      g.circleGrad(cx, cy, 54, C(120, 235, 255, 150), C(120, 235, 255, 0));
      g.additive = false;
      const wdt = Math.abs(Math.cos(time * 1.6 + rl.x * 0.01)) * 10 + 4;
      g.rect(cx - wdt / 2, cy - 14, wdt, 28, C(150, 245, 255)); g.rect(cx - wdt / 2 + 2, cy - 12, Math.max(2, wdt - 6), 24, C(255, 255, 255));
      g.rect(cx - 2, cy - 18, 4, 4, C(200, 250, 255)); g.rect(cx - 2, cy + 14, 4, 4, C(120, 220, 245));
      g.additive = true;
      for (let k = 0; k < 4; k++) { const a = time * 1.5 + k * 1.57; g.rect(Math.round(cx + Math.cos(a) * 24), Math.round(cy + Math.sin(a) * 18), 2, 2, C(210, 250, 255, 200)); }
    }
  }
  g.additive = false;

  // ---- rain, fog, water
  if (w.rain > 0.02) {
    const a = w.rain;
    g.additive = true;
    const slant = w.wind * 26;
    for (let i = 0; i < 260; i++) {
      const span = 3200, sx = hash(i, 77, w.seed) * span;
      const x = Math.floor((view.x * 0.35 + slant * w.time * 0.06) / span) * span + sx - view.x * 0.35;
      const y = hash(i, 78, w.seed) * view.h + view.y;
      g.rect(x, y, 2, 14 + Math.floor(hash(i, 79, w.seed) * 10), C(186, 206, 226, 130 * a * (0.4 + hash(i, 80, w.seed) * 0.6)));
    }
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
      if (w.at(x, y) === 'X' && w.at(x, y - 1) !== 'X' && hash(x, y, w.seed) > 0.5) g.rect(x * T, y * T, T, 2, C(150, 186, 214, 110 * a));
    }
    g.additive = false;
  }
  const fogY = view.y + view.h * 0.62;
  for (let i = Math.floor(view.x / 400) - 1; i <= Math.ceil((view.x + view.w) / 400); i++) {
    const fx = i * 400 + hash(i, 5, w.seed) * 200 - view.x * 0.05;
    const h = 90 + Math.floor(Math.sin(fx * 0.004 + w.time * 0.05) * 40);
    g.rectGradV(fx, fogY - h, 420, h, C(150, 166, 186, 0), C(150, 166, 186, 40));
  }
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) if (w.at(x, y) === ':') {
    const px = x * T, py = y * T, deep = y > SEA + 4;
    g.rect(px, py, T, T, deep ? C(28, 92, 138) : C(39, 133, 175, 115));
    if (w.at(x, y - 1) !== ':') {
      const wave = Math.round(Math.sin(x * 0.7 + time * 2) * 2) * 2;
      g.rect(px, py + wave, T, 4, C(163, 226, 224, 190)); g.rect(px + 6, py + 10 + wave, 12, 2, C(171, 229, 233, 100));
    } else if (Math.round((time * 4 + x * 0.7 + y) % 4) === 0) g.rect(px + 4, py + 6, 8, 2, C(120, 196, 214, 90));
  }
  r.drawParticles(w);
  for (const p of w.fading) r.drawPortal(p, w.linked);
  if (drawPortals) for (const p of w.portals) if (p) r.drawPortal(p, w.linked);
}

const PP_LIFT = C(63, 167, 255);
