// A scripted player for making solutions fast. Write a level, then drive it with a few macros (walkTo, jump, shoot,
// use...). The bot plays the real physics, so every macro reacts to where things actually are, and what it did is
// recorded as the same per-frame input string the replay test and the in-game solution ghost use.
//
//   const bot = new Bot(rawDef);
//   bot.walkTo(200); bot.shoot('blue', 640, 384); bot.finish();
//   bot.inputs()   // "m,flags,ax,ay xN;..."   bot.won   bot.frames
import { World, makeDef, stepFrame } from '../src/world.js';

export class Bot {
  constructor(raw) {
    this.w = new World(makeDef(raw));
    this.log = [];            // one token per frame
    this.moveX = 0;
    this.aim = { x: 0, y: 0 };
    this.aimFn = null;        // set to a function to re-aim every frame (e.g. keep a shield pointed at a turret)
    this.jumpHeld = false;
    this.p.aim = { x: 1, y: 0 };
  }
  get p() { return this.w.player; }
  get won() { return this.w.player.won; }
  get frames() { return this.log.length; }

  /** Advances one frame. flags: jump, use, blue, orange. */
  frame(f = {}) {
    const p = this.p;
    if (p.dead) throw new Error(`player died at frame ${this.log.length} (x ${Math.round(p.pos.x)}, y ${Math.round(p.pos.y)})`);
    const a = this.aimFn ? this.aimFn(this) : this.aim;
    const bits = (f.jump ? 1 : 0) | (this.jumpHeld || f.jump ? 2 : 0) | (f.use ? 4 : 0) | (f.blue ? 8 : 0) | (f.orange ? 16 : 0);
    const inp = {
      moveX: this.moveX, jumpPressed: !!f.jump, jumpHeld: !!(this.jumpHeld || f.jump), usePressed: !!f.use, fireBlue: !!f.blue,
      fireOrange: !!f.orange, fireCycle: false, aimWorld: { x: a.x, y: a.y },
    };
    stepFrame(this.w, inp);
    this.log.push(`${this.moveX},${bits},${a.x},${a.y}`);
  }

  wait(n = 1) { for (let i = 0; i < n; i++) this.frame(); }
  aimAt(x, y) { this.aim = { x, y }; this.aimFn = null; }
  stop() { this.moveX = 0; }

  /** Runs frames until cond(bot) is true (or throws after max frames). */
  until(cond, max = 900, what = 'condition') {
    for (let i = 0; i < max; i++) { if (cond(this)) return; this.frame(); }
    if (!cond(this)) throw new Error(`gave up waiting for ${what} (x ${Math.round(this.p.pos.x)}, y ${Math.round(this.p.pos.y)})`);
  }

  /** Walk along the ground until the player's x is within tol of x. */
  walkTo(x, tol = 4, max = 900) {
    for (let i = 0; i < max; i++) {
      const dx = x - this.p.pos.x;
      if (Math.abs(dx) <= tol || this.p.won) break;
      this.moveX = Math.sign(dx);
      this.frame();
    }
    this.moveX = 0;
    this.wait(6);   // let the slide settle
  }

  /** Hold a direction until cond is true, without stopping afterwards. */
  hold(dir, cond, max = 600, what = 'condition') { this.moveX = dir; this.until(cond, max, what); }

  jump(hold = 20) {
    this.frame({ jump: true });
    this.jumpHeld = true;
    for (let i = 0; i < hold; i++) this.frame();
    this.jumpHeld = false;
  }

  /** Fire a portal ('blue' or 'orange') at a world point. */
  shoot(color, x, y) {
    this.aimAt(x, y);
    this.frame();
    this.frame(color === 'blue' ? { blue: true } : { orange: true });
    this.wait(16);   // the gun has a short cooldown
  }

  use(x, y) {
    if (x !== undefined) this.aimAt(x, y);
    this.frame({ use: true });
    this.wait(2);
  }

  /** Keep aiming at a moving or fixed point every frame. */
  track(fn) { this.aimFn = fn; }
  untrack() { this.aimFn = null; }

  /** Walk to the exit tile and wait to win. */
  finish(exitX, max = 900) {
    if (!this.p.won) this.walkTo(exitX, 6);
    this.until((b) => b.won, 120, 'the win');
  }

  /** The recording as "move,flags,ax,ay xN;" tokens (run-length encoded). */
  inputs() {
    const out = [];
    for (let i = 0; i < this.log.length;) {
      let j = i;
      while (j < this.log.length && this.log[j] === this.log[i]) j++;
      out.push(`${this.log[i]}x${j - i};`);
      i = j;
    }
    return out.join('');
  }
}

/** Replays an inputs string against a fresh world; returns { won, frames, dead }. Used by the tests and tools. */
export function replay(raw, inputs, extra = 2) {
  const w = new World(makeDef(raw));
  let frames = 0, last = null;
  for (const part of inputs.split(';')) {
    if (!part) continue;
    const [tok, n] = part.split('x');
    const [m, f, ax, ay] = tok.split(',');
    const fl = +f;
    const inp = {
      moveX: +m, jumpPressed: !!(fl & 1), jumpHeld: !!(fl & 2), usePressed: !!(fl & 4), fireBlue: !!(fl & 8),
      fireOrange: !!(fl & 16), fireCycle: !!(fl & 32), aimWorld: { x: +ax, y: +ay },
    };
    for (let i = 0; i < +n; i++) {
      last = inp; stepFrame(w, inp); frames++;
      if (w.player.won || w.player.dead) return { won: w.player.won, dead: w.player.dead, frames };
    }
  }
  for (let i = 0; i < extra && !w.player.won && !w.player.dead && last; i++) { stepFrame(w, { ...last, jumpPressed: false, usePressed: false, fireBlue: false, fireOrange: false }); frames++; }
  return { won: w.player.won, dead: w.player.dead, frames };
}
