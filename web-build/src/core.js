// Core maths, colours and the portal transform. Pure logic: no DOM, so the physics can run under Node for tests.

export const K = {
  Tile: 32, ScreenW: 1280, ScreenH: 720, Px: 0.5, CanvasW: 640, CanvasH: 360, SimDt: 1 / 120,
};

export const TAU = Math.PI * 2;

export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
/** Round half to even, like C#'s MathF.Round (matters for pixel snapping at odd coordinates). */
export function roundEven(v) {
  const r = Math.round(v);
  return Math.abs(v % 1) === 0.5 && r % 2 !== 0 ? r - 1 : r;
}
export const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);
export const lerp = (a, b, t) => a + (b - a) * t;
export const smoothStep = (t) => { t = clamp01(t); return t * t * (3 - 2 * t); };
export const easeOutBack = (t) => {
  t = clamp01(t);
  const c1 = 1.70158, c3 = c1 + 1;
  return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2);
};
export const easeOutCubic = (t) => { t = clamp01(t); return 1 - Math.pow(1 - t, 3); };
export const approach = (v, target, delta) => (v < target ? Math.min(v + delta, target) : Math.max(v - delta, target));

// ---- vectors are plain {x, y}
export const V = (x, y) => ({ x, y });
export const vadd = (a, b) => ({ x: a.x + b.x, y: a.y + b.y });
export const vsub = (a, b) => ({ x: a.x - b.x, y: a.y - b.y });
export const vmul = (a, s) => ({ x: a.x * s, y: a.y * s });
export const vdot = (a, b) => a.x * b.x + a.y * b.y;
export const vlen = (a) => Math.hypot(a.x, a.y);
export const vdist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
export const vdist2 = (a, b) => (a.x - b.x) ** 2 + (a.y - b.y) ** 2;
export const safeNormalize = (v, fallback) => {
  const l = Math.hypot(v.x, v.y);
  return l < 1e-5 ? fallback : { x: v.x / l, y: v.y / l };
};

/** Deterministic per-cell hash in [0,1) (same bit-twiddling as the C# version). */
export function hash(x, y, seed = 0) {
  let h = (Math.imul(x | 0, 374761393) + Math.imul(y | 0, 668265263) + Math.imul(seed | 0, 2147483647)) >>> 0;
  h = Math.imul((h ^ (h >>> 13)) >>> 0, 1274126177) >>> 0;
  h = (h ^ (h >>> 16)) >>> 0;
  return (h & 0xFFFFFF) / 0x1000000;
}

// ---- colours: {r,g,b,a} with a cached css string
export function C(r, g, b, a = 255) {
  r = clamp(Math.round(r), 0, 255); g = clamp(Math.round(g), 0, 255); b = clamp(Math.round(b), 0, 255); a = clamp(Math.round(a), 0, 255);
  return { r, g, b, a, css: `rgba(${r},${g},${b},${a / 255})`, rgb: `rgb(${r},${g},${b})` };
}
/** alpha as 0..1 multiplier of the colour's own alpha */
export const withAlpha = (c, a) => C(c.r, c.g, c.b, a * 255);
export const withAlphaInt = (c, a) => C(c.r, c.g, c.b, a);
export const mix = (a, b, t) => {
  t = clamp01(t);
  return C(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
};
export const WHITE = C(255, 255, 255);
export const BLACK = C(0, 0, 0);
export const CLEAR = C(0, 0, 0, 0);

export const overlaps = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
export const R = (x, y, w, h) => ({ x, y, w, h });
export const contains = (r, p) => p.x >= r.x && p.x <= r.x + r.w && p.y >= r.y && p.y <= r.y + r.h;

/** Ray vs axis-aligned box (slab test). Returns entry distance or -1. */
export function rayAabb(o, d, r) {
  let tmin = 0, tmax = Number.MAX_VALUE;
  if (Math.abs(d.x) < 1e-6) { if (o.x < r.x || o.x > r.x + r.w) return -1; }
  else {
    let t1 = (r.x - o.x) / d.x, t2 = (r.x + r.w - o.x) / d.x;
    if (t1 > t2) [t1, t2] = [t2, t1];
    tmin = Math.max(tmin, t1); tmax = Math.min(tmax, t2);
  }
  if (Math.abs(d.y) < 1e-6) { if (o.y < r.y || o.y > r.y + r.h) return -1; }
  else {
    let t1 = (r.y - o.y) / d.y, t2 = (r.y + r.h - o.y) / d.y;
    if (t1 > t2) [t1, t2] = [t2, t1];
    tmin = Math.max(tmin, t1); tmax = Math.min(tmax, t2);
  }
  return tmax >= tmin ? tmin : -1;
}

// ---- palette used by gameplay effects (see PP in art.js for the pixel-art palette)
export const Palette = {
  Blue: C(56, 168, 255), Orange: C(255, 146, 38), White: C(255, 255, 255), Ink: C(14, 17, 22),
  Goo: C(74, 112, 32), GooLight: C(170, 220, 90), Fizzler: C(120, 200, 255), LaserRed: C(255, 54, 54),
  Green: C(80, 230, 140), Dust: C(200, 205, 210, 160),
};

export const PortalColor = { Blue: 0, Orange: 1 };
export const HALF_LEN = 32;

/**
 * A portal lives on the face of two adjacent solid tiles: a centre on the wall surface, an outward normal and an
 * in-plane "up". normal(A) -> -normal(B), up(A) -> up(B). The same transform drives teleporting, momentum, lasers
 * and the see-through rendering.
 */
export class Portal {
  constructor(color, center, normal, a, b, isFixed) {
    this.color = color;
    this.center = center;
    this.normal = normal;
    this.up = Math.abs(normal.x) > 0.5 ? V(0, -1) : V(1, 0);
    this.tileA = a; this.tileB = b;
    this.fixed = isFixed;
    this.open = 0;
    this.age = 0;
  }
  get isWall() { return Math.abs(this.normal.x) > 0.5; }
  covers(x, y) { return (this.tileA.x === x && this.tileA.y === y) || (this.tileB.x === x && this.tileB.y === y); }
  u(p) { return (p.x - this.center.x) * this.up.x + (p.y - this.center.y) * this.up.y; }
  v(p) { return (p.x - this.center.x) * this.normal.x + (p.y - this.center.y) * this.normal.y; }

  static mapDir(a, b, d) {
    const n = d.x * a.normal.x + d.y * a.normal.y;
    const u = d.x * a.up.x + d.y * a.up.y;
    return { x: -n * b.normal.x + u * b.up.x, y: -n * b.normal.y + u * b.up.y };
  }
  static mapPoint(a, b, p) {
    const d = Portal.mapDir(a, b, { x: p.x - a.center.x, y: p.y - a.center.y });
    return { x: b.center.x + d.x, y: b.center.y + d.y };
  }
  /** Canvas-style affine matrix [a,b,c,d,e,f] for the map a -> b. */
  static matrix(a, b) {
    const ex = Portal.mapDir(a, b, { x: 1, y: 0 });
    const ey = Portal.mapDir(a, b, { x: 0, y: 1 });
    return [ex.x, ex.y, ey.x, ey.y, b.center.x - (ex.x * a.center.x + ey.x * a.center.y), b.center.y - (ex.y * a.center.x + ey.y * a.center.y)];
  }
}
