// Every sprite and tile is painted in code (no art files): the palette, the hero, cube, portals, and the baked
// back/front layers of each level. Port of Render/Sprites.cs and Render/LevelArt.cs.
import { C, CLEAR, mix, hash, TAU } from './core.js';
import { Pix, PixelFont } from './gfx.js';
import { World } from './world.js';

/** Pixel palette shared by every sprite and tile. */
export const PP = {
  Ink: C(16, 17, 22),
  Body: C(217, 119, 87), BodyHi: C(242, 165, 132), BodyLo: C(176, 92, 62), BodyOut: C(48, 20, 13), Eye: C(26, 16, 16),
  Cream: C(232, 226, 213), CreamHi: C(247, 243, 234), CreamSeam: C(189, 182, 168), CreamLo: C(176, 168, 152),
  CreamNoise: C(220, 213, 199), TileOut: C(28, 30, 37),
  Metal: C(69, 75, 90), MetalHi: C(115, 124, 147), MetalMid: C(88, 95, 114), MetalLo: C(50, 55, 68),
  MetalSeam: C(42, 46, 57), MetalDeep: C(36, 39, 49), MetalDeep2: C(42, 46, 57),
  Bg: C(29, 33, 43), BgSeam: C(20, 23, 30), BgHi: C(38, 43, 55), BgDetail: C(24, 27, 35),
  BlueRamp: [C(14, 44, 96), C(30, 110, 214), C(63, 167, 255), C(166, 220, 255), C(236, 248, 255)],
  OrangeRamp: [C(96, 38, 6), C(196, 86, 14), C(255, 138, 31), C(255, 208, 138), C(255, 245, 225)],
  GooTop: C(200, 245, 138), Goo1: C(143, 214, 85), Goo2: C(92, 158, 51), Goo3: C(61, 112, 34), Goo4: C(41, 78, 23),
  Laser: C(255, 59, 59), LaserCore: C(255, 228, 220), Exit: C(94, 224, 138), Hazard: C(245, 197, 66),
  Fizz: C(143, 211, 255), Gel: C(63, 167, 255), SpeedGel: C(255, 138, 40),
};

export const PLAYER_FRAMES = 8;
export const PORTAL_FRAMES = 6;

// ------------------------------------------------------------------ the hero and the character customiser

export const PLAYER_H = 20;       // frame height: 4 extra rows on top so hats fit
const Y0 = 4;

const pal = (body, hi, lo, out, eye = C(26, 16, 16)) => ({ body: C(...body), hi: C(...hi), lo: C(...lo), out: C(...out), eye });
export const COLORS = [
  { name: 'TERRACOTTA', ...pal([217, 119, 87], [242, 165, 132], [176, 92, 62], [48, 20, 13]) },
  { name: 'SKY', ...pal([86, 160, 230], [150, 205, 250], [50, 110, 185], [14, 30, 60]) },
  { name: 'MINT', ...pal([96, 200, 140], [160, 238, 186], [56, 150, 98], [12, 44, 28]) },
  { name: 'GRAPE', ...pal([160, 110, 220], [205, 165, 248], [112, 70, 170], [34, 16, 56]) },
  { name: 'BUBBLEGUM', ...pal([240, 130, 170], [255, 186, 212], [200, 88, 130], [60, 18, 36]) },
  { name: 'SUNNY', ...pal([240, 200, 70], [255, 232, 140], [200, 155, 36], [60, 40, 8]) },
  { name: 'SNOW', ...pal([226, 230, 238], [255, 255, 255], [170, 178, 196], [40, 46, 64]) },
  { name: 'MIDNIGHT', ...pal([70, 78, 110], [112, 122, 164], [44, 50, 78], [8, 10, 22], C(240, 244, 255)) },
];

const rc = (r, g, b) => C(r, g, b);
// hats: ops are [x, y, w, h, colour] with y relative to the top row of the body (negative = above the head)
const RED = rc(214, 64, 64), RED_HI = rc(244, 120, 110), RED_LO = rc(150, 36, 44), BLACK = rc(34, 34, 46), GREY = rc(90, 92, 110);
const GOLD = rc(246, 196, 60), GOLD_HI = rc(255, 232, 140), GOLD_LO = rc(196, 140, 30), WHITE = rc(250, 250, 252), CREAM = rc(226, 228, 236);
const BLUE = rc(60, 110, 200), BLUE_HI = rc(120, 170, 240), BLUE_LO = rc(34, 66, 140);
const PURPLE = rc(110, 70, 190), PURPLE_HI = rc(160, 120, 235), PURPLE_LO = rc(70, 40, 130);
const BROWN = rc(150, 96, 54), BROWN_HI = rc(190, 130, 80), BROWN_LO = rc(100, 60, 32);
const PINK = rc(244, 110, 170), TEAL = rc(60, 200, 190), GREEN = rc(80, 190, 90);

export const HATS = [
  { name: 'NONE', ops: [] },
  { name: 'CAP', ops: [[4, -3, 8, 3, RED], [5, -3, 4, 1, RED_HI], [4, -1, 8, 1, RED_LO], [11, -1, 4, 1, RED_LO], [8, -2, 1, 1, WHITE]] },
  { name: 'TOP HAT', ops: [[3, -1, 10, 1, BLACK], [5, -6, 6, 5, BLACK], [5, -2, 6, 1, RED], [6, -5, 1, 3, GREY]] },
  { name: 'CROWN', ops: [[4, -2, 8, 2, GOLD], [4, -4, 2, 2, GOLD], [7, -4, 2, 2, GOLD], [10, -4, 2, 2, GOLD], [4, -2, 8, 1, GOLD_HI], [4, -1, 8, 1, GOLD_LO], [7, -2, 2, 1, RED]] },
  { name: 'PARTY HAT', ops: [[7, -6, 2, 1, GOLD], [7, -5, 2, 1, PINK], [6, -4, 4, 1, TEAL], [6, -3, 4, 1, PINK], [5, -2, 6, 1, TEAL], [5, -1, 6, 1, PINK]] },
  { name: 'WIZARD HAT', ops: [[8, -7, 2, 1, PURPLE], [7, -6, 3, 1, PURPLE], [6, -5, 4, 1, PURPLE], [6, -4, 4, 1, PURPLE_HI], [5, -3, 6, 1, PURPLE], [4, -2, 8, 1, PURPLE], [2, -1, 12, 1, PURPLE_LO], [4, -2, 8, 1, GOLD], [7, -4, 1, 1, GOLD_HI]] },
  { name: 'COWBOY HAT', ops: [[1, -1, 14, 1, BROWN], [1, -2, 2, 1, BROWN], [13, -2, 2, 1, BROWN], [4, -4, 8, 3, BROWN], [7, -5, 2, 1, BROWN], [4, -2, 8, 1, BROWN_LO], [5, -4, 2, 1, BROWN_HI]] },
  { name: 'BEANIE', ops: [[7, -6, 2, 2, WHITE], [3, -4, 10, 3, BLUE], [4, -4, 4, 1, BLUE_HI], [3, -1, 10, 1, BLUE_LO], [3, -2, 10, 1, BLUE_HI]] },
  { name: 'PROPELLER', ops: [[4, -2, 8, 2, BLUE], [4, -2, 4, 2, RED], [5, -2, 2, 1, RED_HI], [7, -3, 2, 1, GREY], [3, -4, 10, 1, GOLD], [3, -4, 4, 1, GOLD_HI]] },
  { name: 'CHEF HAT', ops: [[5, -6, 6, 1, WHITE], [4, -5, 8, 4, WHITE], [4, -1, 8, 1, CREAM], [6, -4, 1, 2, CREAM], [9, -3, 1, 2, CREAM], [4, -5, 1, 1, CLEAR]] },
  { name: 'HALO', ops: [[5, -6, 6, 1, GOLD_HI], [4, -5, 1, 2, GOLD], [11, -5, 1, 2, GOLD], [5, -3, 6, 1, GOLD_LO]] },
];

const FACES = ['NONE', 'SHADES', 'GLASSES', 'MUSTACHE', 'BLUSH'];
const OUTFITS = ['NONE', 'SCARF', 'BOW TIE', 'TIE', 'STRIPES', 'CAPE'];
export const FACE_NAMES = FACES;
export const OUTFIT_NAMES = OUTFITS;
export const DEFAULT_LOOK = { color: 0, hat: 0, face: 0, outfit: 0 };

export function cleanLook(l) {
  const n = (v, max) => (Number.isInteger(v) && v >= 0 && v < max ? v : 0);
  l = l || {};
  return { color: n(l.color, COLORS.length), hat: n(l.hat, HATS.length), face: n(l.face, FACES.length), outfit: n(l.outfit, OUTFITS.length) };
}

/** Frames: 0 idle, 1 blink, 2-5 walk, 6 jump, 7 fall. 16x20 each, facing right. */
export function buildPlayerSheet(look = DEFAULT_LOOK) {
  look = cleanLook(look);
  const col = COLORS[look.color];
  const sheet = new Pix(16 * PLAYER_FRAMES, PLAYER_H);
  const light = col.body.r * 0.3 + col.body.g * 0.6 + col.body.b * 0.1 > 120;
  for (let f = 0; f < PLAYER_FRAMES; f++) {
    const p = new Pix(16, PLAYER_H);
    const bob = f === 3 || f === 5 ? 1 : 0;           // body dips on passing frames
    let top = 3 + bob + Y0, bottom = 11 + bob + Y0;
    if (f === 6) { top -= 1; bottom -= 1; }
    const R = (x, y, w, h, c) => p.rect(x, top + y, w, h, c);

    // cape hangs behind everything
    if (look.outfit === 5) {
      const wave = f === 6 ? -1 : f === 7 ? 1 : 0;
      p.rect(0, top + 1, 3, bottom - top + 2, RED);
      p.rect(0, top + 1, 1, bottom - top + 2, RED_LO);
      p.rect(1 + wave, bottom + 1, 3, 2, RED_LO);
      p.rect(1, top + 1, 1, 3, RED_HI);
    }

    // legs first (body overlaps their tops)
    const legX = [3, 5, 10, 12];
    for (let i = 0; i < 4; i++) {
      let x = legX[i], len = 3;
      if (f === 2 && (i === 0 || i === 2)) { len = 2; x += 1; }
      if (f === 4 && (i === 1 || i === 3)) { len = 2; x += 1; }
      if (f === 6) len = 2;
      if (f === 7) { len = 3; x += i < 2 ? -1 : 1; }
      p.rect(x, bottom + 1, 1, len, col.lo);
    }

    // body
    p.rect(2, top, 12, bottom - top + 1, col.body);
    p.set(2, top, CLEAR); p.set(13, top, CLEAR); p.set(2, bottom, CLEAR); p.set(13, bottom, CLEAR);
    p.hLine(3, top, 10, col.hi);
    p.vLine(2, top + 1, bottom - top - 1, col.hi);
    p.hLine(3, bottom, 10, col.lo);
    p.vLine(13, top + 1, bottom - top - 1, col.lo);
    p.set(4, top + 1, col.hi);

    // little claws on the sides
    const armY = f === 6 || f === 7 ? top + 1 : top + 4;
    p.rect(0, armY, 2, 2, col.body);
    p.set(0, armY + 1, col.lo);
    p.rect(14, armY, 2, 2, col.body);
    p.set(15, armY + 1, col.lo);

    // outfit (over the body)
    switch (look.outfit) {
      case 1: // scarf with a tail
        R(2, 6, 12, 2, RED); R(3, 6, 10, 1, RED_HI); R(3, 7, 10, 1, RED_LO);
        R(3, 8, 2, 2, RED); R(3, 9, 2, 1, RED_LO);
        break;
      case 2: // bow tie
        R(7, 6, 2, 3, RED); R(8, 6, 1, 1, RED_HI); R(11, 6, 2, 3, RED); R(12, 6, 1, 1, RED_HI);
        R(9, 7, 2, 2, RED_LO);
        break;
      case 3: // tie
        R(9, 6, 3, 1, BLUE); R(10, 7, 2, 3, BLUE); R(10, 7, 1, 2, BLUE_HI); R(9, 6, 1, 1, BLUE_HI);
        break;
      case 4: { // sailor stripes
        const sc = light ? BLUE_LO : WHITE;
        R(3, 5, 10, 1, sc); R(3, 7, 10, 1, sc);
        break;
      }
      default: break;
    }

    // face accessories
    switch (look.face) {
      case 1: // shades
        R(7, 3, 6, 2, BLACK); R(8, 3, 1, 1, GREY); R(11, 3, 1, 1, GREY); R(3, 3, 4, 1, BLACK);
        break;
      case 2: // wire-frame glasses: the eyes show through the open frame
        R(7, 2, 6, 1, BROWN_LO); R(7, 5, 6, 1, BROWN_LO); R(7, 3, 1, 2, BROWN_LO); R(12, 3, 1, 2, BROWN_LO); R(9, 3, 2, 1, BROWN_LO);
        break;
      case 3: // moustache
        R(7, 6, 6, 1, BROWN_LO); R(7, 7, 2, 1, BROWN_LO); R(11, 7, 2, 1, BROWN_LO); R(8, 6, 4, 1, BROWN);
        break;
      case 4: // blush
        R(6, 5, 2, 1, C(255, 130, 150)); R(11, 5, 2, 1, C(255, 130, 150));
        break;
      default: break;
    }
    // eyes (looking right); the glasses draw over the top when worn
    const eyeCol = col.eye;
    const hideEyes = look.face === 1;
    if (!hideEyes) {
      if (f === 1) { R(8, 4, 1, 1, eyeCol); R(11, 4, 1, 1, eyeCol); }
      else { R(8, 3, 1, 2, eyeCol); R(11, 3, 1, 2, eyeCol); }
    }

    // hat
    for (const [x, y, w, h, c] of HATS[look.hat].ops) R(x, y, w, h, c);

    p.outline(col.out);
    sheet.draw(p, f * 16, 0);
  }
  return sheet;
}

function buildCube() {
  const p = new Pix(16, 16);
  const body = C(143, 150, 163), hi = C(185, 192, 204), lo = C(96, 103, 122), pad = C(92, 99, 117), padHi = C(135, 144, 163);
  p.rect(1, 1, 14, 14, body);
  p.hLine(2, 1, 12, hi); p.vLine(1, 2, 12, hi);
  p.hLine(2, 14, 12, lo); p.vLine(14, 2, 12, lo);
  for (const [x, y] of [[1, 1], [11, 1], [1, 11], [11, 11]]) {
    p.rect(x, y, 4, 4, pad);
    p.set(x + 1, y + 1, padHi);
    p.set(x, y, CLEAR);
  }
  p.set(14, 1, CLEAR); p.set(1, 14, CLEAR); p.set(14, 14, CLEAR);
  // edge notches
  p.rect(7, 1, 2, 2, lo); p.rect(7, 13, 2, 2, lo); p.rect(1, 7, 2, 2, lo); p.rect(13, 7, 2, 2, lo);
  // centre disc with a heart
  p.rect(5, 5, 6, 6, C(232, 228, 220));
  p.set(5, 5, body); p.set(10, 5, body); p.set(5, 10, body); p.set(10, 10, body);
  const heart = C(255, 111, 168);
  p.set(6, 6, heart); p.set(9, 6, heart);
  p.hLine(6, 7, 4, heart);
  p.hLine(6, 8, 4, heart);
  p.set(7, 9, heart); p.set(8, 9, heart);
  p.outline(C(29, 32, 40));
  return p;
}

function buildExit() {
  const p = new Pix(28, 44);
  const frame = C(58, 65, 80), frameHi = C(92, 101, 122);
  p.rect(0, 0, 28, 44, frame);
  p.hLine(1, 0, 26, frameHi); p.vLine(0, 1, 43, frameHi);
  for (let y = 4; y < 44; y++) {
    const t = (y - 4) / 40;
    p.rect(4, y, 20, 1, mix(C(18, 60, 40), C(86, 220, 140), t * t));
  }
  p.rect(4, 4, 20, 2, C(170, 255, 200));
  p.set(0, 0, CLEAR); p.set(27, 0, CLEAR);
  p.outline(PP.TileOut);
  return p;
}

function buildLamp() {
  const p = new Pix(14, 4);
  p.rect(0, 0, 14, 2, C(58, 65, 80));
  p.rect(1, 2, 12, 1, C(255, 244, 214));
  p.rect(2, 3, 10, 1, C(255, 232, 180));
  return p;
}

function buildPortal(ramp, linked, vertical) {
  const w = 12, h = 40;
  const sheet = vertical ? new Pix(w * PORTAL_FRAMES, h) : new Pix(h * PORTAL_FRAMES, w);
  for (let f = 0; f < PORTAL_FRAMES; f++) {
    const spark = f / PORTAL_FRAMES * TAU;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const dx = (x + 0.5 - w / 2) / (w / 2), dy = (y + 0.5 - h / 2) / (h / 2 - 1);
      const e = dx * dx + dy * dy;
      if (e > 1) continue;
      const ang = Math.atan2(dy, dx);
      const dd = ang - spark;
      const da = Math.abs(dd - TAU * Math.round(dd / TAU));
      let c;
      if (e > 0.78) c = da < 0.5 ? ramp[4] : ramp[1];
      else if (e > 0.5) c = da < 0.7 ? ramp[4] : ramp[2];
      else if (e > 0.36) c = ramp[3];
      else if (linked) continue;
      else {
        // swirling membrane
        const sw = Math.sin(ang * 2 + e * 9 - spark * 2);
        c = sw > 0.3 ? ramp[1] : sw > -0.4 ? ramp[0] : mix(ramp[0], PP.Ink, 0.4);
      }
      sheet.set(vertical ? f * w + x : f * h + y, vertical ? y : x, c);
    }
  }
  return sheet;
}

export const Sprites = {
  ready: false,
  setLook(look) {
    this.look = cleanLook(look);
    this.playerSheet = buildPlayerSheet(this.look);
    this.playerTex = this.playerSheet.toCanvas();
  },
  load() {
    if (this.ready) return;
    this.setLook(DEFAULT_LOOK);
    this.cube = buildCube();
    this.exitDoor = buildExit();
    this.lamp = buildLamp();
    this.cubeTex = this.cube.toCanvas();
    this.exitTex = this.exitDoor.toCanvas();
    this.lampTex = this.lamp.toCanvas();
    this.portalVTex = [[], []]; this.portalHTex = [[], []];
    for (let c = 0; c < 2; c++) for (let l = 0; l < 2; l++) {
      const ramp = c === 0 ? PP.BlueRamp : PP.OrangeRamp;
      this.portalVTex[c][l] = buildPortal(ramp, l === 1, true).toCanvas();
      this.portalHTex[c][l] = buildPortal(ramp, l === 1, false).toCanvas();
    }
    this.ready = true;
  },
};

// ------------------------------------------------------------------ level art

const P = 16;
export const THEMES = [
  null,
  C(30, 190, 180, 46),   // teal
  C(150, 90, 230, 52),   // violet
  C(235, 150, 40, 44),   // amber
  C(60, 200, 100, 44),   // green
  C(235, 60, 70, 46),    // crimson
  C(110, 170, 255, 56),  // ice
];
export const THEME_COUNT = 7;

const staticSolid = (c) => '#X<>^vJQqG'.includes(c) || World.isReceiver(c);

/**
 * Bakes the static parts of a level into two pixel buffers (16 px per tile): the back wall (behind everything) and
 * the solid tiles (in front of characters, which hides the half of a body that is inside a portal).
 */
export class LevelArt {
  constructor(w) {
    this.w = w;
    this.back = new Pix(w.w * P, w.h * P);
    this.front = new Pix(w.w * P, w.h * P);
    this.paintBack(w);
    for (let ty = 0; ty < w.h; ty++) for (let tx = 0; tx < w.w; tx++) this.paintTile(w, tx, ty);
    this.backTex = this.back.toCanvas();
    this.frontTex = this.front.toCanvas();
  }

  mass(w, x, y) { return x < 0 || y < 0 || x >= w.w || y >= w.h || staticSolid(w.at(x, y)); }

  paintBack(w) {
    const b = this.back;
    b.rect(0, 0, b.w, b.h, PP.Bg);
    const panel = 64;
    for (let py = 0; py * panel < b.h; py++) for (let px = 0; px * panel < b.w; px++) {
      const x0 = px * panel, y0 = py * panel;
      const hsh = hash(px, py, 21);
      const tone = mix(PP.Bg, C(33, 38, 49), hash(px, py, 5));
      b.rect(x0, y0, panel, panel, tone);
      b.hLine(x0, y0, panel, PP.BgSeam);
      b.vLine(x0, y0, panel, PP.BgSeam);
      b.hLine(x0 + 1, y0 + 1, panel - 2, PP.BgHi);
      b.vLine(x0 + 1, y0 + 1, panel - 2, PP.BgHi);
      for (const [rx, ry] of [[4, 4], [59, 4], [4, 59], [59, 59]]) {
        b.set(x0 + rx, y0 + ry, C(48, 54, 68));
        b.set(x0 + rx + 1, y0 + ry + 1, PP.BgSeam);
      }
      if (hsh < 0.12) {
        // vent
        for (let k = 0; k < 5; k++) {
          b.rect(x0 + 18, y0 + 20 + k * 5, 28, 2, C(15, 17, 23));
          b.hLine(x0 + 18, y0 + 22 + k * 5, 28, C(44, 50, 63));
        }
      } else if (hsh < 0.2) {
        // wall monitor
        b.rect(x0 + 16, y0 + 18, 32, 22, C(44, 50, 63));
        b.rect(x0 + 18, y0 + 20, 28, 18, C(12, 26, 32));
        for (let k = 0; k < 4; k++) {
          const len = 6 + Math.trunc(hash(px * 7 + k, py, 9) * 18);
          b.hLine(x0 + 20, y0 + 22 + k * 4, len, C(63, 191, 191, 200));
        }
      } else if (hsh < 0.26) {
        // hazard band
        for (let i = 0; i < 40; i++) for (let j = 0; j < 6; j++)
          b.set(x0 + 12 + i, y0 + 46 + j, Math.trunc((i + j) / 4) % 2 === 0 ? C(120, 100, 40) : C(30, 30, 30));
      } else if (hsh < 0.33) {
        // faint ring emblem
        for (let y = 0; y < 30; y++) for (let x = 0; x < 30; x++) {
          const d = Math.sqrt((x - 14.5) * (x - 14.5) + (y - 14.5) * (y - 14.5));
          if (d > 11 && d < 14) b.set(x0 + 17 + x, y0 + 17 + y, C(40, 46, 59));
          if (d < 4) b.set(x0 + 17 + x, y0 + 17 + y, C(40, 46, 59));
        }
      }
    }

    // pipes running along some rows
    for (let ty = 2; ty < w.h - 3; ty++) {
      if (hash(ty, 3, 77) > 0.12) continue;
      const y = ty * P + 6;
      b.rect(0, y, b.w, 5, C(46, 52, 66));
      b.hLine(0, y, b.w, C(70, 78, 96));
      b.hLine(0, y + 4, b.w, C(26, 29, 37));
      for (let x = 20; x < b.w; x += 72) {
        b.rect(x, y - 1, 4, 7, C(80, 88, 108));
        b.vLine(x + 3, y - 1, 7, C(40, 45, 58));
      }
    }

    // warm safety stripe along the lower wall
    const stripeY = (w.h - 5) * P + 2;
    b.rect(0, stripeY, b.w, 1, C(190, 176, 150));
    b.rect(0, stripeY + 2, b.w, 4, C(128, 66, 48));
    b.rect(0, stripeY + 6, b.w, 1, C(70, 36, 28));

    // big painted chamber number in the first open area near the top
    if (w.def.label.length > 0) {
      const s = 7, tw = PixelFont.pixWidth(w.def.label, s) + 8, th = 7 * s + 8;
      for (let ty = 2; ty < Math.trunc(w.h / 2); ty++) {
        let placed = false;
        for (let tx = w.w - 3 - Math.trunc(tw / P); tx >= 2; tx--) {
          const x0 = tx * P, y0 = ty * P;
          let free = true;
          for (let cy = Math.trunc(y0 / P); cy <= Math.trunc((y0 + th) / P) && free; cy++)
            for (let cx = Math.trunc(x0 / P); cx <= Math.trunc((x0 + tw) / P) && free; cx++)
              if (w.at(cx, cy) !== ' ') free = false;
          if (!free) continue;
          b.rect(x0 - 2, y0, tw + 4, th + 6, C(31, 35, 46));
          PixelFont.drawPix(b, w.def.label, x0 + 4, y0 + 6, s, C(40, 46, 60));
          PixelFont.drawPix(b, w.def.label, x0 + 2, y0 + 4, s, C(52, 59, 75));
          b.rect(x0 + 2, y0 + th + 2, tw - 4, 3, C(128, 66, 48));
          placed = true;
          break;
        }
        if (placed) break;
      }
    }

    // ambient occlusion: tiles cast a soft shadow down-right onto the wall
    const shadow = new Uint8Array(b.w * b.h);
    for (let y = 0; y < b.h; y++) for (let x = 0; x < b.w; x++) {
      if (x - 3 < 0 || y - 4 < 0) continue;
      const c = w.at(Math.trunc((x - 3) / P), Math.trunc((y - 4) / P));
      if (staticSolid(c) || World.isDoor(c)) shadow[y * b.w + x] = 1;
    }
    for (let i = 0; i < shadow.length; i++) if (shadow[i]) b.shade(i % b.w, Math.trunc(i / b.w), 0.4);

    // each chamber family has its own colour grade so the wall doesn't look the same every time
    if (w.def.theme > 0 && w.def.theme < THEMES.length) b.blendRect(0, 0, b.w, b.h, THEMES[w.def.theme]);
  }

  paintTile(w, tx, ty) {
    const c = w.at(tx, ty);
    switch (c) {
      case '#': this.panel(w, tx, ty); return;
      case 'X': this.metal(w, tx, ty); return;
      case '=': this.glass(w, tx, ty); return;
      case '<': case '>': case '^': case 'v': this.metal(w, tx, ty); this.emitter(tx, ty, c); return;
      case 'J': this.metal(w, tx, ty); this.gel(w, tx, ty, PP.Gel, C(166, 220, 255), C(28, 95, 184), C(20, 60, 120), false); return;
      case 'G': this.metal(w, tx, ty); this.gel(w, tx, ty, PP.SpeedGel, C(255, 214, 150), C(180, 70, 10), C(120, 40, 5), true); return;
      case 'Q': case 'q': this.metal(w, tx, ty); this.plate(tx, ty, c === 'Q'); return;
      default:
        if (World.isReceiver(c)) { this.metal(w, tx, ty); this.receiver(tx, ty); }
    }
  }

  exposed(w, x, y) {
    const m = (a, b) => this.mass(w, a, b);
    return {
      U: !m(x, y - 1), D: !m(x, y + 1), L: !m(x - 1, y), R: !m(x + 1, y),
      UL: !m(x - 1, y - 1), UR: !m(x + 1, y - 1), DL: !m(x - 1, y + 1), DR: !m(x + 1, y + 1),
    };
  }

  applyEdges(ox, oy, e, hi, hi2, lo, outline) {
    const f = this.front;
    if (e.U) { f.hLine(ox, oy + 1, P, hi); f.hLine(ox, oy + 2, P, hi2); }
    if (e.L) f.vLine(ox + 1, oy, P, hi2);
    if (e.D) { f.hLine(ox, oy + P - 2, P, lo); f.hLine(ox, oy + P - 3, P, mix(lo, hi2, 0.4)); }
    if (e.R) f.vLine(ox + P - 2, oy, P, lo);
    if (e.U) f.hLine(ox, oy, P, outline);
    if (e.D) f.hLine(ox, oy + P - 1, P, outline);
    if (e.L) f.vLine(ox, oy, P, outline);
    if (e.R) f.vLine(ox + P - 1, oy, P, outline);
    // rounded outer corners, dotted inner corners
    if (e.U && e.L) { f.set(ox, oy, CLEAR); f.set(ox + 1, oy + 1, outline); }
    if (e.U && e.R) { f.set(ox + P - 1, oy, CLEAR); f.set(ox + P - 2, oy + 1, outline); }
    if (e.D && e.L) { f.set(ox, oy + P - 1, CLEAR); f.set(ox + 1, oy + P - 2, outline); }
    if (e.D && e.R) { f.set(ox + P - 1, oy + P - 1, CLEAR); f.set(ox + P - 2, oy + P - 2, outline); }
    if (!e.U && !e.L && e.UL) f.set(ox, oy, outline);
    if (!e.U && !e.R && e.UR) f.set(ox + P - 1, oy, outline);
    if (!e.D && !e.L && e.DL) f.set(ox, oy + P - 1, outline);
    if (!e.D && !e.R && e.DR) f.set(ox + P - 1, oy + P - 1, outline);
  }

  panel(w, tx, ty) {
    const f = this.front, ox = tx * P, oy = ty * P;
    for (let y = 0; y < P; y++) for (let x = 0; x < P; x++) {
      let col = PP.Cream;
      if ((tx % 2 === 0 && x === 0) || (ty % 2 === 0 && y === 0)) col = PP.CreamSeam;
      else if ((tx % 2 === 0 && x === 1) || (ty % 2 === 0 && y === 1)) col = PP.CreamHi;
      else if ((tx % 2 === 1 && x === P - 1) || (ty % 2 === 1 && y === P - 1)) col = C(214, 207, 192);
      else if (hash(ox + x, oy + y, 3) < 0.035) col = PP.CreamNoise;
      f.set(ox + x, oy + y, col);
    }
    this.applyEdges(ox, oy, this.exposed(w, tx, ty), C(255, 253, 247), PP.CreamHi, PP.CreamLo, PP.TileOut);
  }

  metal(w, tx, ty) {
    const f = this.front, ox = tx * P, oy = ty * P;
    const e = this.exposed(w, tx, ty);
    const interior = !(e.U || e.D || e.L || e.R || e.UL || e.UR || e.DL || e.DR);
    for (let y = 0; y < P; y++) for (let x = 0; x < P; x++) {
      let col;
      if (interior) {
        col = PP.MetalDeep;
        if ((tx % 2 === 0 && x === 0) || (ty % 2 === 0 && y === 0)) col = C(30, 33, 42);
        else if (((ox + x) + (oy + y)) % 6 === 0) col = PP.MetalDeep2;
      } else {
        col = PP.Metal;
        if ((tx % 2 === 0 && x === 0) || (ty % 2 === 0 && y === 0)) col = PP.MetalSeam;
        else if ((tx % 2 === 0 && x === 1) || (ty % 2 === 0 && y === 1)) col = PP.MetalMid;
        else if (((ox + x) - (oy + y)) % 7 === 0) col = C(74, 80, 96);
      }
      f.set(ox + x, oy + y, col);
    }
    if (!interior) {
      const rx = tx % 2 === 0 ? 3 : P - 4, ry = ty % 2 === 0 ? 3 : P - 4;
      f.set(ox + rx, oy + ry, C(130, 139, 160));
      f.set(ox + rx + 1, oy + ry + 1, PP.MetalSeam);
    }
    this.applyEdges(ox, oy, e, PP.MetalHi, PP.MetalMid, PP.MetalLo, C(17, 18, 23));
  }

  glass(w, tx, ty) {
    const f = this.front, ox = tx * P, oy = ty * P;
    f.rect(ox, oy, P, P, C(140, 210, 255, 46));
    const u = w.at(tx, ty - 1) !== '=', d = w.at(tx, ty + 1) !== '=', l = w.at(tx - 1, ty) !== '=', r = w.at(tx + 1, ty) !== '=';
    const edge = C(212, 241, 255, 220), edge2 = C(159, 216, 255, 110);
    if (u) { f.hLine(ox, oy, P, edge); f.hLine(ox, oy + 1, P, edge2); }
    if (d) { f.hLine(ox, oy + P - 1, P, edge); f.hLine(ox, oy + P - 2, P, edge2); }
    if (l) { f.vLine(ox, oy, P, edge); f.vLine(ox + 1, oy, P, edge2); }
    if (r) { f.vLine(ox + P - 1, oy, P, edge); f.vLine(ox + P - 2, oy, P, edge2); }
    for (let i = 0; i < P; i++) {
      const k = (i + ty * 5 + tx * 3) % 20;
      if (k < 2) for (let j = 0; j < 2; j++) f.blend(ox + i, oy + (P - 1 - i + j) % P, C(255, 255, 255, 70));
    }
  }

  emitter(tx, ty, dir) {
    const f = this.front, ox = tx * P, oy = ty * P, house = C(29, 31, 38);
    switch (dir) {
      case '>': f.rect(ox + 11, oy + 3, 5, 10, house); f.rect(ox + 14, oy + 5, 2, 6, PP.Laser); f.rect(ox + 15, oy + 7, 1, 2, PP.LaserCore); break;
      case '<': f.rect(ox, oy + 3, 5, 10, house); f.rect(ox, oy + 5, 2, 6, PP.Laser); f.rect(ox, oy + 7, 1, 2, PP.LaserCore); break;
      case 'v': f.rect(ox + 3, oy + 11, 10, 5, house); f.rect(ox + 5, oy + 14, 6, 2, PP.Laser); f.rect(ox + 7, oy + 15, 2, 1, PP.LaserCore); break;
      case '^': f.rect(ox + 3, oy, 10, 5, house); f.rect(ox + 5, oy, 6, 2, PP.Laser); f.rect(ox + 7, oy, 2, 1, PP.LaserCore); break;
    }
  }

  receiver(tx, ty) {
    const f = this.front, ox = tx * P, oy = ty * P;
    for (let y = 0; y < P; y++) for (let x = 0; x < P; x++) {
      const d = Math.sqrt((x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5));
      if (d < 6.2) f.set(ox + x, oy + y, C(29, 31, 38));
      if (d < 4.2) f.set(ox + x, oy + y, C(74, 32, 24));
      if (d >= 6.2 && d < 7.2) f.set(ox + x, oy + y, C(110, 118, 138));
    }
  }

  gel(w, tx, ty, body, hi, lo, outline, streaks) {
    if (this.mass(w, tx, ty - 1)) return;
    const f = this.front, ox = tx * P, oy = ty * P;
    f.rect(ox, oy, P, 6, body);
    f.hLine(ox, oy, P, outline);
    f.hLine(ox, oy + 1, P, hi);
    f.hLine(ox, oy + 5, P, lo);
    if (streaks) for (let x = 0; x < P; x += 4) { f.set(ox + x + 1, oy + 3, hi); f.set(ox + x + 2, oy + 3, hi); f.set(ox + x + 3, oy + 2, hi); }
    else for (let x = 0; x < P; x += 5) f.set(ox + x + (tx % 3), oy + 3, C(220, 242, 255));
  }

  /** Launch plate: an orange pad with a chevron pointing up and to the side it throws you. */
  plate(tx, ty, right) {
    const f = this.front, ox = tx * P, oy = ty * P;
    const pad = C(255, 150, 40), padHi = C(255, 215, 130), padLo = C(170, 80, 10);
    f.rect(ox, oy, P, 5, pad);
    f.hLine(ox, oy, P, C(90, 40, 5));
    f.hLine(ox, oy + 1, P, padHi);
    f.hLine(ox, oy + 4, P, padLo);
    const dx = right ? 1 : -1, cx = ox + (right ? 4 : P - 5);
    for (let i = 0; i < 4; i++) {
      f.set(cx + dx * i * 2, oy + 7 - i, C(255, 214, 150));
      f.set(cx + dx * i * 2 + dx, oy + 7 - i, C(255, 214, 150));
      f.set(cx + dx * i * 2, oy + 8 - i, C(255, 160, 50));
    }
  }
}
