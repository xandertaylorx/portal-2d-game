// Boss fight: COLOSSUS, a giant mech standing in a pit between two ledges. Your portal gun is the only weapon that
// can hurt it: it lobs mortar shells at you, and when its armour vents to fire, the hatch on top of its dome is open.
// Put a blue portal on the floor where a shell will land and an orange one on the ceiling above the mech, and its own
// shell falls down its own hatch. It gets angrier in three phases (shockwaves, then a portal purge and twin shells).
import { K, C, V, withAlpha, mix, clamp, clamp01, Portal, HALF_LEN } from './core.js';
import { World, makeDef, emptyInput, Sfx, PKind } from './world.js';
import { PP } from './art.js';

const T = K.Tile;
export const ARENA_W = 32, ARENA_H = 18;
const LEDGE_Y = 12 * T;                       // y of the ledge floors
const PIT_L = 12 * T, PIT_R = 20 * T;         // the pit between the ledges, in world units
const BOSS_X = (PIT_L + PIT_R) / 2;           // 512
const CORE = V(BOSS_X, 196), CORE_R = 44;     // the hatch on top of the dome
const GRAV = 600;
const MAX_HP = 100, HIT = 12;
const HEARTS = 5;

/** The arena: two ledges of portal panels over a pit of goo, a ceiling panel above the mech, cubes and dodge platforms. */
export function bossArena() {
  const g = Array.from({ length: ARENA_H }, (_, y) => Array.from({ length: ARENA_W }, (_, x) => (y === 0 || y === ARENA_H - 1 || x === 0 || x === ARENA_W - 1 ? 'X' : ' ')));
  const set = (x, y, c) => { g[y][x] = c; };
  const fill = (x0, y0, x1, y1, c) => { for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) set(x, y, c); };
  fill(15, 0, 16, 0, '#');                                                   // the one ceiling panel above the mech's hatch takes portals
  fill(0, 4, 0, 11, '#'); fill(31, 4, 31, 11, '#');                          // and so do the outer walls
  for (const [a, b] of [[1, 11], [20, 30]]) { fill(a, 12, b, 12, '#'); fill(a, 13, b, 17, 'X'); }
  fill(12, 16, 19, 17, '~');                                                 // the bottom of the pit
  fill(2, 9, 4, 9, 'X'); fill(27, 9, 29, 9, 'X');                            // a low platform on each ledge to jump onto
  set(7, 11, 'P'); set(5, 11, 'C'); set(10, 11, 'C'); set(21, 11, 'C'); set(26, 11, 'C');
  return makeDef({ name: 'COLOSSUS', map: g.map((r) => r.join('')), gun: 2, blue: [1, 0], orange: [1, 0], hints: [], theme: 5 });
}

export class Boss {
  constructor(save, hooks = {}) {
    this.save = save; this.hooks = hooks;
    this.best = save.bossBest || 0;
    this.attempts = 0;
    this.begin(true);
  }

  /** Starts (or restarts from a checkpoint) the fight. */
  begin(fresh) {
    if (fresh) { this.hp = MAX_HP; this.checkpoint = MAX_HP; this.time = 0; this.attempts = 1; this.totalHits = 0; }
    else this.hp = this.checkpoint;
    this.world = new World(bossArena());
    this.world.fightHooks = null;
    const p = this.world.player;
    this.hearts = HEARTS; this.invuln = 0; this.hurtFlash = 0;
    this.shells = []; this.waves = []; this.puffs = [];
    this.hatch = 0; this.hatchT = 0; this.flash = 0; this.recoil = [0, 0]; this.eye = 1; this.slam = 0;
    this.nextShell = 4.5; this.nextWave = 7; this.nextPurge = 16; this.purgeWarn = 0; this.second = 0;
    this.fightT = 0; this.phaseT = 0; this.phase = 'intro'; this.rise = 1; this.message = ''; this.msgT = 0; this.freeze = 0;
    this.pend = { jump: false, use: false, blue: false, orange: false, cycle: false }; this.acc = 0;
    this.dying = 0; this.matchWon = false; this.shown = new Set();
    this.hint = ''; this.hintT = 0;
    this.player = p;
  }

  get stage() { return this.hp > 66 ? 1 : this.hp > 33 ? 2 : 3; }
  dispose() { /* nothing global to restore */ }

  say(text, t = 2.2) { this.message = text; this.msgT = t; }
  tip(text, t = 5) { this.hint = text; this.hintT = t; }

  // ---------------------------------------------------------------- the boss's attacks

  /** Lob a mortar shell so that it lands at (tx, LEDGE_Y) after `flight` seconds. */
  lob(tx, flight, side) {
    const sx = side < 0 ? BOSS_X - 112 : BOSS_X + 112, sy = 262;
    const vx = (tx - sx) / flight, vy = (LEDGE_Y - sy - 0.5 * GRAV * flight * flight) / flight;
    this.shells.push({ x: sx, y: sy, vx, vy, age: 0, portalCd: 0, reflected: false, land: tx, trail: [] });
    this.recoil[side < 0 ? 0 : 1] = 1;
    this.world.emit(Sfx.TurretFire, V(sx, sy), 3);
    this.world.fx.burst(V(sx + side * -12, sy), C(255, 190, 90), 12, 220, 0.3, 3);
  }

  fireShells() {
    const p = this.player, side = p.pos.x < BOSS_X ? -1 : 1;
    const flight = this.stage === 1 ? 1.7 : this.stage === 2 ? 1.5 : 1.3;
    const edge = side < 0 ? [40, PIT_L - 40] : [PIT_R + 40, ARENA_W * T - 40];
    const tx = clamp(p.pos.x, edge[0], edge[1]);
    this.lob(tx, flight, side);
    if (this.stage === 3 && Math.random() < 0.6) {
      // a second shell a little further along: one portal can take only one of them
      const alt = tx + (tx - edge[0] > (edge[1] - edge[0]) / 2 ? -150 : 150);
      this.second = 0.45; this.secondTx = clamp(alt, edge[0], edge[1]); this.secondSide = side; this.secondFlight = flight;
    }
    // the armour vents a moment after firing: that is your window
    this.hatchPlan = { delay: 0.5, open: this.stage === 1 ? 3.6 : this.stage === 2 ? 3.1 : 2.6 };
  }

  slamWave() {
    const p = this.player, side = p.pos.x < BOSS_X ? -1 : 1;
    this.slam = 1;
    this.waves.push({ x: side < 0 ? PIT_L : PIT_R, dir: side, wait: 0.8, speed: 250 });
    this.world.emit(Sfx.Land, V(BOSS_X, LEDGE_Y), 6);
  }

  // ---------------------------------------------------------------- damage

  hurtPlayer(src) {
    const p = this.player;
    if (this.invuln > 0 || p.dead || this.phase !== 'fight') return;
    this.hearts--; this.invuln = 1.6; this.hurtFlash = 1;
    this.world.fx.burst(p.pos, PP.OrangeRamp[2], 22, 300, 0.5, 3.5, 500, PKind.Square, false);
    this.world.emit(Sfx.Hit, p.pos, 6, 0.05);
    p.vel.y = Math.min(p.vel.y, -260);
    if (src) { const d = Math.sign(p.pos.x - src.x) || 1; p.vel.x += d * 260; }
    if (this.hearts <= 0) this.world.kill(p, PP.OrangeRamp[2]);
  }

  hitBoss() {
    const before = this.stage;
    this.hp = Math.max(0, this.hp - HIT);
    this.totalHits++;
    this.flash = 1; this.eye = 0.2; this.hatchT = 0;
    this.freeze = 0.09;
    this.hooks.shake?.(14);
    this.world.emit(Sfx.Hit, V(CORE.x, CORE.y), 8, 0.09);
    this.world.fx.burst(CORE, C(255, 200, 90), 60, 520, 0.9, 4);
    this.world.fx.burst(CORE, C(255, 255, 255), 24, 300, 0.5, 3);
    if (this.hp <= 0) { this.phase = 'dying'; this.dying = 0; this.say('COLOSSUS DOWN', 4); return; }
    if (this.stage > before) { this.checkpoint = this.hp; this.say(this.stage === 2 ? 'PHASE 2: SHOCKWAVES' : 'PHASE 3: PORTAL PURGE'); }
    else this.say('DIRECT HIT!', 1.4);
  }

  // ---------------------------------------------------------------- shells

  explode(s, harmless) {
    const w = this.world;
    w.fx.burst(V(s.x, s.y), harmless ? C(200, 210, 230) : C(255, 170, 70), harmless ? 10 : 34, harmless ? 160 : 380, harmless ? 0.25 : 0.6, 3.5);
    if (harmless) { w.emit(Sfx.PortalFail, V(s.x, s.y), 1); return; }
    w.emit(Sfx.Hit, V(s.x, s.y), 5, 0.03);
    this.puffs.push({ x: s.x, y: s.y, t: 0 });
    const p = this.player;
    if (!p.dead && Math.hypot(p.pos.x - s.x, p.pos.y - s.y) < 62) this.hurtPlayer(s);
  }

  updateShells(dt) {
    const w = this.world, solid = (x, y) => w.isSolid(Math.floor(x / T), Math.floor(y / T));
    for (let i = this.shells.length - 1; i >= 0; i--) {
      const s = this.shells[i];
      s.age += dt; s.portalCd = Math.max(0, s.portalCd - dt);
      const px = s.x, py = s.y;
      s.vy += GRAV * dt; s.x += s.vx * dt; s.y += s.vy * dt;
      let dead = false;
      // portals: the shell goes in one and comes out of the other, keeping its speed
      if (w.linked && s.portalCd <= 0) {
        for (let k = 0; k < 2 && !dead; k++) {
          const p = w.portals[k], q = w.portals[1 - k];
          if (!p || !q) continue;
          const v0 = p.v({ x: px, y: py }), v1 = p.v({ x: s.x, y: s.y });
          if (v0 >= 0 && v1 < 0 && Math.abs(p.u({ x: s.x, y: s.y })) < HALF_LEN) {
            const np = Portal.mapPoint(p, q, { x: s.x, y: s.y }), nv = Portal.mapDir(p, q, { x: s.vx, y: s.vy });
            const sp = Math.hypot(nv.x, nv.y);
            s.x = np.x + q.normal.x * 6; s.y = np.y + q.normal.y * 6; s.vx = q.normal.x * sp; s.vy = q.normal.y * sp; s.portalCd = 0.08; s.reflected = true; s.trail.length = 0;
            p.pulse = 1; q.pulse = 1;
            w.fx.burst(p.center, k === 0 ? PP.BlueRamp[3] : PP.OrangeRamp[3], 14, 220, 0.3, 3);
            w.fx.burst(q.center, k === 0 ? PP.OrangeRamp[3] : PP.BlueRamp[3], 14, 220, 0.3, 3);
            w.emit(Sfx.Teleport, q.center, 2);
            if (s.reflected && !this.shown.has('reflected')) { this.shown.add('reflected'); this.tip('NICE. NOW GET IT INTO THE OPEN HATCH ON TOP OF ITS HEAD.', 4); }
            break;
          }
        }
      }
      // the hatch
      if (s.age > 0.25 && Math.hypot(s.x - CORE.x, (s.y - CORE.y) * 0.7) < CORE_R + 14) {
        if (this.hatch > 0.6) { this.shells.splice(i, 1); this.hitBoss(); continue; }
        this.explode(s, true); this.shells.splice(i, 1); this.world.fx.burst(CORE, C(255, 255, 255), 10, 160, 0.3, 2.5); continue;
      }
      // the rest of its armour
      if (s.age > 0.25 && s.x > BOSS_X - 90 && s.x < BOSS_X + 90 && s.y > 230 && s.y < 440) { this.explode(s, true); this.shells.splice(i, 1); continue; }
      for (const c of w.cubes) {
        if (!c.solid) continue;
        const r = c.rect;
        if (s.x > r.x && s.x < r.x + r.w && s.y > r.y && s.y < r.y + r.h) { dead = true; this.explode(s, false); c.vel.y -= 200; break; }
      }
      if (!dead && (solid(s.x, s.y) || s.y > ARENA_H * T)) { dead = true; this.explode(s, false); }
      if (!dead && !this.player.dead) {
        const r = this.player.rect;
        if (s.x > r.x - 4 && s.x < r.x + r.w + 4 && s.y > r.y - 4 && s.y < r.y + r.h + 4) { dead = true; this.explode(s, false); this.hurtPlayer(s); }
      }
      s.trail.push({ x: s.x, y: s.y }); if (s.trail.length > 9) s.trail.shift();
      if (dead) this.shells.splice(i, 1);
    }
  }

  updateWaves(dt) {
    const p = this.player;
    for (let i = this.waves.length - 1; i >= 0; i--) {
      const wv = this.waves[i];
      if (wv.wait > 0) { wv.wait -= dt; continue; }
      wv.x += wv.dir * wv.speed * dt;
      if (wv.x < 24 || wv.x > ARENA_W * T - 24) { this.waves.splice(i, 1); continue; }
      if (!p.dead && Math.abs(p.pos.x - wv.x) < 22 && p.pos.y + p.half.y > LEDGE_Y - 30 && p.pos.y + p.half.y < LEDGE_Y + 4) this.hurtPlayer({ x: wv.x, y: LEDGE_Y });
    }
  }

  // ---------------------------------------------------------------- the loop

  /** @param input  the player's input for this frame (readInput()) */
  update(dt, input) {
    if (this.freeze > 0) { this.freeze -= dt; return; }
    const w = this.world;
    this.msgT = Math.max(0, this.msgT - dt); this.hintT = Math.max(0, this.hintT - dt);
    this.invuln = Math.max(0, this.invuln - dt); this.hurtFlash = Math.max(0, this.hurtFlash - dt * 3); this.flash = Math.max(0, this.flash - dt * 3);
    this.slam = Math.max(0, this.slam - dt * 3); this.recoil = this.recoil.map((r) => Math.max(0, r - dt * 4));
    this.eye = Math.min(1, this.eye + dt * 0.8);
    this.phaseT += dt;
    if (this.phase === 'intro') {
      this.rise = Math.max(0, 1 - this.phaseT / 2.4);
      if (this.phaseT > 3.0) { this.phase = 'fight'; this.phaseT = 0; this.tip('WAIT FOR THE MORTAR. PUT BLUE ON THE FLOOR WHERE IT WILL LAND, ORANGE ON THE CEILING ABOVE THE MECH.', 9); }
    }
    const pend = this.pend;
    pend.jump ||= input.jumpPressed; pend.use ||= input.usePressed; pend.blue ||= input.fireBlue; pend.orange ||= input.fireOrange; pend.cycle ||= input.fireCycle;
    this.acc += dt;
    let first = true, guard = 0;
    while (this.acc >= K.SimDt && guard++ < 10) {
      const live = this.phase === 'fight';
      const inp = live ? { ...input, jumpPressed: first && pend.jump, usePressed: first && pend.use, fireBlue: first && pend.blue, fireOrange: first && pend.orange, fireCycle: first && pend.cycle }
        : { ...emptyInput(), aimWorld: input.aimWorld };
      if (first) pend.jump = pend.use = pend.blue = pend.orange = pend.cycle = false;
      w.update(K.SimDt, inp);
      this.step(K.SimDt);
      first = false; this.acc -= K.SimDt;
    }
    if (this.acc > K.SimDt * 10) this.acc = 0;
    for (const e of w.events) this.hooks.event?.(e);
    w.events.length = 0;
    for (let i = this.puffs.length - 1; i >= 0; i--) { this.puffs[i].t += dt; if (this.puffs[i].t > 0.4) this.puffs.splice(i, 1); }
    // smoke and sparks from a damaged mech
    if (this.hp < 70 && this.phase !== 'intro' && Math.random() < dt * (0.8 + (100 - this.hp) / 30)) {
      w.fx.burst(V(BOSS_X + (Math.random() - 0.5) * 120, 240 + Math.random() * 60), this.hp < 35 ? C(255, 160, 60) : C(90, 90, 100), 3, 90, 0.7, 3, -200, PKind.Square, false);
    }
    if (this.phase === 'dying') {
      this.dying += dt;
      if (Math.random() < dt * 14) { const x = BOSS_X + (Math.random() - 0.5) * 150, y = 200 + Math.random() * 180; w.fx.burst(V(x, y), C(255, 190, 80), 26, 360, 0.7, 4); w.fx.burst(V(x, y), C(255, 255, 255), 8, 200, 0.4, 3); w.emit(Sfx.Hit, V(x, y), 4); }
      if (this.dying > 3.2) this.finishWin();
    }
    if (this.phase === 'fight' && this.player.dead && this.player.deadTimer > 1.3) this.loseRound();
  }

  step(dt) {
    const p = this.player;
    if (this.phase === 'intro' || this.phase === 'dying') { this.updateShells(dt); return; }
    this.time += dt; this.fightT += dt;
    if (!p.dead) {
      // the boss only wastes shells on a player who is on a ledge
      this.nextShell -= dt;
      if (this.nextShell <= 0) {
        this.nextShell = this.stage === 1 ? 5.2 : this.stage === 2 ? 4.4 : 3.8;
        this.fireShells();
      }
      if (this.stage >= 2) {
        this.nextWave -= dt;
        if (this.nextWave <= 0) { this.nextWave = this.stage === 2 ? 8 : 6.5; this.slamWave(); if (!this.shown.has('wave')) { this.shown.add('wave'); this.tip('SHOCKWAVE! JUMP OVER IT.', 3.5); } }
      }
      if (this.stage === 3) {
        this.nextPurge -= dt;
        if (this.nextPurge <= 0 && this.purgeWarn <= 0) { this.purgeWarn = 1.4; this.say('PORTAL PURGE!', 1.4); this.world.emit(Sfx.Fizzle, V(BOSS_X, 220), 4); }
        if (this.purgeWarn > 0) { this.purgeWarn -= dt; if (this.purgeWarn <= 0) { this.nextPurge = 13; this.world.clearPlayerPortals(); this.hooks.shake?.(8); } }
      }
      if (this.second > 0) { this.second -= dt; if (this.second <= 0) this.lob(this.secondTx, this.secondFlight, this.secondSide); }
    }
    if (this.hatchPlan) { this.hatchPlan.delay -= dt; if (this.hatchPlan.delay <= 0) { this.hatchT = this.hatchPlan.open; this.hatchPlan = null; this.world.emit(Sfx.DoorOpen, V(CORE.x, CORE.y), 2); if (!this.shown.has('hatch')) { this.shown.add('hatch'); this.tip('THE HATCH IS OPEN! SEND THE SHELL INTO IT.', 4); } } }
    if (this.hatchT > 0) this.hatchT -= dt;
    this.hatch = clamp(this.hatch + (this.hatchT > 0 ? 6 : -4) * dt, 0, 1);
    this.updateShells(dt);
    this.updateWaves(dt);
    // the pit is deadly
    if (!p.dead && p.pos.y > ARENA_H * T - 70) this.world.kill(p, PP.OrangeRamp[2]);
  }

  loseRound() {
    this.hooks.shake?.(6);
    this.attempts++;
    this.begin(false);
    this.phase = 'intro'; this.phaseT = 1.6;      // skip most of the entrance
    this.rise = 0;
    this.say('SYSTEM FAILURE - RETRYING FROM THE CHECKPOINT', 2.4);
  }

  finishWin() {
    this.phase = 'won'; this.phaseT = 0; this.matchWon = true;
    const first = !(this.save.bossWins > 0);
    this.save.bossWins = (this.save.bossWins || 0) + 1;
    this.newBest = this.save.bossBest <= 0 || !this.save.bossBest || this.time < this.save.bossBest;
    if (this.newBest) this.save.bossBest = this.time;
    this.firstWin = first;
    this.save.store();
  }

  // ---------------------------------------------------------------- drawing (world space)

  drawOverlay(g, w) {
    if (w !== this.world) return;
    const t = this.time + this.phaseT * (this.phase === 'intro' ? 1 : 0);
    const cx = BOSS_X, rise = easeOut(this.rise) * 230;
    const sh = (this.flash > 0 ? Math.round(Math.sin(t * 90) * 3 * this.flash) : 0) + (this.phase === 'dying' ? Math.round(Math.sin(t * 70) * 4) : 0);
    const bx = cx + sh, by = rise;
    const dark = C(34, 38, 48), mid = PP.Metal, hi = PP.MetalHi, lo = PP.MetalLo;
    const facing = this.player && this.player.pos.x < BOSS_X ? -1 : 1;
    const R = (x, y, w2, h, c) => g.rect(Math.round(bx + x), Math.round(by + y), w2, h, c);
    g.additive = false;
    // base inside the pit
    R(-86, 330, 172, 130, dark); R(-86, 330, 172, 4, hi);
    for (let i = 0; i < 9; i++) R(-80 + i * 20, 346, 10, 6, i % 2 ? PP.Hazard : dark);
    // torso
    R(-84, 262, 168, 76, mid); R(-84, 262, 168, 4, hi); R(-84, 334, 168, 4, lo);
    R(-60, 276, 120, 40, dark); R(-56, 280, 112, 8, C(60, 66, 80));
    for (let i = 0; i < 5; i++) R(-48 + i * 24, 296, 12, 14, i % 2 ? C(90, 24, 24) : C(60, 66, 80));
    // shoulder cannons that kick back when they fire
    for (const [side, k] of [[-1, 0], [1, 1]]) {
      const rc = Math.round(this.recoil[k] * 12) * -side * -1;
      R(side < 0 ? -128 : 84, 244, 44, 56, mid); R(side < 0 ? -128 : 84, 244, 44, 4, hi);
      R(side < 0 ? -156 - rc : 120 + rc, 262, 40, 20, dark); R(side < 0 ? -156 - rc : 120 + rc, 262, 40, 4, hi);
      R(side < 0 ? -160 - rc : 156 + rc, 266, 6, 12, this.recoil[k] > 0.3 ? C(255, 190, 90) : C(120, 30, 20));
    }
    // dome
    const dome = [[-40, 200, 80], [-56, 212, 112], [-66, 224, 132], [-72, 236, 144], [-74, 248, 148]];
    for (const [dx, dy, dw] of dome) { R(dx, dy, dw, 14, mid); R(dx, dy, dw, 3, hi); }
    R(-74, 248, 148, 14, lo);
    // visor eye that watches you
    const ex = facing * 18 * this.eye + 0;
    R(-44 + ex * 0.4, 224, 88, 22, C(14, 16, 22));
    const eyeCol = this.flash > 0.3 ? C(255, 255, 255) : mix(C(120, 20, 20), C(255, 70, 50), this.eye);
    R(-30 + ex, 230, 60, 10, eyeCol);
    // the hatch: two flaps slide apart to show the glowing core
    const open = this.hatch;
    R(-26 - Math.round(open * 22), 192, 26, 8, hi); R(0 + Math.round(open * 22), 192, 26, 8, hi);
    R(-26 - Math.round(open * 22), 200, 26, 4, lo); R(0 + Math.round(open * 22), 200, 26, 4, lo);
    if (open > 0.05) { g.additive = true; g.circleGrad(bx, by + 196, 26 + 34 * open + (this.flash * 30), C(255, 190, 90, 220 * open), C(255, 120, 40, 0)); g.circleGrad(bx, by + 196, 10 + 8 * open, C(255, 255, 255, 255 * open), C(255, 220, 160, 0)); g.additive = false; R(-8, 192, 16, 8, withAlpha(C(255, 235, 190), open)); }
    // hits leave cracks
    if (this.hp < 80) for (let i = 0; i < Math.round((100 - this.hp) / 20); i++) { R(-60 + i * 30, 270 + (i % 2) * 18, 18, 3, C(20, 22, 28)); R(-50 + i * 30, 273 + (i % 2) * 18, 3, 12, C(20, 22, 28)); }
    // hatch outline marker when open (so you know where to aim)
    if (open > 0.5) { g.additive = true; for (let k = 0; k < 16; k++) { const a = k / 16 * Math.PI * 2 + t * 3; g.rect(Math.round(bx + Math.cos(a) * (CORE_R + 8)), Math.round(by + 196 + Math.sin(a) * (CORE_R + 8)), 2, 2, C(255, 220, 140, 200)); } g.additive = false; }

    // landing markers for shells in flight
    g.additive = true;
    for (const s of this.shells) {
      if (s.reflected) continue;
      const a = 0.35 + 0.35 * Math.sin(t * 14), tt = clamp01(s.age / 1.8);
      for (let k = -2; k <= 2; k++) g.rect(Math.round(s.land + k * 16) - 4, LEDGE_Y - 4, 8, 4, C(255, 90, 70, 255 * a));
      g.circleGrad(s.land, LEDGE_Y - 2, 46, C(255, 90, 60, 90 * tt), C(255, 90, 60, 0));
    }
    // the ceiling panel that is the best place for the exit portal
    const pr = 0.5 + 0.5 * Math.sin(t * 2.5);
    for (let k = 0; k < 7; k++) g.rect(PIT_L + 28 + k * 32, 36, 12, 2, C(255, 170, 80, 60 + 120 * pr));
    // shells
    for (const s of this.shells) {
      const col = s.reflected ? PP.BlueRamp[3] : C(255, 150, 70);
      for (let i = 0; i < s.trail.length; i++) { const q = s.trail[i], a = (i + 1) / s.trail.length; g.circleGrad(q.x, q.y, 6 + a * 10, withAlpha(col, 0.35 * a), withAlpha(col, 0)); }
      g.circleGrad(s.x, s.y, 30, withAlpha(col, 0.85), withAlpha(col, 0));
      g.rect(Math.round(s.x) - 6, Math.round(s.y) - 6, 12, 12, withAlpha(col, 0.95));
      g.rect(Math.round(s.x) - 3, Math.round(s.y) - 3, 6, 6, C(255, 255, 255, 255));
    }
    for (const q of this.puffs) { const a = 1 - q.t / 0.4; g.circleGrad(q.x, q.y, 20 + q.t * 160, C(255, 170, 70, 200 * a), C(255, 80, 20, 0)); }
    // shockwaves along the ledge
    for (const wv of this.waves) {
      if (wv.wait > 0) { const a = 0.4 + 0.4 * Math.sin(t * 20); g.rect(Math.round(wv.x) - 16, LEDGE_Y - 6, 32, 6, C(255, 90, 70, 255 * a)); continue; }
      for (let k = 0; k < 6; k++) g.rect(Math.round(wv.x - wv.dir * k * 10) - 6, LEDGE_Y - 30 + k * 4, 12, 30 - k * 4, C(255, 110, 80, 220 - k * 30));
      g.circleGrad(wv.x, LEDGE_Y - 16, 40, C(255, 90, 60, 120), C(255, 90, 60, 0));
    }
    g.additive = false;
    if (this.purgeWarn > 0) { g.additive = true; for (const p of this.world.portals) if (p) g.circleGrad(p.center.x, p.center.y, 50 + Math.sin(t * 30) * 8, C(255, 60, 60, 120), C(255, 60, 60, 0)); g.additive = false; }
    // a shield flicker on the player after a hit
    const pl = this.player;
    if (this.invuln > 0 && !pl.dead && Math.floor(this.invuln * 12) % 2 === 0) { g.additive = true; g.circleGrad(pl.pos.x, pl.pos.y, 26, C(255, 255, 255, 120), C(255, 255, 255, 0)); g.additive = false; }
  }

  lights() {
    const out = [];
    const open = this.hatch, rise = easeOut(this.rise) * 230;
    out.push({ x: BOSS_X, y: 206 + rise, r: 150 + 120 * open, col: open > 0.1 ? C(255, 170, 80, 120 + 120 * open) : C(255, 60, 40, 55) });
    for (const s of this.shells) out.push({ x: s.x, y: s.y, r: 130, col: withAlpha(s.reflected ? PP.BlueRamp[2] : C(255, 140, 60), 0.8) });
    for (const wv of this.waves) if (wv.wait <= 0) out.push({ x: wv.x, y: LEDGE_Y - 14, r: 100, col: C(255, 90, 60, 150) });
    return out;
  }

  // ---------------------------------------------------------------- HUD

  drawHud(g, UI, Align) {
    const W = K.ScreenW, mid = W / 2;
    // boss bar
    const bw = 560, bx = mid - bw / 2, f = clamp01(this.hp / MAX_HP);
    g.rect(bx - 3, 12, bw + 6, 26, C(8, 10, 14, 235));
    g.rect(bx, 15, Math.round(bw * f), 20, this.stage === 3 ? C(255, 90, 50) : this.stage === 2 ? C(255, 170, 60) : C(230, 70, 70));
    g.rect(bx, 15, Math.round(bw * f), 4, withAlpha(C(255, 255, 255), 0.35));
    for (const mark of [0.33, 0.66]) g.rect(bx + Math.round(bw * mark), 15, 2, 20, C(8, 10, 14));
    UI.text('COLOSSUS', V(mid, 46), 16, C(255, 170, 160), Align.Center);
    // your hearts
    for (let i = 0; i < HEARTS; i++) {
      const x = 134 + i * 34, on = i < this.hearts, blink = this.hurtFlash > 0 && i === this.hearts;
      heart(g, x, 22, on ? C(255, 80, 90) : C(48, 52, 64), blink ? C(255, 255, 255) : null);
    }
    UI.text(`ATTEMPT ${this.attempts}`, V(134, 54), 16, C(150, 156, 170));
    UI.text(UI.formatTime(this.time), V(W - 134, 20), 30, PP.CreamHi, Align.Right);
    if (this.save.bossBest > 0) UI.text(`BEST ${UI.formatTime(this.save.bossBest)}`, V(W - 134, 54), 16, C(150, 156, 170), Align.Right);
    if (this.hatch > 0.5 && this.phase === 'fight') UI.textShadow('HATCH OPEN', V(mid, 80), 30, withAlpha(PP.Hazard, 0.9), Align.Center);
    if (this.hintT > 0) {
      const a = clamp01(this.hintT * 2), tw = UI.measure(this.hint, 16).x, r = { x: mid - tw / 2 - 24, y: K.ScreenH - 78, w: tw + 48, h: 40 };
      UI.panel(r, a, PP.OrangeRamp[2]);
      UI.text(this.hint, V(mid, r.y + 20), 16, withAlpha(PP.CreamHi, a), Align.Center);
    } else UI.text('CLICK  PORTALS      E  GRAB A CUBE (IT BLOCKS SHELLS)      ESC  PAUSE', V(mid, K.ScreenH - 36), 16, withAlpha(C(190, 196, 210), 0.5), Align.Center);
    const cy = K.ScreenH / 2 - 60;
    if (this.phase === 'intro') {
      const a = clamp01(this.phaseT * 3);
      UI.textShadow('WARNING', V(mid, cy), 90, withAlpha(C(255, 90, 80), a * (0.6 + 0.4 * Math.sin(this.phaseT * 12))), Align.Center);
      UI.text('A GIANT MECH IS ONLINE', V(mid, cy + 70), 30, withAlpha(PP.CreamHi, a), Align.Center);
    } else if (this.msgT > 0) {
      UI.textShadow(this.message, V(mid, cy), 56, withAlpha(this.message.startsWith('DIRECT') ? PP.Hazard : this.message.startsWith('SYSTEM') ? C(255, 90, 80) : PP.CreamHi, clamp01(this.msgT * 2)), Align.Center);
    }
  }
}

function heart(g, x, y, c, flash) {
  const p = flash || c;
  for (const [dx, dy, w, h] of [[2, 0, 6, 4], [12, 0, 6, 4], [0, 4, 20, 6], [2, 10, 16, 4], [6, 14, 8, 4], [8, 18, 4, 2]]) g.rect(x + dx, y + dy, w, h, p);
}
function easeOut(t) { return 1 - (1 - t) * (1 - t); }
