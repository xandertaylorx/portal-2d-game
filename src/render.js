// The scene renderer: back wall, tiles, props, characters, lasers, particles, and the see-through portal lenses.
// Port of Render/Renderer.cs. World units are 32 per tile; the camera zoom of 0.5 maps that to 16 px tiles on the
// 640x360 canvas, which CSS then scales up with nearest-neighbour filtering.
//
// Portal windows: for each linked portal X the world is rendered off-screen through the portal transform
// (partner -> X). The C# game then runs a fragment shader over a lens-shaped patch; here the same shader runs on the
// CPU over a few thousand pixels per portal.
import {
  K, C, withAlpha, mix, clamp, clamp01, hash, easeOutBack, roundEven, Portal, PortalColor, HALF_LEN, overlaps, V,
} from './core.js';
import { PP, Sprites, LevelArt, PORTAL_FRAMES, PLAYER_H } from './art.js';
import { PixelFont, makeCanvas } from './gfx.js';
import { World, PKind, GunMode } from './world.js';

const T = K.Tile;
const snap = (v) => roundEven(v * K.Px) / K.Px;
const snapV = (v) => V(snap(v.x), snap(v.y));
const ipx = (x, y, w, h, c, g) => g.rect(Math.trunc(snap(x)), Math.trunc(snap(y)), Math.trunc(Math.max(2, snap(w))), Math.trunc(Math.max(2, snap(h))), c);

const smoothstep = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };
const mixf = (a, b, t) => a + (b - a) * t;

export class Renderer {
  constructor(gfx) {
    this.gfx = gfx;
    this.windowsEnabled = true;
    this.time = 0;
    this.art = new Map();
    this.views = [0, 1].map(() => {
      const cv = makeCanvas(K.CanvasW, K.CanvasH);
      cv.dataset.readback = '1';
      return cv;
    });
    this.viewData = [null, null];
    this.lensCanvas = makeCanvas(192, 192);
    this.lensCtx = this.lensCanvas.getContext('2d');
  }

  artFor(w) {
    let a = this.art.get(w);
    if (a) return a;
    // keep the cache tiny: menu world + current level
    for (const key of [...this.art.keys()]) if (key.def.name !== 'Menu') this.art.delete(key);
    a = new LevelArt(w);
    this.art.set(w, a);
    return a;
  }

  // ================================================================ frame entry points

  viewRect() {
    const g = this.gfx;
    const tl = g.toWorld(0, 0), br = g.toWorld(K.CanvasW, K.CanvasH);
    return { x: tl.x - T, y: tl.y - T, w: br.x - tl.x + 2 * T, h: br.y - tl.y + 2 * T };
  }

  /** Renders the world through each linked portal into its own off-screen canvas. Call before the main pass. */
  prepareWindows(w, cam, mainCanvas) {
    this.viewData = [null, null];
    if (!this.windowsEnabled || !w.linked) return;
    const g = this.gfx;
    g.camera(cam);
    const screen = this.viewRect();
    for (let i = 0; i < 2; i++) {
      const x = w.portals[i], y = w.portals[1 - i];
      const view = this.mappedBounds(x, y, screen);
      g.setTarget(this.views[i]);
      g.clear(PP.Bg);
      g.camera(cam);
      g.push();
      g.mult(Portal.matrix(y, x));
      this.drawWorld(w, view, true);
      g.pop();
    }
    g.setTarget(mainCanvas);
  }

  drawScene(w, cam, eye) {
    const g = this.gfx;
    g.camera(cam);
    this.drawWorld(w, this.viewRect(), false);
    const lenses = this.windowsEnabled && w.linked;
    for (let i = 0; i < 2; i++) {
      const p = w.portals[i];
      if (!p) continue;
      if (lenses) this.drawLens(w, p, eye, i);
      else this.drawPortal(p, w.linked);
    }
  }

  mappedBounds(from, to, r) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const p of [V(r.x, r.y), V(r.x + r.w, r.y), V(r.x, r.y + r.h), V(r.x + r.w, r.y + r.h)]) {
      const q = Portal.mapPoint(from, to, p);
      minX = Math.min(minX, q.x); minY = Math.min(minY, q.y); maxX = Math.max(maxX, q.x); maxY = Math.max(maxY, q.y);
    }
    return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
  }

  // ================================================================ world

  /** Draws a baked layer, but only the part inside the view (the layers can be thousands of pixels wide). */
  drawLayer(tex, w, view) {
    const x0 = clamp(Math.floor(view.x / 2), 0, tex.width), y0 = clamp(Math.floor(view.y / 2), 0, tex.height);
    const x1 = clamp(Math.ceil((view.x + view.w) / 2), 0, tex.width), y1 = clamp(Math.ceil((view.y + view.h) / 2), 0, tex.height);
    if (x1 <= x0 || y1 <= y0) return;
    this.gfx.tex(tex, x0, y0, x1 - x0, y1 - y0, x0 * 2, y0 * 2, (x1 - x0) * 2, (y1 - y0) * 2);
  }

  drawWorld(w, view, drawPortals) {
    const g = this.gfx;
    const a = this.artFor(w);
    g.rectR(view, PP.Bg);
    this.drawLayer(a.backTex, w, view);
    this.drawLamps(w, view);
    if (w.hasExit) this.drawExit(w);
    const x0 = Math.max(0, Math.trunc(view.x / T)), y0 = Math.max(0, Math.trunc(view.y / T));
    const x1 = Math.min(w.w - 1, Math.trunc((view.x + view.w) / T)), y1 = Math.min(w.h - 1, Math.trunc((view.y + view.h) / T));
    // doors sit behind the player and cubes, so the open gap never covers them
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
      const c = w.tiles[x][y];
      if (World.isDoor(c)) this.drawDoor(w, x, y, c.charCodeAt(0) - 97);
    }
    for (const b of w.buttons) this.drawButton(b);
    for (const c of w.cubes) this.drawWithClones(w, c, () => this.drawCube(c));
    if (w.player) this.drawWithClones(w, w.player, () => this.drawPlayer(w.player));
    this.drawLayer(a.frontTex, w, view);

    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
      const c = w.tiles[x][y];
      if (c === '~') this.drawGoo(w, x, y);
      else if (c === '|') this.drawFizzler(w, x, y);
    }
    if (w.hasExit) this.drawExitSign(w);
    for (const r of w.receivers) this.drawReceiverGlow(r);
    for (const e of w.emitters) this.drawEmitterGlow(e);
    this.drawLasers(w);
    this.drawParticles(w);
    if (drawPortals) for (const p of w.portals) if (p) this.drawPortal(p, w.linked);
  }

  drawWithClones(w, b, draw) {
    draw();
    if (!w.linked || (b.held === true)) return;
    const g = this.gfx;
    for (let i = 0; i < 2; i++) {
      const p = w.portals[i];
      const u = p.u(b.pos), v = p.v(b.pos);
      const extN = b.extentAlong(p.normal) + 4, extT = b.extentAlong(p.up);
      if (Math.abs(u) > HALF_LEN + extT || v > extN || v < -extN) continue;
      g.push();
      g.mult(Portal.matrix(p, w.portals[1 - i]));
      draw();
      g.pop();
    }
  }

  drawLamps(w, view) {
    const g = this.gfx;
    const lamps = [];
    for (let x = 3; x < w.w - 3; x += 7)
      for (let y = 1; y < w.h - 4; y++)
        if (!w.isSolid(x, y) && w.isSolid(x, y - 1) && !w.isSolid(x, y + 1) && !w.isSolid(x, y + 2)) { lamps.push([x, y]); break; }
    g.additive = true;
    for (const [x, y] of lamps) {
      const lx = x * T + T / 2, ly = y * T;
      if (lx < view.x - 300 || lx > view.x + view.w + 300) continue;
      const flick = 0.85 + 0.15 * Math.sin(this.time * 1.3 + lx);
      g.quadGradV([V(lx - 12, ly + 8), V(lx + 12, ly + 8), V(lx + 130, ly + 300), V(lx - 130, ly + 300)], ly + 8, ly + 300,
        C(255, 236, 200, 30 * flick), C(255, 236, 200, 0));
      g.circleGrad(lx, ly + 8, 40, C(255, 230, 190, 50), C(255, 230, 190, 0));
    }
    g.additive = false;
    for (const [x, y] of lamps) g.tex(Sprites.lampTex, 0, 0, 14, 4, x * T + 2, y * T, 28, 8);
  }

  // ================================================================ dynamic tiles

  drawGoo(w, tx, ty) {
    const g = this.gfx, ox = tx * T, oy = ty * T;
    const surf = w.at(tx, ty - 1) !== '~';
    if (!surf) {
      g.rect(ox, oy, T, T, PP.Goo4);
      for (let i = 0; i < 16; i += 4) ipx(ox + i * 2 + (ty % 2) * 4, oy + ((i * 3 + tx * 5) % 16) * 2, 2, 2, PP.Goo3, g);
      return;
    }
    for (let i = 0; i < 16; i++) {
      const wx = tx * 16 + i;
      const top = 3 + roundEven(Math.sin(this.time * 2.4 + wx * 0.45) * 1.2 + Math.sin(this.time * 1.3 + wx * 0.17) * 0.7);
      const x = ox + i * 2;
      g.rect(x, oy + top * 2, 2, 2, PP.GooTop);
      g.rect(x, oy + top * 2 + 2, 2, 2, PP.Goo1);
      g.rect(x, oy + top * 2 + 4, 2, (9 - top) * 2, PP.Goo2);
      g.rect(x, oy + 18 + 4, 2, T - 22, PP.Goo3);
      if ((i + tx) % 3 === 0) g.rect(x, oy + 20, 2, 2, PP.Goo3);
    }
    g.additive = true;
    g.rectGradV(ox, oy - 40, T, 46, C(140, 230, 70, 0), C(140, 230, 70, 30));
    g.additive = false;
  }

  drawFizzler(w, tx, ty) {
    const g = this.gfx, ox = tx * T, oy = ty * T, t = this.time;
    g.additive = true;
    g.rect(ox + 6, oy, 20, T, C(42, 111, 191, 34));
    for (let k = 0; k < 3; k++) {
      const a = 0.5 + 0.5 * Math.sin(t * 8 + ty * 1.7 + k * 2.1 + tx);
      g.rect(ox + 8 + k * 8, oy, 2, T, C(143, 211, 255, 40 + 120 * a));
    }
    const step = Math.trunc(t * 20);
    for (let s = 0; s < 3; s++)
      if (hash(tx * 31 + s, ty * 17 + step, 4) < 0.35)
        g.rect(ox + 6 + Math.trunc(hash(tx, ty + s, step) * 10) * 2, oy + Math.trunc(hash(ty, tx + s, step) * 16) * 2, 2, 2, C(230, 248, 255, 220));
    g.additive = false;
    const solidAbove = w.isSolid(tx, ty - 1) || w.at(tx, ty - 1) === '=';
    const solidBelow = w.isSolid(tx, ty + 1) || w.at(tx, ty + 1) === '=';
    if (solidAbove) { g.rect(ox + 2, oy, 28, 6, C(93, 102, 124)); g.rect(ox + 6, oy + 6, 20, 2, PP.Fizz); }
    if (solidBelow) { g.rect(ox + 2, oy + T - 6, 28, 6, C(93, 102, 124)); g.rect(ox + 6, oy + T - 8, 20, 2, PP.Fizz); }
  }

  drawDoor(w, tx, ty, grp) {
    const g = this.gfx, ox = tx * T, oy = ty * T;
    const o = w.doorOpen[grp];
    const dc = String.fromCharCode(97 + grp);
    const top = w.at(tx, ty - 1) !== dc, bottom = w.at(tx, ty + 1) !== dc;
    g.rect(ox + 2, oy, 28, T, C(22, 24, 30));
    g.rect(ox + 2, oy, 2, T, C(58, 65, 80));
    g.rect(ox + 28, oy, 2, T, C(58, 65, 80));
    const hgt = Math.trunc(snap(T * (1 - o)));
    if (hgt >= 2) {
      g.rect(ox + 6, oy, 20, hgt, C(93, 102, 124));
      g.rect(ox + 8, oy, 2, hgt, C(130, 139, 160));
      for (let y = 6; y < hgt; y += 8) g.rect(ox + 6, oy + y, 20, 2, C(70, 76, 92));
      if (bottom && hgt >= 8)
        for (let i = 0; i < 10; i++) g.rect(ox + 6 + i * 2, oy + hgt - 6, 2, 6, Math.trunc(i / 2) % 2 === 0 ? PP.Hazard : C(30, 30, 34));
    }
    if (top) {
      const col = w.groupActive[grp] ? PP.Exit : C(255, 80, 70);
      g.rect(ox + 8, oy - 10, 16, 8, C(22, 24, 30));
      g.rect(ox + 12, oy - 8, 8, 4, col);
      g.additive = true;
      g.circleGrad(ox + 16, oy - 6, 18, withAlpha(col, 90 / 255), withAlpha(col, 0));
      g.additive = false;
    }
  }

  drawReceiverGlow(r) {
    if (r.glow < 0.01) return;
    const g = this.gfx;
    const cx = r.x * T + 16, cy = r.y * T + 16;
    const col = mix(C(255, 120, 70), C(255, 220, 160), r.glow);
    g.rect(cx - 6, cy - 6, 12, 12, withAlpha(col, r.glow));
    g.rect(cx - 8, cy - 4, 16, 8, withAlpha(col, r.glow));
    g.additive = true;
    g.circleGrad(cx, cy, 40, C(255, 120, 60, 150 * r.glow), C(255, 80, 40, 0));
    g.additive = false;
  }

  drawEmitterGlow(e) {
    const g = this.gfx;
    g.additive = true;
    g.circleGrad(e.x * T + 16 + e.dir.x * 14, e.y * T + 16 + e.dir.y * 14, 18 + Math.sin(this.time * 20) * 2, C(255, 60, 60, 120), C(255, 40, 40, 0));
    g.additive = false;
  }

  // ================================================================ props & characters

  drawExit(w) {
    const g = this.gfx, t = this.time;
    const x = w.exit.x * T + T / 2, yb = (w.exit.y + 1) * T;
    g.additive = true;
    g.circleGrad(x, yb - 36, 90, C(60, 230, 140, 45), C(60, 230, 140, 0));
    g.additive = false;
    // Match the one-tile-wide, two-tile-high trigger instead of clipping a larger door into nearby walls.
    g.tex(Sprites.exitTex, 0, 0, 16, 32, x - 16, yb - 64, 32, 64);
    if (!w.exitReady) {
      g.rect(x - 12, yb - 58, 24, 54, C(20, 24, 28, 210));
      g.rect(x - 6, yb - 34, 12, 12, C(245, 197, 66));
      g.rect(x - 4, yb - 40, 8, 8, C(245, 197, 66));
      g.rect(x - 2, yb - 38, 4, 4, C(20, 24, 28));
      return;
    }
    for (let k = 0; k < 3; k++) {
      const tt = (t * 0.8 + k / 3) % 1;
      const cy = Math.trunc(snap(yb - 12 - tt * 36));
      const col = C(210, 255, 225, 220 * Math.sin(tt * Math.PI));
      for (let i = 0; i < 4; i++) {
        g.rect(Math.trunc(x) - 8 + i * 2, cy + 6 - i * 2, 2, 2, col);
        g.rect(Math.trunc(x) + 6 - i * 2, cy + 6 - i * 2, 2, 2, col);
      }
    }
  }

  drawExitSign(w) {
    const g = this.gfx;
    const x = w.exit.x * T + T / 2, yb = (w.exit.y + 1) * T;
    g.rect(Math.trunc(x) - 26, Math.trunc(yb) - 88, 52, 20, C(20, 24, 28));
    g.rect(Math.trunc(x) - 26, Math.trunc(yb) - 88, 52, 2, C(58, 65, 80));
    const m = PixelFont.measure('EXIT', 1);
    PixelFont.draw(g, 'EXIT', V(x - m.x / 2, yb - 84), 1, w.exitReady ? PP.Exit : PP.Hazard);
    const groups = w.def.exitGroups || [];
    for (let i = 0; i < groups.length; i++) {
      const lit = w.groupActive[groups[i]];
      g.rect(x - groups.length * 4 + i * 8, yb - 66, 6, 4, lit ? PP.Exit : C(245, 90, 70));
    }
  }

  drawButton(b) {
    const g = this.gfx;
    const cx = Math.trunc(b.x * T + T / 2), fy = (b.y + 1) * T;
    const h = b.press > 0.5 ? 4 : 8;
    g.rect(cx - 28, fy - 6, 56, 6, C(43, 47, 58));
    g.rect(cx - 28, fy - 6, 56, 2, C(74, 81, 99));
    const dome = b.timed ? (b.press > 0.5 ? C(255, 214, 90) : C(230, 170, 40)) : b.press > 0.5 ? C(255, 120, 70) : C(217, 69, 58);
    g.rect(cx - 18, fy - 6 - h, 36, h, dome);
    g.rect(cx - 16, fy - 6 - h, 28, 2, C(255, 160, 140));
    if (b.timed) {
      // countdown bar: how long the door stays open
      g.rect(cx - 20, fy - 24, 40, 5, C(24, 27, 34));
      g.rect(cx - 19, fy - 23, Math.trunc(38 * b.holdT / World.ButtonHold), 3, C(255, 214, 90));
    }
    if (b.press > 0.05) {
      g.additive = true;
      g.circleGrad(cx, fy - 6, 44, C(255, 140, 60, 80 * b.press), C(255, 140, 60, 0));
      g.additive = false;
    }
  }

  drawCube(c) {
    if (c.gone) return;
    const g = this.gfx;
    const pos = snapV(c.pos);
    let alpha = 1;
    if (c.dissolving) alpha = 1 - clamp01(c.dissolveT / 0.8);
    const dx = pos.x - 16, dy = pos.y - 16;
    g.tex(Sprites.cubeTex, 0, 0, 16, 16, dx, dy, 32, 32, alpha);
    if (c.dissolving) {
      g.additive = true;
      g.tex(Sprites.cubeTex, 0, 0, 16, 16, dx, dy, 32, 32, 200 / 255 * clamp01(c.dissolveT));
      g.additive = false;
      return;
    }
    const px = Math.trunc(pos.x), py = Math.trunc(pos.y);
    if (c.mirror !== 0) {
      // mirror cube: a silvery diagonal showing which way it bends a beam
      g.rect(px - 12, py - 12, 24, 24, C(120, 200, 255, 90));
      const a = c.mirror > 0 ? V(pos.x - 12, pos.y + 12) : V(pos.x - 12, pos.y - 12);
      const e = c.mirror > 0 ? V(pos.x + 12, pos.y - 12) : V(pos.x + 12, pos.y + 12);
      g.line(a, e, 4, C(225, 245, 255));
      g.line(a, e, 1.5, C(255, 255, 255));
    }
    const glow = Math.max(c.held ? 1 : 0, c.onButtonGlow, c.teleportFlash);
    if (glow > 0.05) {
      const gc = withAlpha(c.held ? PP.BlueRamp[2] : PP.OrangeRamp[2], glow * 0.9);
      g.rectLines({ x: pos.x - 18, y: pos.y - 18, w: 36, h: 36 }, 2, gc);
    }
  }

  drawPlayer(p) {
    if (p.dead) return;
    const g = this.gfx;
    let frame;
    if (!p.onGround) frame = p.vel.y < 0 ? 6 : 7;
    else if (Math.abs(p.vel.x) > 10) frame = 2 + Math.trunc(p.walkPhase * 1.3) % 4;
    else frame = (this.time % 3.2) < 0.12 ? 1 : 0;

    const feet = snapV(V(p.pos.x, p.pos.y + p.half.y));
    const f = p.facing;
    const sw = f > 0 ? 16 : -16;
    const dx = feet.x - 16, dy = feet.y - PLAYER_H * 2 + 2;
    g.tex(Sprites.playerTex, frame * 16, 0, sw, PLAYER_H, dx, dy, 32, PLAYER_H * 2);
    if (p.teleportFlash > 0.05) {
      g.additive = true;
      g.tex(Sprites.playerTex, frame * 16, 0, sw, PLAYER_H, dx, dy, 32, PLAYER_H * 2, 180 / 255 * p.teleportFlash);
      g.additive = false;
    }

    // pixel portal gun held in the front claw, pointing at the cursor
    const bodyTop = frame === 6 ? 2 : (frame === 3 || frame === 5 ? 4 : 3);
    const armY = frame === 6 || frame === 7 ? bodyTop + 1 : bodyTop + 4;
    const hand = V(feet.x + f * 12, feet.y - 32 + 2 + armY * 2 + 2);
    const aim = p.aim;
    const gunCol = p.lastShot === PortalColor.Blue ? PP.BlueRamp[2] : PP.OrangeRamp[2];
    const kick = p.gunKick * 2;
    for (let k = 0; k <= 5; k++) {
      const d = k * 2.2 * 2 - kick;
      const pt = V(hand.x + aim.x * d, hand.y + aim.y * d);
      const c = k === 5 ? gunCol : k === 1 || k === 2 ? C(58, 65, 80) : C(232, 228, 220);
      g.rect(Math.trunc(snap(pt.x)) - 2, Math.trunc(snap(pt.y)) - 2, 4, 4, PP.BodyOut);
      g.rect(Math.trunc(snap(pt.x)), Math.trunc(snap(pt.y)), 2, 2, c);
    }
    const td = 5 * 4.4 - kick;
    const tip = V(hand.x + aim.x * td, hand.y + aim.y * td);
    g.rect(Math.trunc(snap(tip.x)), Math.trunc(snap(tip.y)), 2, 2, gunCol);
    g.additive = true;
    g.circleGrad(tip.x, tip.y, 10 + p.gunKick * 10, withAlpha(gunCol, 150 / 255), withAlpha(gunCol, 0));
    g.additive = false;
  }

  // ================================================================ lasers & particles

  drawLasers(w) {
    if (w.lasers.length === 0) return;
    const g = this.gfx;
    const flick = Math.trunc(this.time * 30) % 7 === 0;
    for (const s of w.lasers) {
      const x0 = Math.min(s.a.x, s.b.x), x1 = Math.max(s.a.x, s.b.x);
      const y0 = Math.min(s.a.y, s.b.y), y1 = Math.max(s.a.y, s.b.y);
      const horiz = y1 - y0 < x1 - x0;
      g.additive = true;
      if (horiz) g.rect(Math.trunc(x0), Math.trunc(snap(y0)) - 8, Math.trunc(x1 - x0), 16, C(255, 40, 40, flick ? 20 : 40));
      else g.rect(Math.trunc(snap(x0)) - 8, Math.trunc(y0), 16, Math.trunc(y1 - y0), C(255, 40, 40, flick ? 20 : 40));
      g.additive = false;
      if (horiz) {
        g.rect(Math.trunc(x0), Math.trunc(snap(y0)) - 2, Math.trunc(x1 - x0), 6, PP.Laser);
        g.rect(Math.trunc(x0), Math.trunc(snap(y0)), Math.trunc(x1 - x0), 2, PP.LaserCore);
      } else {
        g.rect(Math.trunc(snap(x0)) - 2, Math.trunc(y0), 6, Math.trunc(y1 - y0), PP.Laser);
        g.rect(Math.trunc(snap(x0)), Math.trunc(y0), 2, Math.trunc(y1 - y0), PP.LaserCore);
      }
    }
    g.additive = true;
    for (const imp of w.laserImpacts) g.circleGrad(imp.x, imp.y, flick ? 12 : 16, C(255, 90, 70, 200), C(255, 40, 40, 0));
    g.additive = false;
  }

  drawParticles(w) {
    const g = this.gfx;
    for (const p of w.fx.items) if (!p.additive) this.drawParticle(p);
    g.additive = true;
    for (const p of w.fx.items) if (p.additive) this.drawParticle(p);
    for (const t of w.fx.trails) {
      const a = t.life / 0.25;
      // dotted pixel tracer
      const dx = t.b.x - t.a.x, dy = t.b.y - t.a.y, len = Math.hypot(dx, dy);
      if (len < 1) continue;
      const col = withAlpha(t.color, a);
      for (let s = 0; s < len; s += 6) g.rect(Math.trunc(snap(t.a.x + dx / len * s)), Math.trunc(snap(t.a.y + dy / len * s)), 2, 2, col);
    }
    g.additive = false;
  }

  drawParticle(p) {
    const g = this.gfx;
    const a = clamp01(p.life / p.maxLife);
    const col = withAlpha(p.color, a * p.color.a / 255);
    const size = p.size > 3 ? 4 : 2;
    if (p.kind === PKind.Spark) g.rect(Math.trunc(snap(p.px - p.vx * 0.03)), Math.trunc(snap(p.py - p.vy * 0.03)), 2, 2, withAlpha(col, a * 0.5));
    const sx = Math.trunc(snap(p.px)), sy = Math.trunc(snap(p.py));
    if (p.kind === PKind.Ring) {
      g.rect(sx - 2, sy, 2, 2, col); g.rect(sx + 2, sy, 2, 2, col); g.rect(sx, sy - 2, 2, 2, col);
      return;
    }
    g.rect(sx, sy, size, size, col);
  }

  // ================================================================ portals

  /** Fetches the off-screen view for portal i once per frame. */
  viewPixels(i) {
    if (!this.viewData[i]) this.viewData[i] = this.views[i].getContext('2d', { willReadFrequently: true }).getImageData(0, 0, K.CanvasW, K.CanvasH);
    return this.viewData[i];
  }

  /** Linked portal drawn as a lens showing a live, parallaxed view out of its partner. */
  drawLens(w, x, eye, index) {
    const g = this.gfx, t = this.time;
    const open = easeOutBack(x.open);
    const tint = x.color === PortalColor.Blue ? PP.BlueRamp[2] : PP.OrangeRamp[2];
    const deep = x.color === PortalColor.Blue ? PP.BlueRamp[0] : PP.OrangeRamp[0];
    const pulse = 1 + 0.04 * Math.sin(t * 5 + x.color);
    const ra = 25 * open * pulse, rb = 15 * open * pulse;           // canvas pixels
    if (ra < 1) return;
    const raW = ra / K.Px, rbW = rb / K.Px;
    const c = x.center;

    g.additive = true;
    g.circleGrad(c.x + x.normal.x * 24, c.y + x.normal.y * 24, 90 * open, withAlpha(tint, 70 / 255), withAlpha(tint, 0));
    g.additive = false;

    const eyeU = ((eye.x - c.x) * x.up.x + (eye.y - c.y) * x.up.y) * K.Px;
    const par = clamp(-eyeU * 0.12, -16, 16);
    const sc = g.toScreen(c.x, c.y);
    const up = x.up, nrm = x.normal;
    const uu = V(up.x * (raW + 4), up.y * (raW + 4)), nn = V(nrm.x * (rbW + 4), nrm.y * (rbW + 4));
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const v of [V(c.x + uu.x + nn.x, c.y + uu.y + nn.y), V(c.x + uu.x - nn.x, c.y + uu.y - nn.y), V(c.x - uu.x - nn.x, c.y - uu.y - nn.y), V(c.x - uu.x + nn.x, c.y - uu.y + nn.y)]) {
      const s = g.toScreen(v.x, v.y);
      minX = Math.min(minX, s.x); minY = Math.min(minY, s.y); maxX = Math.max(maxX, s.x); maxY = Math.max(maxY, s.y);
    }
    const bx0 = Math.max(0, Math.ceil(minX - 0.5)), by0 = Math.max(0, Math.ceil(minY - 0.5));
    const bx1 = Math.min(K.CanvasW, Math.ceil(maxX - 0.5)), by1 = Math.min(K.CanvasH, Math.ceil(maxY - 0.5));
    const bw = bx1 - bx0, bh = by1 - by0;
    if (bw > 0 && bh > 0 && bw <= 192 && bh <= 192) {
      const src = this.viewPixels(index).data;
      const out = this.lensCtx.createImageData(bw, bh);
      const od = out.data;
      const tr = tint.r / 255, tg = tint.g / 255, tb = tint.b / 255, dr = deep.r / 255, dg = deep.g / 255, db = deep.b / 255;
      const opened = clamp01(x.open);
      for (let py = by0; py < by1; py++) for (let px = bx0; px < bx1; px++) {
        const pcx = px + 0.5, pcy = py + 0.5;
        const dx = pcx - sc.x, dy = pcy - sc.y;
        const nu = (dx * up.x + dy * up.y) / ra, nv = (dx * nrm.x + dy * nrm.y) / rb;     // nv: +1 = room side
        const e = nu * nu + nv * nv;
        if (e > 1) continue;
        const r = Math.sqrt(e);
        // fish-eye lens + swirl that grows toward the rim
        const lens = mixf(0.8, 1.2, r * r);
        const sw = r * r * 0.7 * Math.sin(t * 2.2 + r * 9) * opened;
        let qx = nu * lens, qy = nv * lens;
        const cs = Math.cos(sw), sn = Math.sin(sw);
        const qx2 = qx * cs - qy * sn, qy2 = qx * sn + qy * cs;
        // tangent -> sideways on the far side, across -> how deep we look out of the other portal
        const fu = qx2 * 62 + par;
        const fd = 2 + (0.5 - qy2 * 0.5) * 150;
        const spx = clamp(Math.floor(sc.x + up.x * fu - nrm.x * fd) + 0.5, 0.5, K.CanvasW - 0.5);
        const spy = clamp(Math.floor(sc.y + up.y * fu - nrm.y * fd) + 0.5, 0.5, K.CanvasH - 0.5);
        const si = (Math.floor(spy) * K.CanvasW + Math.floor(spx)) * 4;
        let cr = src[si] / 255, cg = src[si + 1] / 255, cb = src[si + 2] / 255;
        let k = 0.04 + 0.35 * smoothstep(0.6, 1.0, r);
        cr = mixf(cr, dr, k); cg = mixf(cg, dg, k); cb = mixf(cb, db, k);
        cr = cr * 1.18 + tr * 0.05; cg = cg * 1.18 + tg * 0.05; cb = cb * 1.18 + tb * 0.05;
        const shine = nu * 0.6 - nv * 0.8;
        if (r > 0.5 && r < 0.62 && shine > 0.3) { cr = mixf(cr, 1, 0.25); cg = mixf(cg, 1, 0.25); cb = mixf(cb, 1, 0.25); }
        const ripple = Math.sin(r * 24 - t * 5);
        if (ripple > 0.94 && r > 0.25) { cr = mixf(cr, tr, 0.3); cg = mixf(cg, tg, 0.3); cb = mixf(cb, tb, 0.3); }
        const checker = (px + py) % 2;
        const ang = Math.atan2(nv, nu);
        const spark = Math.sin(ang * 3 - t * 5) >= 0.9;
        if (r > 0.78 && r <= 0.86 && checker > 0.5) { cr = mixf(cr, tr, 0.75); cg = mixf(cg, tg, 0.75); cb = mixf(cb, tb, 0.75); }
        if (r > 0.86) { cr = mixf(dr, tr, 0.55); cg = mixf(dg, tg, 0.55); cb = mixf(db, tb, 0.55); }
        if (r > 0.88 && r < 0.95) { cr = tr; cg = tg; cb = tb; }
        if (r > 0.86 && spark) { cr = mixf(tr, 1, 0.8); cg = mixf(tg, 1, 0.8); cb = mixf(tb, 1, 0.8); }
        const o = ((py - by0) * bw + (px - bx0)) * 4;
        od[o] = clamp(cr, 0, 1) * 255; od[o + 1] = clamp(cg, 0, 1) * 255; od[o + 2] = clamp(cb, 0, 1) * 255; od[o + 3] = 255;
      }
      this.lensCtx.putImageData(out, 0, 0);
      const ctx = g.ctx;
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.globalAlpha = 1;
      ctx.drawImage(this.lensCanvas, 0, 0, bw, bh, bx0, by0, bw, bh);
    }

    // sparkles drifting out of the lens
    g.additive = true;
    for (let k = 0; k < 5; k++) {
      const tt = (t * 0.7 + k * 0.2) % 1;
      const side = hash(k, Math.trunc(t * 0.7 + k * 0.2), 3) * 2 - 1;
      const d = rbW + tt * 40;
      const pt = snapV(V(c.x + up.x * side * raW * 0.8 + nrm.x * d, c.y + up.y * side * raW * 0.8 + nrm.y * d));
      g.rect(Math.trunc(pt.x), Math.trunc(pt.y), 2, 2, withAlpha(tint, 1 - tt));
    }
    g.additive = false;

    // keep characters that are passing through visible on top of the lens (room side only)
    const a = g.toScreen(c.x + uu.x, c.y + uu.y), bb = g.toScreen(c.x - uu.x + nn.x, c.y - uu.y + nn.y);
    const sx = Math.trunc(Math.min(a.x, bb.x)), sy = Math.trunc(Math.min(a.y, bb.y));
    const sw = Math.trunc(Math.abs(a.x - bb.x)) + 1, sh = Math.trunc(Math.abs(a.y - bb.y)) + 1;
    const p1 = V(c.x + uu.x + nn.x, c.y + uu.y + nn.y), p2 = V(c.x - uu.x, c.y - uu.y);
    const lensRect = { x: Math.min(p1.x, p2.x) - 2, y: Math.min(p1.y, p2.y) - 2, w: Math.abs(p1.x - p2.x) + 4, h: Math.abs(p1.y - p2.y) + 4 };
    const any = (w.player && !w.player.dead && overlaps(w.player.rect, lensRect)) || w.cubes.some((cb) => !cb.gone && overlaps(cb.rect, lensRect));
    if (!any) return;
    g.clip(sx, sy, sw, sh);
    for (const cb of w.cubes) this.drawWithClones(w, cb, () => this.drawCube(cb));
    if (w.player) this.drawWithClones(w, w.player, () => this.drawPlayer(w.player));
    g.unclip();
  }

  drawPortal(p, linked) {
    const g = this.gfx;
    const ci = p.color, li = linked ? 1 : 0;
    const frame = Math.trunc(this.time * 10 + ci * 3) % PORTAL_FRAMES;
    const open = easeOutBack(p.open);
    const col = ci === 0 ? PP.BlueRamp[2] : PP.OrangeRamp[2];

    g.additive = true;
    g.circleGrad(p.center.x + p.normal.x * 20, p.center.y + p.normal.y * 20, 70 * open, withAlpha(col, 60 / 255), withAlpha(col, 0));
    g.additive = false;

    const c = snapV(p.center);
    if (p.isWall) {
      const hgt = 80 * open;
      g.tex(Sprites.portalVTex[ci][li], frame * 12, 0, 12, 40, c.x - 12, c.y - snap(hgt / 2), 24, snap(hgt));
    } else {
      const wid = 80 * open;
      g.tex(Sprites.portalHTex[ci][li], frame * 40, 0, 40, 12, c.x - snap(wid / 2), c.y - 12, snap(wid), 24);
    }
  }

  drawAimPreview(w, p) {
    if (p.dead || p.won || w.def.gun === GunMode.None) return;
    const g = this.gfx;
    const origin = World.gunOrigin(p);
    const { hit, candidate: candB } = w.previewShot(origin, p.aim, PortalColor.Blue);
    const cand = candB ?? (w.gunAllows(PortalColor.Orange) ? w.previewShot(origin, p.aim, PortalColor.Orange).candidate : null);
    if (!hit.hit) return;
    for (let d = 30; d < hit.dist - 8; d += 12) {
      g.rect(Math.trunc(snap(origin.x + p.aim.x * d)), Math.trunc(snap(origin.y + p.aim.y * d)), 2, 2, C(255, 255, 255, 90 * (1 - d / (hit.dist + 1)) + 30));
    }
    if (cand) {
      const blink = Math.trunc(this.time * 4) % 2 === 0;
      const c = snapV(cand.center);
      const col = C(255, 255, 255, blink ? 220 : 120);
      for (let k = -16; k <= 16; k += 4) g.rect(Math.trunc(c.x + cand.up.x * k * 2), Math.trunc(c.y + cand.up.y * k * 2), 2, 2, col);
      for (const s of [-1, 1]) {
        const end = V(c.x + cand.up.x * 34 * s, c.y + cand.up.y * 34 * s);
        g.rect(Math.trunc(snap(end.x)) - 2, Math.trunc(snap(end.y)) - 2, 6, 6, col);
      }
    } else {
      const pt = snapV(V(hit.point.x + hit.normal.x * 6, hit.point.y + hit.normal.y * 6));
      const red = C(255, 90, 80, 230);
      for (let k = -2; k <= 2; k++) {
        g.rect(Math.trunc(pt.x) + k * 2, Math.trunc(pt.y) + k * 2, 2, 2, red);
        g.rect(Math.trunc(pt.x) + k * 2, Math.trunc(pt.y) - k * 2, 2, 2, red);
      }
    }
  }
}
