// Ghosts: your best run recorded as a light-weight path, and the reference solution replayed through a real World.
// Both end up as a "ghost player" object the renderer can draw with the normal player art.
import { K, V } from './core.js';
import { World, stepFrame } from './world.js';

const HZ = 20;                       // recorded samples per second
const AIM_STEPS = 64;
const MAX_SAMPLES = HZ * 180;        // never record more than three minutes

/** Records the player's pose a few times a second while a chamber is played. */
export class GhostRecorder {
  constructor() { this.reset(); }
  reset() { this.data = []; this.px = 0; this.py = 0; this.steps = 0; this.full = false; }

  /** Call once per sim step (1/120 s). */
  step(p) {
    if (this.full || !p || p.dead) return;
    if (this.steps++ % (Math.round(1 / (K.SimDt * HZ))) !== 0) return;
    if (this.data.length >= MAX_SAMPLES * 3) { this.full = true; return; }
    const x = Math.round(p.pos.x), y = Math.round(p.pos.y);
    const aim = Math.round(((Math.atan2(p.aim.y, p.aim.x) / (Math.PI * 2) + 1) % 1) * AIM_STEPS) % AIM_STEPS;
    const flags = (p.onGround ? 1 : 0) | (Math.abs(p.vel.x) > 10 ? 2 : 0) | (p.vel.y < 0 ? 4 : 0) | (p.facing > 0 ? 8 : 0) | (p.lastShot === 1 ? 16 : 0) | (aim << 5);
    this.data.push(x - this.px, y - this.py, flags);
    this.px = x; this.py = y;
  }

  /** The recording as a compact string for the save file (or null if it is empty or too long to keep). */
  encode() {
    if (this.data.length < 9 || this.full) return null;
    const s = this.data.join(',');
    return s.length > 60000 ? null : s;
  }
}

/** A saved recording being played back: at(t) gives a pose the renderer can draw. */
export class GhostPlayback {
  constructor(str) {
    const a = String(str).split(',').map(Number);
    this.n = Math.floor(a.length / 3);
    this.xs = new Float32Array(this.n); this.ys = new Float32Array(this.n); this.fl = new Int32Array(this.n);
    let x = 0, y = 0;
    for (let i = 0; i < this.n; i++) { x += a[i * 3]; y += a[i * 3 + 1]; this.xs[i] = x; this.ys[i] = y; this.fl[i] = a[i * 3 + 2]; }
    this.dist = 0; this.lastI = -1;
    this.pose = makePose();
  }
  get duration() { return this.n / HZ; }

  at(t) {
    if (this.n === 0) return null;
    const f = Math.min(t * HZ, this.n - 1), i = Math.floor(f), j = Math.min(i + 1, this.n - 1), k = f - i;
    const p = this.pose;
    p.pos.x = this.xs[i] + (this.xs[j] - this.xs[i]) * k;
    p.pos.y = this.ys[i] + (this.ys[j] - this.ys[i]) * k;
    const fl = this.fl[i];
    p.onGround = !!(fl & 1);
    p.vel.x = fl & 2 ? (fl & 8 ? 100 : -100) : 0;
    p.vel.y = fl & 4 ? -100 : 100;
    p.facing = fl & 8 ? 1 : -1;
    p.lastShot = fl & 16 ? 1 : 0;
    const ang = ((fl >> 5) / AIM_STEPS) * Math.PI * 2;
    p.aim.x = Math.cos(ang); p.aim.y = Math.sin(ang);
    if (i !== this.lastI) { this.dist += Math.abs(this.xs[j] - this.xs[i]); this.lastI = i; }
    p.walkPhase = this.dist * 0.045 * 1.4;
    return p;
  }
  get done() { return false; }
}

/** The reference solution, replayed frame by frame through its own World; the ghost is that world's player. */
export class SolutionGhost {
  constructor(def, inputs) {
    this.world = new World(def);
    this.frames = [];
    for (const part of inputs.split(';')) {
      if (!part) continue;
      const [tok, n] = part.split('x');
      const [m, f, ax, ay] = tok.split(',');
      const fl = +f;
      const inp = {
        moveX: +m, jumpPressed: !!(fl & 1), jumpHeld: !!(fl & 2), usePressed: !!(fl & 4), fireBlue: !!(fl & 8),
        fireOrange: !!(fl & 16), fireCycle: !!(fl & 32), aimWorld: { x: +ax, y: +ay },
      };
      for (let i = 0; i < +n; i++) this.frames.push(inp);
    }
    this.i = 0; this.acc = 0;
  }
  /** Advances by real seconds (the solution was recorded at 60 frames per second). */
  advance(dt) {
    this.acc += dt;
    while (this.acc >= 1 / 60 && this.i < this.frames.length) {
      stepFrame(this.world, this.frames[this.i++]);
      this.acc -= 1 / 60;
    }
  }
  get pose() { return this.world.player; }
  get done() { return this.i >= this.frames.length; }
}

function makePose() {
  return {
    pos: V(0, 0), vel: V(0, 0), half: V(11, 15), onGround: true, facing: 1, aim: V(1, 0), walkPhase: 0,
    dead: false, won: false, teleportFlash: 0, gunKick: 0, lastShot: 0, holding: null,
  };
}
