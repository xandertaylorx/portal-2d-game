// Canvas 2D stand-in for the bits of Raylib the game uses. Everything is drawn in "virtual" units (1280x720 for the
// UI, world units for the scene) through a tiny matrix stack, onto a 640x360 pixel canvas that CSS scales up.
import { K, C, CLEAR, clamp, roundEven } from './core.js';

// ------------------------------------------------------------------ CPU pixel buffer

/** All pixel art (tiles, sprites, static level layers) is painted into these, then uploaded as canvases. */
export class Pix {
  constructor(w, h) {
    this.w = w; this.h = h;
    this.d = new Uint8ClampedArray(w * h * 4);
  }
  inside(x, y) { return x >= 0 && y >= 0 && x < this.w && y < this.h; }
  get(x, y) {
    if (!this.inside(x, y)) return CLEAR;
    const i = (y * this.w + x) * 4;
    return { r: this.d[i], g: this.d[i + 1], b: this.d[i + 2], a: this.d[i + 3] };
  }
  set(x, y, c) {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return;
    const i = (y * this.w + x) * 4;
    this.d[i] = c.r; this.d[i + 1] = c.g; this.d[i + 2] = c.b; this.d[i + 3] = c.a;
  }
  /** Alpha-blend a colour over the existing pixel. */
  blend(x, y, c) {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h || c.a === 0) return;
    const i = (y * this.w + x) * 4;
    if (c.a === 255) { this.d[i] = c.r; this.d[i + 1] = c.g; this.d[i + 2] = c.b; this.d[i + 3] = 255; return; }
    const a = c.a / 255, ia = 1 - a, da = this.d[i + 3] / 255;
    const outA = a + da * ia;
    this.d[i] = Math.trunc(c.r * a + this.d[i] * ia);
    this.d[i + 1] = Math.trunc(c.g * a + this.d[i + 1] * ia);
    this.d[i + 2] = Math.trunc(c.b * a + this.d[i + 2] * ia);
    this.d[i + 3] = clamp(Math.trunc(outA * 255), 0, 255);
  }
  rect(x, y, w, h, c) { for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) this.set(i, j, c); }
  blendRect(x, y, w, h, c) { for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) this.blend(i, j, c); }
  hLine(x, y, w, c) { this.rect(x, y, w, 1, c); }
  vLine(x, y, h, c) { this.rect(x, y, 1, h, c); }
  /** Darken an existing pixel. */
  shade(x, y, amount) {
    if (!this.inside(x, y)) return;
    const i = (y * this.w + x) * 4, k = 1 - amount;
    this.d[i] = clamp(this.d[i] * k, 0, 255); this.d[i + 1] = clamp(this.d[i + 1] * k, 0, 255); this.d[i + 2] = clamp(this.d[i + 2] * k, 0, 255);
  }
  draw(src, dx, dy) {
    for (let y = 0; y < src.h; y++) for (let x = 0; x < src.w; x++) this.blend(dx + x, dy + y, src.get(x, y));
  }
  /** Add a 1px outline around every opaque pixel (classic sprite outline). */
  outline(c) {
    const copy = this.d.slice();
    for (let y = 0; y < this.h; y++) for (let x = 0; x < this.w; x++) {
      if (copy[(y * this.w + x) * 4 + 3] > 0) continue;
      let near = false;
      for (let k = 0; k < 4 && !near; k++) {
        const nx = x + (k === 0 ? 1 : k === 1 ? -1 : 0), ny = y + (k === 2 ? 1 : k === 3 ? -1 : 0);
        if (nx >= 0 && ny >= 0 && nx < this.w && ny < this.h && copy[(ny * this.w + nx) * 4 + 3] > 200) near = true;
      }
      if (near) this.set(x, y, c);
    }
  }
  toCanvas() {
    const cv = makeCanvas(this.w, this.h);
    const ctx = cv.getContext('2d');
    ctx.putImageData(new ImageData(this.d, this.w, this.h), 0, 0);
    return cv;
  }
}

export function makeCanvas(w, h) {
  const cv = document.createElement('canvas');
  cv.width = w; cv.height = h;
  return cv;
}

// ------------------------------------------------------------------ bitmap font

const GW = 5, GH = 7, CW = 6, CH = 8;
const GLYPHS = {
  A: [0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11], B: [0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E],
  C: [0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E], D: [0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E],
  E: [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F], F: [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10],
  G: [0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F], H: [0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11],
  I: [0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E], J: [0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C],
  K: [0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11], L: [0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F],
  M: [0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11], N: [0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11],
  O: [0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E], P: [0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10],
  Q: [0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D], R: [0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11],
  S: [0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E], T: [0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
  U: [0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E], V: [0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04],
  W: [0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A], X: [0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11],
  Y: [0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04], Z: [0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F],
  0: [0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E], 1: [0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E],
  2: [0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F], 3: [0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E],
  4: [0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02], 5: [0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E],
  6: [0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E], 7: [0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08],
  8: [0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E], 9: [0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C],
  ' ': [0, 0, 0, 0, 0, 0, 0], '!': [0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04],
  '"': [0x0A, 0x0A, 0, 0, 0, 0, 0], '#': [0x0A, 0x0A, 0x1F, 0x0A, 0x1F, 0x0A, 0x0A],
  '%': [0x18, 0x19, 0x02, 0x04, 0x08, 0x13, 0x03], '&': [0x0C, 0x12, 0x14, 0x08, 0x15, 0x12, 0x0D],
  "'": [0x04, 0x04, 0, 0, 0, 0, 0], '(': [0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02],
  ')': [0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08], '*': [0x00, 0x04, 0x15, 0x0E, 0x15, 0x04, 0x00],
  '+': [0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00], ',': [0, 0, 0, 0, 0x0C, 0x04, 0x08],
  '-': [0, 0, 0, 0x1F, 0, 0, 0], '.': [0, 0, 0, 0, 0, 0x0C, 0x0C],
  '/': [0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x00], ':': [0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00],
  ';': [0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x04, 0x08], '<': [0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02],
  '=': [0x00, 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00], '>': [0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08],
  '?': [0x0E, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04], '@': [0x0E, 0x11, 0x01, 0x0D, 0x15, 0x15, 0x0E],
  '[': [0x0E, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0E], ']': [0x0E, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0E],
  _: [0, 0, 0, 0, 0, 0, 0x1F], '|': [0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
};
const ORDER = Object.keys(GLYPHS).join('');
const glyphOf = (ch) => GLYPHS[ch.toUpperCase()] || GLYPHS['?'];
const WHITE = C(255, 255, 255);

function paintGlyph(p, ch, x, y, s, col) {
  const g = glyphOf(ch);
  for (let r = 0; r < GH; r++) for (let b = 0; b < GW; b++) if ((g[r] >> (GW - 1 - b)) & 1) p.rect(x + b * s, y + r * s, s, s, col);
}

export const PixelFont = {
  atlas: null,
  tinted: new Map(),
  load() {
    const p = new Pix(ORDER.length * CW, CH);
    for (let i = 0; i < ORDER.length; i++) paintGlyph(p, ORDER[i], i * CW, 0, 1, WHITE);
    this.atlas = p.toCanvas();
  },
  /** Size in virtual units at font scale s (canvas pixels per font pixel). */
  measure(text, s) { return { x: Math.max(0, text.length * CW - 1) * s / K.Px, y: GH * s / K.Px }; },
  drawPix(p, text, x, y, s, col) { for (const ch of text) { paintGlyph(p, ch, x, y, s, col); x += CW * s; } },
  pixWidth(text, s) { return Math.max(0, text.length * CW - 1) * s; },
  tint(col) {
    let t = this.tinted.get(col.rgb);
    if (!t) {
      if (this.tinted.size > 400) this.tinted.clear();
      t = makeCanvas(this.atlas.width, this.atlas.height);
      const c = t.getContext('2d');
      c.drawImage(this.atlas, 0, 0);
      c.globalCompositeOperation = 'source-in';
      c.fillStyle = col.rgb;
      c.fillRect(0, 0, t.width, t.height);
      this.tinted.set(col.rgb, t);
    }
    return t;
  },
  draw(g, text, pos, s, col) {
    if (col.a === 0) return;
    const unit = s / K.Px;
    let x = roundEven(pos.x * K.Px) / K.Px, y = roundEven(pos.y * K.Px) / K.Px;
    const tex = this.tint(col), alpha = col.a / 255;
    for (const raw of text) {
      if (raw !== ' ') {
        let idx = ORDER.indexOf(raw.toUpperCase());
        if (idx < 0) idx = ORDER.indexOf('?');
        g.tex(tex, idx * CW, 0, GW, GH, x, y, GW * unit, GH * unit, alpha);
      }
      x += CW * unit;
    }
  },
};

// ------------------------------------------------------------------ the canvas wrapper

export class Gfx {
  constructor(canvas) {
    this.setTarget(canvas);
    this.m = [1, 0, 0, 1, 0, 0];
    this.stack = [];
    this.opacity = 1;      // multiplies everything drawn (used to fade HUD panels under the cursor)
  }
  setTarget(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d', { willReadFrequently: canvas.dataset?.readback === '1' });
    this.ctx.imageSmoothingEnabled = false;
    this.ctx.setTransform(1, 0, 0, 1, 0, 0);
  }
  // ---- matrix
  /** The pixel-art camera: virtual units -> canvas pixels. */
  camera(cam) { this.m = [cam.zoom, 0, 0, cam.zoom, cam.offset.x - cam.target.x * cam.zoom, cam.offset.y - cam.target.y * cam.zoom]; this.stack.length = 0; }
  identity(zoom = K.Px) { this.m = [zoom, 0, 0, zoom, 0, 0]; this.stack.length = 0; }
  push() { this.stack.push(this.m.slice()); }
  pop() { this.m = this.stack.pop(); }
  mult(n) {
    const [a, b, c, d, e, f] = this.m;
    this.m = [a * n[0] + c * n[1], b * n[0] + d * n[1], a * n[2] + c * n[3], b * n[2] + d * n[3], a * n[4] + c * n[5] + e, b * n[4] + d * n[5] + f];
  }
  toScreen(x, y) { return { x: this.m[0] * x + this.m[2] * y + this.m[4], y: this.m[1] * x + this.m[3] * y + this.m[5] }; }
  toWorld(x, y) {
    const [a, b, c, d, e, f] = this.m, det = a * d - b * c;
    return { x: (d * (x - e) - c * (y - f)) / det, y: (a * (y - f) - b * (x - e)) / det };
  }
  set additive(on) { this.ctx.globalCompositeOperation = on ? 'lighter' : 'source-over'; }
  _local() { const m = this.m; this.ctx.setTransform(m[0], m[1], m[2], m[3], m[4], m[5]); }
  _reset() { this.ctx.setTransform(1, 0, 0, 1, 0, 0); }

  // ---- primitives
  clear(color) {
    this._reset();
    this.ctx.globalAlpha = 1;
    this.ctx.fillStyle = color.css;
    this.ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);
  }
  /** Axis-aligned filled rectangle snapped to the pixel grid (matches GL pixel-centre coverage). */
  rect(x, y, w, h, color) {
    if (color.a === 0 || w === 0 || h === 0) return;
    const m = this.m;
    const ax = m[0] * x + m[2] * y + m[4], ay = m[1] * x + m[3] * y + m[5];
    const bx = m[0] * (x + w) + m[2] * (y + h) + m[4], by = m[1] * (x + w) + m[3] * (y + h) + m[5];
    const x0 = Math.ceil(Math.min(ax, bx) - 0.5 - 1e-6), x1 = Math.ceil(Math.max(ax, bx) - 0.5 - 1e-6);
    const y0 = Math.ceil(Math.min(ay, by) - 0.5 - 1e-6), y1 = Math.ceil(Math.max(ay, by) - 0.5 - 1e-6);
    if (x1 <= x0 || y1 <= y0) return;
    const ctx = this.ctx;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalAlpha = this.opacity;
    ctx.fillStyle = color.css;
    ctx.fillRect(x0, y0, x1 - x0, y1 - y0);
  }
  rectR(r, color) { this.rect(r.x, r.y, r.w, r.h, color); }
  /** Raylib draws these borders inside the rectangle. */
  rectLines(r, t, color) {
    this.rect(r.x, r.y, r.w, t, color);
    this.rect(r.x, r.y + r.h - t, r.w, t, color);
    this.rect(r.x, r.y + t, t, r.h - 2 * t, color);
    this.rect(r.x + r.w - t, r.y + t, t, r.h - 2 * t, color);
  }
  rectGradV(x, y, w, h, top, bottom) {
    const ctx = this.ctx;
    this._local();
    const g = ctx.createLinearGradient(0, y, 0, y + h);
    g.addColorStop(0, top.css); g.addColorStop(1, bottom.css);
    ctx.globalAlpha = this.opacity;
    ctx.fillStyle = g;
    ctx.fillRect(x, y, w, h);
    this._reset();
  }
  rectGradH(x, y, w, h, left, right) {
    const ctx = this.ctx;
    this._local();
    const g = ctx.createLinearGradient(x, 0, x + w, 0);
    g.addColorStop(0, left.css); g.addColorStop(1, right.css);
    ctx.globalAlpha = this.opacity;
    ctx.fillStyle = g;
    ctx.fillRect(x, y, w, h);
    this._reset();
  }
  /** A quad filled with a vertical gradient (used for the lamp light cones). */
  quadGradV(pts, y0, y1, near, far) {
    const ctx = this.ctx;
    this._local();
    const g = ctx.createLinearGradient(0, y0, 0, y1);
    g.addColorStop(0, near.css); g.addColorStop(1, far.css);
    ctx.globalAlpha = this.opacity;
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
    ctx.closePath();
    ctx.fill();
    this._reset();
  }
  circleGrad(cx, cy, r, inner, outer) {
    if (r <= 0) return;
    const ctx = this.ctx;
    this._local();
    const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, r);
    g.addColorStop(0, inner.css); g.addColorStop(1, outer.css);
    ctx.globalAlpha = this.opacity;
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.fill();
    this._reset();
  }
  circle(cx, cy, r, color) {
    const ctx = this.ctx;
    this._local();
    ctx.globalAlpha = this.opacity; ctx.fillStyle = color.css;
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.fill();
    this._reset();
  }
  line(a, b, thick, color) {
    const ctx = this.ctx;
    this._local();
    ctx.globalAlpha = this.opacity; ctx.strokeStyle = color.css; ctx.lineWidth = thick;
    ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
    this._reset();
  }
  /** Draw part of an image into a destination rectangle (negative sw/sh flip). alpha is 0..1. */
  tex(img, sx, sy, sw, sh, dx, dy, dw, dh, alpha = 1) {
    if (alpha <= 0) return;
    const ctx = this.ctx, m = this.m;
    const flipX = sw < 0, flipY = sh < 0;
    sw = Math.abs(sw); sh = Math.abs(sh);
    ctx.globalAlpha = alpha * this.opacity;
    if (m[1] === 0 && m[2] === 0) {
      const ax = m[0] * dx + m[4], ay = m[3] * dy + m[5], bx = m[0] * (dx + dw) + m[4], by = m[3] * (dy + dh) + m[5];
      const x0 = Math.ceil(Math.min(ax, bx) - 0.5 - 1e-6), x1 = Math.ceil(Math.max(ax, bx) - 0.5 - 1e-6);
      const y0 = Math.ceil(Math.min(ay, by) - 0.5 - 1e-6), y1 = Math.ceil(Math.max(ay, by) - 0.5 - 1e-6);
      if (x1 <= x0 || y1 <= y0) return;
      const fx = flipX !== (m[0] * dw < 0), fy = flipY !== (m[3] * dh < 0);
      ctx.setTransform(fx ? -1 : 1, 0, 0, fy ? -1 : 1, fx ? x1 : x0, fy ? y1 : y0);
      ctx.drawImage(img, sx, sy, sw, sh, 0, 0, x1 - x0, y1 - y0);
    } else {
      this._local();
      ctx.translate(dx, dy);
      if (flipX) { ctx.translate(dw, 0); ctx.scale(-1, 1); }
      if (flipY) { ctx.translate(0, dh); ctx.scale(1, -1); }
      ctx.drawImage(img, sx, sy, sw, sh, 0, 0, dw, dh);
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalAlpha = this.opacity;
  }
  /** Clip to a canvas-pixel rectangle until unclip(). */
  clip(x, y, w, h) {
    const ctx = this.ctx;
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.beginPath(); ctx.rect(x, y, w, h); ctx.clip();
  }
  unclip() { this.ctx.restore(); }
}
