// Fight mode: you against your evil twin in randomly generated arenas. Everything runs on the normal World (so portals,
// cubes, gel, goo, turrets and lift beams all work), with a second player-like body for the twin, an AI to drive it,
// and a few extras that make it a fight: energy bolts that fly through portals, health and power-up pickups, stomps,
// cube impacts and round modifiers.
import { K, C, V, withAlpha, mix, clamp, clamp01, Portal, PortalColor, HALF_LEN, overlaps, lerp } from './core.js';
import { World, Player, makeDef, emptyInput, Sfx, PKind } from './world.js';
import { PP } from './art.js';
import { PixelFont } from './gfx.js';

const T = K.Tile;
export const ARENA_W = 32, ARENA_H = 18;           // a compact room: everyone is always in sight
const FLOOR = 16;                                  // row of the floor surface
const BOLT_SPEED = 780, BOLT_DMG = 18, BOLT_COST = 22, BOLT_CD = 0.28;
const MODIFIERS = [
  { id: 'none', name: '' },
  { id: 'lowg', name: 'LOW GRAVITY' },
  { id: 'ricochet', name: 'RICOCHET BOLTS' },
  { id: 'sudden', name: 'SUDDEN DEATH' },
];
const snap2 = (v) => Math.round(v / 2) * 2;

// ------------------------------------------------------------------ arena generation

function mulberry(seed) {
  let s = seed >>> 0;
  return () => { s = (s + 0x6D2B79F5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

/** A random arena: floor, stepped platforms you can jump between, and a few hazards and toys. */
export function generateArena(seed) {
  const r = mulberry(seed), ri = (a, b) => a + Math.floor(r() * (b - a + 1)), pick = (arr) => arr[Math.floor(r() * arr.length)];
  const W = ARENA_W, H = ARENA_H;
  const g = Array.from({ length: H }, (_, y) => Array.from({ length: W }, (_, x) => (y === 0 || y >= FLOOR || x === 0 || x === W - 1 ? 'X' : ' ')));
  const set = (x, y, c) => { if (x > 0 && x < W - 1 && y > 0 && y < H - 1) g[y][x] = c; };
  const fill = (x0, y0, x1, y1, c) => { for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) set(x, y, c); };
  // portal-friendly panels on walls, ceiling and floor
  for (let i = 0, n = ri(2, 4); i < n; i++) { const x = ri(3, W - 9), w = ri(4, 7); for (let k = 0; k < w; k++) if (x + k < W - 1) g[0][x + k] = '#'; }
  for (const x of [0, W - 1]) for (let i = 0, n = ri(1, 2); i < n; i++) { const y = ri(3, 10), h = ri(3, 5); for (let k = 0; k < h; k++) g[y + k][x] = '#'; }
  for (let x = 1; x < W - 1; x++) if (r() < 0.55) g[FLOOR][x] = '#';

  // platforms, tier by tier, each one reachable from the one below
  const tiers = [13, 10, 7, 4], plats = [{ x: 1, w: W - 2, y: FLOOR }], tops = [];
  let below = [plats[0]];
  for (let t = 0; t < tiers.length; t++) {
    const cur = [];
    for (let i = 0, n = ri(1, 3); i < n; i++) {
      const w = ri(3, 7), parent = pick(below);
      const lo = Math.max(2, parent.x - w - 1), hi = Math.min(W - 2 - w, parent.x + parent.w + 1);
      if (hi < lo) continue;
      const x = ri(lo, hi);
      if (cur.some((q) => x < q.x + q.w + 2 && x + w + 2 > q.x)) continue;
      fill(x, tiers[t], x + w - 1, tiers[t], r() < 0.75 ? '#' : 'X');
      const p = { x, w, y: tiers[t] };
      cur.push(p); plats.push(p);
      for (let k = 1; k < w - 1; k += 2) tops.push({ x: x + k, y: tiers[t] });
    }
    if (cur.length) below = cur;
  }
  for (let x = 6; x < W - 6; x += 3) tops.push({ x, y: FLOOR });

  const features = [];
  if (r() < 0.55) { const w = ri(3, 5), x = ri(10, W - 10 - w); fill(x, FLOOR, x + w - 1, FLOOR + 1, '~'); features.push('goo'); }
  if (r() < 0.6) { const x = ri(8, W - 11); if (g[FLOOR][x] !== '~' && g[FLOOR][x + 2] !== '~') { fill(x, FLOOR, x + 2, FLOOR, 'J'); features.push('gel'); } }
  if (r() < 0.35) { const x = ri(8, W - 9); if (g[FLOOR][x] !== '~' && g[FLOOR][x] !== 'J') { set(x, FLOOR, r() < 0.5 ? 'Q' : 'q'); features.push('plate'); } }
  if (r() < 0.55) { const x = ri(8, W - 9); if (g[FLOOR][x] !== '~' && g[FLOOR][x] !== 'J' && !'Qq'.includes(g[FLOOR][x])) { set(x, FLOOR, 'B'); features.push('lift'); } }
  // sentries on the ceiling, away from the spawn corners so nobody is shot the moment a round starts
  for (let i = 0, n = r() < 0.65 ? ri(1, 2) : 0; i < n; i++) { set(ri(10, W - 11), 1, 'S'); features.push('turret'); }
  if (r() < 0.3) { const y = pick([7, 10, 13]); if (g[y][1] === ' ') { set(1, y, '>'); features.push('laser'); } }
  for (let i = 0, n = ri(2, 3); i < n; i++) { const t = pick(tops.filter((q) => q.x > 5 && q.x < W - 6)); if (t && g[t.y - 1][t.x] === ' ') set(t.x, t.y - 1, i === 0 && r() < 0.4 ? pick(['M', 'N']) : 'C'); }

  // spawn points: solid ground on both sides, never in goo
  const sp = [2, 3, 4, W - 5, W - 4, W - 3];
  for (const x of sp) { g[FLOOR][x] = 'X'; g[FLOOR + 1][x] = 'X'; }
  for (let y = FLOOR - 3; y < FLOOR; y++) for (const x of sp) if (g[y][x] !== ' ') g[y][x] = ' ';
  g[FLOOR - 1][3] = 'P';
  const rows = g.map((row) => row.join(''));
  return {
    def: makeDef({ name: 'ARENA', map: rows, gun: 2, blue: [1, 0], orange: [1, 0], hints: [], theme: 1 + Math.floor(r() * 6), seed }),
    spawns: { player: V(3 * T + 16, FLOOR * T - 15), enemy: V((W - 4) * T + 16, FLOOR * T - 15) },
    tops: tops.filter((q) => g[q.y][q.x] !== '~'), features, plats,
  };
}

// ------------------------------------------------------------------ the fight

export class Fight {
  /** @param {object} save  the save data (fightBest / fightWins are updated) */
  constructor(save, audioHooks = {}) {
    this.save = save;
    this.hooks = audioHooks;                 // { shake(n), freeze(s) }
    this.streak = save.fightStreak || 0;      // matches won in a row this session
    this.newMatch();
  }

  get skill() { return clamp(0.22 + this.level * 0.14, 0.2, 0.96); }

  newMatch() {
    this.level = this.streak;
    this.wins = 0; this.losses = 0; this.roundNo = 0;
    this.phase = 'intro'; this.phaseT = 0; this.message = '';
    this.startRound();
  }

  startRound() {
    this.roundNo++;
    const seed = Math.floor(Math.random() * 1e9);
    this.arena = generateArena(seed);
    this.modifier = MODIFIERS[Math.random() < 0.45 ? 0 : 1 + Math.floor(Math.random() * 3)];
    this.world = new World(this.arena.def);
    World.Gravity = this.modifier.id === 'lowg' ? 1150 : 1900;
    const w = this.world;
    const startHp = this.modifier.id === 'sudden' ? 50 : 100;
    this.you = { body: w.player, hp: startHp, max: startHp, energy: 100, shield: 0, rapid: 0, cd: 0, flash: 0, stompCd: 0, cubeCd: 0, isYou: true };
    const e = new Player(V(this.arena.spawns.enemy.x, this.arena.spawns.enemy.y));
    e.evil = true; e.facing = -1; e.input = emptyInput();
    w.extra.push(e);
    const eMax = Math.round(startHp * (1 + this.level * 0.12));
    this.evil = { body: e, hp: eMax, max: eMax, energy: 100, shield: 0, rapid: 0, cd: 0, flash: 0, stompCd: 0, cubeCd: 0, isYou: false };
    this.fighters = [this.you, this.evil];
    this.bolts = []; this.pickups = []; this.floaters = []; this.pickupT = 5 + Math.random() * 3;
    this.ai = { strafe: 1, strafeT: 0, jumpCd: 0, fireCd: 1.6 - this.skill, tacticT: 6 + Math.random() * 5, tactic: null, dodgeCd: 0, stuckT: 0, lastX: e.pos.x };
    this.freeze = 0;
    this.pend = { jump: false, use: false, blue: false, orange: false, cycle: false };
    this.acc = 0; this.time = 0; this.hitFlash = 0; this.slow = 1;
    w.fightHooks = {
      laser: (b) => this.hurtBody(b, 0, null, true),
      turret: (b) => this.hurtBody(b, 26, null, false),
    };
    this.laserCd = new Map();
    this.phase = 'intro'; this.phaseT = 0;
    this.message = '';
  }

  /** Called when leaving the mode so the physics constants go back to normal. */
  dispose() { World.Gravity = 1900; }

  fighterOf(body) { return this.fighters.find((f) => f.body === body); }

  // ---------------------------------------------------------------- damage

  /** Damage with knockback. from: world point the hit came from (for the push), or null. */
  hurtBody(body, dmg, from, isLaser = false) {
    const f = this.fighterOf(body);
    if (!f || body.dead || this.phase !== 'fight') return;
    if (isLaser) {
      // beams hurt in ticks rather than killing outright
      const t = this.laserCd.get(body) || 0;
      if (this.time < t) return;
      this.laserCd.set(body, this.time + 0.35);
      dmg = 9;
    }
    if (f.shield > 0) {
      f.shield = 0;
      this.world.fx.burst(body.pos, mix(PP.BlueRamp[2], C(255, 255, 255), 0.4), 24, 260, 0.5, 3);
      this.world.emit(Sfx.Pickup, body.pos, 2);
      return;
    }
    f.hp -= dmg; f.flash = 1;
    this.floaters.push({ x: body.pos.x, y: body.pos.y - 26, t: 0, text: String(Math.round(dmg)), col: dmg >= 25 ? C(255, 220, 90) : C(255, 255, 255) });
    if (from) {
      const dx = body.pos.x - from.x, dy = body.pos.y - from.y, d = Math.hypot(dx, dy) || 1;
      body.vel.x += dx / d * 260; body.vel.y = Math.min(body.vel.y, -150) + dy / d * 60;
    }
    this.world.fx.burst(body.pos, f.isYou ? PP.OrangeRamp[2] : C(255, 70, 70), 14 + Math.round(dmg / 3), 300, 0.45, 3, 500, PKind.Square, false);
    this.world.emit(Sfx.Hit, body.pos, Math.min(6, 2 + dmg / 8), dmg >= 30 ? 0.05 : 0.025);
    if (f.hp <= 0) { f.hp = 0; this.world.kill(body, f.isYou ? Palette_orange() : Palette_red()); }
  }

  // ---------------------------------------------------------------- bolts

  fire(f, aim) {
    const cost = f.rapid > 0 ? BOLT_COST * 0.35 : BOLT_COST, cd = f.rapid > 0 ? BOLT_CD * 0.45 : BOLT_CD;
    if (f.energy < cost || f.cd > 0 || f.body.dead) return false;
    f.energy -= cost; f.cd = cd;
    const o = World.gunOrigin(f.body);
    const a = this.modifier.id === 'ricochet' ? 2 : 0;
    this.bolts.push({ x: o.x + aim.x * 22, y: o.y + aim.y * 22, vx: aim.x * BOLT_SPEED, vy: aim.y * BOLT_SPEED, owner: f, age: 0, bounces: a, trail: [], portalCd: 0 });
    f.body.gunKick = 1; f.body.lastShot = f.isYou ? PortalColor.Blue : PortalColor.Orange;
    this.world.emit(Sfx.Bolt, o, 0);
    this.world.fx.burst(V(o.x + aim.x * 22, o.y + aim.y * 22), f.isYou ? PP.BlueRamp[3] : C(255, 120, 100), 6, 160, 0.2, 2.5);
    return true;
  }

  updateBolts(dt) {
    const w = this.world, solid = (x, y) => w.isSolid(Math.floor(x / T), Math.floor(y / T));
    for (let i = this.bolts.length - 1; i >= 0; i--) {
      const b = this.bolts[i];
      b.age += dt; b.portalCd = Math.max(0, b.portalCd - dt);
      let dead = b.age > 3.2;
      const steps = Math.max(1, Math.ceil(Math.hypot(b.vx, b.vy) * dt / 6));
      for (let s = 0; s < steps && !dead; s++) {
        const px = b.x, py = b.y;
        b.x += b.vx * dt / steps; b.y += b.vy * dt / steps;
        if (b.x < -64 || b.y < -64 || b.x > ARENA_W * T + 64 || b.y > ARENA_H * T + 64) { dead = true; break; }
        // portals
        if (w.linked && b.portalCd <= 0) {
          for (let k = 0; k < 2 && !dead; k++) {
            const p = w.portals[k], q = w.portals[1 - k];
            if (!p || !q) continue;
            const v0 = p.v({ x: px, y: py }), v1 = p.v({ x: b.x, y: b.y });
            if (v0 >= 0 && v1 < 0 && Math.abs(p.u({ x: b.x, y: b.y })) < HALF_LEN) {
              const np = Portal.mapPoint(p, q, { x: b.x, y: b.y }), nv = Portal.mapDir(p, q, { x: b.vx, y: b.vy });
              b.x = np.x + q.normal.x * 5; b.y = np.y + q.normal.y * 5; b.vx = nv.x; b.vy = nv.y; b.portalCd = 0.05; b.trail.length = 0;
              w.fx.burst(p.center, k === 0 ? PP.BlueRamp[3] : PP.OrangeRamp[3], 8, 180, 0.25, 2.5);
              w.fx.burst(q.center, k === 0 ? PP.OrangeRamp[3] : PP.BlueRamp[3], 8, 180, 0.25, 2.5);
              p.pulse = 0.7; q.pulse = 0.7;
              break;
            }
          }
        }
        // cubes: a mirror cube bends the bolt, any other cube eats it
        for (const c of w.cubes) {
          if (!c.solid) continue;
          const r = c.rect;
          if (b.x > r.x && b.x < r.x + r.w && b.y > r.y && b.y < r.y + r.h) {
            if (c.mirror !== 0) {
              const nvx = c.mirror > 0 ? -b.vy : b.vy, nvy = c.mirror > 0 ? -b.vx : b.vx;
              b.vx = nvx; b.vy = nvy; const d = Math.hypot(nvx, nvy) || 1;
              b.x = c.pos.x + nvx / d * (c.half.x + 3); b.y = c.pos.y + nvy / d * (c.half.y + 3);
              w.fx.burst(c.pos, C(200, 240, 255), 6, 150, 0.2, 2);
            } else { w.fx.burst(b, C(200, 210, 230), 8, 140, 0.25, 2.5); dead = true; }
            break;
          }
        }
        if (dead) break;
        // fighters (including the one who fired, once the bolt has had time to get away or come back through a portal)
        for (const f of this.fighters) {
          if (f.body.dead) continue;
          if (f === b.owner && b.age < 0.12) continue;
          const r = f.body.rect;
          if (b.x > r.x - 3 && b.x < r.x + r.w + 3 && b.y > r.y - 3 && b.y < r.y + r.h + 3) {
            this.hurtBody(f.body, BOLT_DMG, { x: b.x - b.vx * 0.05, y: b.y - b.vy * 0.05 });
            dead = true; break;
          }
        }
        if (dead) break;
        // walls
        if (solid(b.x, b.y)) {
          if (b.bounces > 0) {
            b.bounces--;
            const hitX = solid(b.x, py), hitY = solid(px, b.y);
            if (hitX || !hitY) b.vx = -b.vx; if (hitY || !hitX) b.vy = -b.vy;
            b.x = px; b.y = py;
            w.fx.burst(V(b.x, b.y), C(255, 255, 255), 5, 120, 0.2, 2);
          } else { w.fx.burst(V(px, py), b.owner.isYou ? PP.BlueRamp[3] : C(255, 140, 120), 8, 150, 0.3, 2.5, 0, PKind.Spark, true); dead = true; }
        } else if (w.at(Math.floor(b.x / T), Math.floor(b.y / T)) === '|') { w.fx.burst(V(b.x, b.y), C(150, 220, 255), 8, 150, 0.3, 2.5); dead = true; }
      }
      b.trail.push({ x: b.x, y: b.y }); if (b.trail.length > 7) b.trail.shift();
      if (dead) this.bolts.splice(i, 1);
    }
  }

  // ---------------------------------------------------------------- pickups, stomps, cubes

  updatePickups(dt) {
    this.pickupT -= dt;
    if (this.pickupT <= 0 && this.pickups.length < 2) {
      this.pickupT = 9 + Math.random() * 5;
      const spot = this.arena.tops[Math.floor(Math.random() * this.arena.tops.length)];
      if (spot) {
        const hurt = Math.min(this.you.hp / this.you.max, this.evil.hp / this.evil.max);
        const type = hurt < 0.6 && Math.random() < 0.55 ? 'heal' : ['heal', 'rapid', 'shield'][Math.floor(Math.random() * 3)];
        this.pickups.push({ type, x: spot.x * T + 16, y: spot.y * T - 22, t: 0 });
        this.world.fx.burst(V(spot.x * T + 16, spot.y * T - 22), C(255, 255, 255), 12, 180, 0.4, 2.5);
      }
    }
    for (let i = this.pickups.length - 1; i >= 0; i--) {
      const p = this.pickups[i]; p.t += dt;
      for (const f of this.fighters) {
        if (f.body.dead || Math.hypot(f.body.pos.x - p.x, f.body.pos.y - 8 - p.y) > 26) continue;
        if (p.type === 'heal') f.hp = Math.min(f.max, f.hp + 40);
        else if (p.type === 'rapid') f.rapid = 7;
        else f.shield = 1;
        this.world.fx.burst(V(p.x, p.y), PICK_COL[p.type], 18, 220, 0.5, 3);
        this.world.emit(Sfx.Pickup, V(p.x, p.y), 0);
        this.pickups.splice(i, 1);
        break;
      }
    }
  }

  updateContacts(dt) {
    const w = this.world;
    for (const f of this.fighters) { f.stompCd = Math.max(0, f.stompCd - dt); f.cubeCd = Math.max(0, f.cubeCd - dt); }
    // stomps: land on the other fighter's head
    const [a, b] = this.fighters;
    for (const [x, y] of [[a, b], [b, a]]) {
      if (x.body.dead || y.body.dead || x.stompCd > 0) continue;
      if (x.body.vel.y > 240 && overlaps(x.body.rect, y.body.rect) && x.body.pos.y < y.body.pos.y - 8) {
        x.stompCd = 0.5;
        x.body.vel.y = -520;
        this.hurtBody(y.body, 22, V(x.body.pos.x, x.body.pos.y - 30));
        w.fx.burst(y.body.pos, C(255, 255, 255), 10, 200, 0.3, 3);
      }
    }
    // cubes thrown or flung at speed hurt whoever they hit
    for (const c of w.cubes) {
      if (!c.solid || c.held) continue;
      const sp = Math.hypot(c.vel.x, c.vel.y);
      if (sp < 480) continue;
      for (const f of this.fighters) {
        if (f.body.dead || f.cubeCd > 0 || f.body.holding === c) continue;
        if (overlaps(c.rect, f.body.rect)) {
          f.cubeCd = 0.5;
          this.hurtBody(f.body, clamp((sp - 380) / 9, 10, 48), c.pos);
          c.vel.x *= -0.3; c.vel.y *= -0.3;
          w.fx.burst(c.pos, C(220, 230, 255), 12, 220, 0.3, 3);
        }
      }
    }
  }

  // ---------------------------------------------------------------- the evil twin's brain

  /** Is there goo (rather than ground) just ahead of the body when walking in direction dir? */
  gooAhead(e, dir) {
    const w = this.world, col = Math.floor((e.pos.x + dir * 30) / T), feet = Math.floor((e.pos.y + e.half.y + 2) / T);
    if (w.beams.some((b) => b.len > 0 && b.x === col && b.y >= feet - 1)) return true;     // keep out of lift and push beams
    for (let y = feet; y < feet + 7; y++) { const c = w.at(col, y); if (c === '~') return true; if (w.isSolid(col, y)) return false; }
    return false;
  }

  platformOf(b) {
    const feet = b.pos.y + b.half.y;
    let best = null;
    for (const q of this.arena.plats) {
      const top = q.y * T;
      if (top < feet - 8 || b.pos.x < q.x * T - 10 || b.pos.x > (q.x + q.w) * T + 10) continue;
      if (!best || top < best.y * T) best = q;
    }
    return best;
  }

  /** One step of a route over the platforms toward the player: where to walk and whether to jump now. */
  navigate(e, p) {
    const A = this.platformOf(e), B = this.platformOf(p);
    if (!A || !B || A === B) return null;
    const plats = this.arena.plats, gap = (a, b) => Math.max(0, a.x - (b.x + b.w), b.x - (a.x + a.w));
    const ok = (a, b) => (b.y < a.y ? a.y - b.y <= 3 && gap(a, b) <= 3 : gap(a, b) <= 4);
    const prev = new Map([[A, null]]), queue = [A];
    while (queue.length) {
      const a = queue.shift();
      if (a === B) break;
      for (const b of plats) if (!prev.has(b) && ok(a, b)) { prev.set(b, a); queue.push(b); }
    }
    if (!prev.has(B)) return null;
    let n = B;
    while (prev.get(n) !== A) { n = prev.get(n); if (!n) return null; }
    const cx = (q) => (q.x + q.w / 2) * T;
    if (n.y < A.y) {
      // climbing: line up beside the nearer end of the platform above, then jump toward it
      const l = n.x * T, r = (n.x + n.w) * T;
      // a full-height jump clears a platform three tiles up only if you take off about 95 units out from its edge
      const tx = e.pos.x < l ? l - 95 : e.pos.x > r ? r + 95 : (e.pos.x - l < r - e.pos.x ? l - 95 : r + 95);
      const near = Math.abs(e.pos.x - tx) < 12;
      return { move: e.onGround ? (near ? Math.sign(cx(n) - e.pos.x) : Math.sign(tx - e.pos.x)) : Math.sign(cx(n) - e.pos.x), jump: near && e.onGround };
    }
    // dropping or crossing: walk off the nearest end toward it
    const l = A.x * T, r = (A.x + A.w) * T, dir = Math.sign(cx(n) - e.pos.x) || 1;
    const edge = dir > 0 ? r : l;
    return { move: dir, jump: Math.abs(e.pos.x - edge) < 20 && n.y <= A.y && gap(A, n) > 0 };
  }

  think(dt, you) {
    const ai = this.ai, e = this.evil.body, w = this.world, sk = this.skill;
    const inp = emptyInput();
    inp.aimWorld = { x: e.pos.x + e.facing * 100, y: e.pos.y };
    if (e.dead || you.body.dead) { e.input = inp; return; }
    const p = you.body;
    const dx = p.pos.x - e.pos.x, dy = p.pos.y - e.pos.y, dist = Math.hypot(dx, dy) || 1;
    const eye = V(e.pos.x, e.pos.y - 2);
    const los = !w.castGrid(eye, V(dx / dist, (dy - 2) / dist), dist, (x, y) => w.isSolid(x, y)).hit;
    ai.fireCd -= dt; ai.jumpCd -= dt; ai.strafeT -= dt; ai.dodgeCd -= dt; ai.tacticT -= dt;

    // where to aim: a little ahead of a moving target, with an error that shrinks as the twin gets better
    const lead = dist / BOLT_SPEED * 0.8;
    let tx = p.pos.x + p.vel.x * lead, ty = p.pos.y - 4 + p.vel.y * lead * 0.5;
    const err = (1 - sk) * 0.34 + 0.02, ang = Math.atan2(ty - eye.y, tx - eye.x) + (Math.random() - 0.5) * err * 2;
    inp.aimWorld = { x: eye.x + Math.cos(ang) * 300, y: eye.y + Math.sin(ang) * 300 };

    // movement
    let want = 0;
    const need = this.evil.hp < this.evil.max * 0.5 ? this.pickups.find((k) => k.type === 'heal') : this.pickups[0];
    if (need && Math.hypot(need.x - e.pos.x, need.y - e.pos.y) < 700 && (this.evil.hp < this.evil.max * 0.7 || los === false)) want = Math.sign(need.x - e.pos.x);
    else if (los && dist < 560) {
      if (ai.strafeT <= 0) { ai.strafe = [-1, 1, 0, 1, -1][Math.floor(Math.random() * 5)]; ai.strafeT = 0.5 + Math.random() * 0.9; }
      want = ai.strafe;
      if (dist < 150) want = -Math.sign(dx) || 1;
      else if (dist > 460) want = Math.sign(dx);
    } else want = Math.sign(dx);

    // don't walk into goo or off a cliff into it
    let danger = false, gap = false;
    if (want !== 0 && e.onGround && this.gooAhead(e, want)) {
      danger = true; gap = true;
      // jump the pit if there is ground within reach on the far side, otherwise stop
      const col = Math.floor((e.pos.x + want * 30) / T), feet = Math.floor((e.pos.y + e.half.y + 2) / T);
      let across = false;
      for (let k = 2; k <= 5; k++) if (w.isSolid(col + want * k, feet)) { across = true; break; }
      if (!across) want = 0;
    }
    // a wall or step in the way
    const ahead = Math.floor((e.pos.x + want * 22) / T), kneeRow = Math.floor((e.pos.y + e.half.y - 10) / T);
    const blocked = want !== 0 && w.isSolid(ahead, kneeRow);
    inp.moveX = want;
    inp.jumpHeld = false;
    // climbing, dropping and crossing between platforms when the player is on a different level
    // if the route keeps failing (a lift, gel or a hazard in the way), shake it up for a moment
    const here = this.platformOf(e);
    if (here !== ai.lastPlat) { ai.lastPlat = here; ai.platT = 0; } else ai.platT = (ai.platT || 0) + dt;
    if (ai.platT > 3.5 && Math.abs(dy) > 60) { ai.wander = 1.1; ai.platT = 0; ai.wanderDir = Math.random() < 0.5 ? -1 : 1; }
    ai.wander = Math.max(0, (ai.wander || 0) - dt);
    const hop = Math.abs(dy) > 60 && ai.wander <= 0 ? this.navigate(e, p) : null;
    if (ai.wander > 0) {
      inp.moveX = ai.wanderDir;
      if (e.onGround && ai.jumpCd <= 0 && !this.gooAhead(e, ai.wanderDir)) { inp.jumpPressed = true; inp.jumpHeld = true; ai.jumpCd = 0.4; }
      else if (!e.onGround && e.vel.y < 0) inp.jumpHeld = true;
    } else if (hop) {
      const pit = hop.move && e.onGround && this.gooAhead(e, hop.move);
      if (!pit) {
        inp.moveX = hop.move;
        if (hop.jump && ai.jumpCd <= 0) { inp.jumpPressed = true; inp.jumpHeld = true; ai.jumpCd = 0.45; }
        else if (!e.onGround && e.vel.y < 0 && ai.jumpCd > 0.25) inp.jumpHeld = true;
      } else {
        // a pit in the way: hop it if there is ground on the far side, otherwise wait
        const col = Math.floor((e.pos.x + hop.move * 30) / T), feet = Math.floor((e.pos.y + e.half.y + 2) / T);
        let across = false;
        for (let k = 2; k <= 5; k++) if (w.isSolid(col + hop.move * k, feet)) { across = true; break; }
        inp.moveX = across ? hop.move : 0;
        if (across && ai.jumpCd <= 0) { inp.jumpPressed = true; inp.jumpHeld = true; ai.jumpCd = 0.5; }
      }
      ai.lastX = e.pos.x; ai.stuckT = 0;
    } else {
      // a wall or step in the way, or a pit to hop, or just a restless hop now and then
      const wantsUp = dy < -60 && Math.abs(dx) < 330 && w.isSolid(Math.floor(e.pos.x / T), Math.floor((e.pos.y - 110) / T));
      if (e.onGround && ai.jumpCd <= 0 && (blocked || gap && danger || wantsUp || (Math.random() < 0.004 + 0.01 * sk))) {
        inp.jumpPressed = true; inp.jumpHeld = true; ai.jumpCd = 0.5;
      } else if (!e.onGround && e.vel.y < 0 && ai.jumpCd > 0.3) inp.jumpHeld = true;
      ai.stuckT = Math.abs(e.pos.x - ai.lastX) < 0.5 && want !== 0 ? ai.stuckT + dt : 0; ai.lastX = e.pos.x;
      if (ai.stuckT > 0.5 && e.onGround) { inp.jumpPressed = true; inp.jumpHeld = true; ai.stuckT = 0; }
    }

    // dodge incoming bolts
    if (ai.dodgeCd <= 0 && e.onGround) {
      for (const b of this.bolts) {
        if (b.owner === this.evil) continue;
        const bx = e.pos.x - b.x, by = e.pos.y - b.y, d = Math.hypot(bx, by);
        if (d < 220 && (bx * b.vx + by * b.vy) / (d * BOLT_SPEED) > 0.9 && Math.random() < 0.3 + sk * 0.6) { inp.jumpPressed = true; inp.jumpHeld = true; ai.dodgeCd = 0.6; break; }
      }
    }

    // shooting
    const f = this.evil;
    if (los && dist < 700 && ai.fireCd <= 0 && f.energy >= BOLT_COST * (f.rapid > 0 ? 0.35 : 1) && !ai.tactic) {
      inp.fireBolt = true;
      ai.fireCd = (f.rapid > 0 ? 0.18 : 0.3) + (1 - sk) * 0.7 + Math.random() * 0.35;
    }

    // portal ambush: open a portal behind you and one beside the twin, then shoot into its own portal
    if (!ai.tactic && ai.tacticT <= 0 && los && dist > 220 && w.gunAllows(PortalColor.Blue)) {
      ai.tactic = { phase: 0, t: 0 }; ai.tacticT = 9 + Math.random() * 6 - sk * 3;
    }
    if (ai.tactic) {
      const t = ai.tactic; t.t += dt;
      if (t.phase === 0 && t.t > 0.15) {
        const ux = dx / dist, uy = dy / dist;
        inp.aimWorld = { x: p.pos.x + ux * 500, y: p.pos.y + uy * 500 }; inp.fireBlue = true; t.phase = 1; t.t = 0;
      } else if (t.phase === 1 && t.t > 0.4) {
        const side = Math.sign(dx) || 1;
        inp.aimWorld = { x: e.pos.x - side * 400, y: e.pos.y - 20 }; inp.fireOrange = true; t.phase = 2; t.t = 0;
      } else if (t.phase === 2) {
        const o = w.portals[PortalColor.Orange], bl = w.portals[PortalColor.Blue];
        if (!o || !bl || t.t > 2.6) ai.tactic = null;
        else {
          inp.moveX = 0;
          inp.aimWorld = { x: o.center.x, y: o.center.y };
          if (t.t > 0.35 && ai.fireCd <= 0 && f.energy >= BOLT_COST) { inp.fireBolt = true; ai.fireCd = 0.4; }
        }
      }
    }
    e.input = inp;
  }

  // ---------------------------------------------------------------- the loop

  /** @param input  the player's input for this frame (the app's readInput() plus .fireBolt) */
  update(dt, input) {
    if (this.freeze > 0) { this.freeze -= dt; return; }
    this.phaseT += dt;
    const w = this.world;
    if (this.phase === 'intro') {
      if (this.phaseT > 3.2) { this.phase = 'fight'; this.phaseT = 0; }
    } else if (this.phase === 'roundEnd') {
      this.slow = lerp(this.slow, 0.5, 0.1);
      if (this.phaseT > 2.4) {
        if (this.wins >= 3 || this.losses >= 3) { this.phase = 'matchEnd'; this.phaseT = 0; this.finishMatch(); }
        else this.startRound();
      }
    }
    const sim = dt * (this.phase === 'roundEnd' ? this.slow : 1);
    const pd = this.pend;
    pd.jump ||= input.jumpPressed; pd.use ||= input.usePressed; pd.blue ||= input.fireBlue; pd.orange ||= input.fireOrange; pd.cycle ||= input.fireCycle;
    this.acc += sim;
    let first = true, guard = 0;
    while (this.acc >= K.SimDt && guard++ < 10) {
      const live = this.phase === 'fight';
      const inp = live
        ? { ...input, jumpPressed: first && pd.jump, usePressed: first && pd.use, fireBlue: first && pd.blue, fireOrange: first && pd.orange, fireCycle: first && pd.cycle }
        : { ...emptyInput(), aimWorld: input.aimWorld };
      if (first) pd.jump = pd.use = pd.blue = pd.orange = pd.cycle = false;
      if (live) this.think(K.SimDt, this.you); else this.evil.body.input = { ...emptyInput(), aimWorld: { x: 0, y: 0 } };
      w.update(K.SimDt, inp);
      this.time += K.SimDt;
      if (live || this.phase === 'roundEnd') this.simStep(K.SimDt, input, live);
      first = false;
      this.acc -= K.SimDt;
    }
    if (this.acc > K.SimDt * 10) this.acc = 0;
    for (const f of this.fighters) f.flash = Math.max(0, f.flash - dt * 4);
    for (let i = this.floaters.length - 1; i >= 0; i--) { this.floaters[i].t += dt; if (this.floaters[i].t > 0.9) this.floaters.splice(i, 1); }
  }

  simStep(dt, playerInput, live) {
    const w = this.world;
    for (const f of this.fighters) {
      f.cd = Math.max(0, f.cd - dt);
      f.energy = Math.min(100, f.energy + 24 * dt * (f.rapid > 0 ? 1.4 : 1));
      f.rapid = Math.max(0, f.rapid - dt);
    }
    if (live) {
      // bolts
      if (playerInput.fireBolt && !this.you.body.dead) this.fire(this.you, this.you.body.aim);
      if (this.evil.body.input?.fireBolt && !this.evil.body.dead) {
        const a = this.evil.body.input.aimWorld, o = World.gunOrigin(this.evil.body), d = Math.hypot(a.x - o.x, a.y - o.y) || 1;
        this.fire(this.evil, V((a.x - o.x) / d, (a.y - o.y) / d));
      }
      this.updatePickups(dt);
      this.updateContacts(dt);
    }
    this.updateBolts(dt);
    // sound / shake events from the world
    for (const e of w.events) { this.hooks.event?.(e); }
    w.events.length = 0;
    if (this.phase === 'fight' && (this.you.body.dead || this.evil.body.dead)) {
      this.phase = 'roundEnd'; this.phaseT = 0; this.slow = 1;
      const youDied = this.you.body.dead;
      if (youDied && this.evil.body.dead) { this.message = 'DRAW'; }
      else if (youDied) { this.losses++; this.message = 'DEFEATED'; }
      else { this.wins++; this.message = 'K.O.!'; }
      this.hooks.shake?.(10);
    }
  }

  finishMatch() {
    const won = this.wins >= 3;
    this.matchWon = won;
    if (won) { this.streak++; this.save.fightWins = (this.save.fightWins || 0) + 1; this.save.fightBest = Math.max(this.save.fightBest || 0, this.streak); }
    else this.streak = 0;
    this.save.fightStreak = 0;
    this.save.store();
  }

  // ---------------------------------------------------------------- drawing (world space)

  /** Called inside the renderer's world pass: bolts, pickups, shields, health bars and damage numbers. */
  drawOverlay(g, w) {
    if (w !== this.world) return;
    const t = this.time;
    g.additive = true;
    const e = this.evil.body;
    if (!e.dead) {
      g.circleGrad(e.pos.x, e.pos.y, 46 + Math.sin(t * 6) * 4, C(255, 40, 60, 80), C(255, 40, 60, 0));
      // burning eyes
      g.circleGrad(e.pos.x + e.facing * 5, e.pos.y - 9, 7, C(255, 60, 60, 230), C(255, 60, 60, 0));
    }
    for (const f of this.fighters) {
      const b = f.body;
      if (b.dead) continue;
      if (f.flash > 0.05) g.circleGrad(b.pos.x, b.pos.y, 44, C(255, 255, 255, 220 * f.flash), C(255, 255, 255, 0));
      if (f.shield > 0) {
        g.circleGrad(b.pos.x, b.pos.y, 36, C(120, 200, 255, 14), C(120, 200, 255, 0));
        for (let k = 0; k < 28; k++) { const a = k / 28 * Math.PI * 2 + t * 2; g.rect(snap2(b.pos.x + Math.cos(a) * 26), snap2(b.pos.y + Math.sin(a) * 28), 2, 2, C(160, 220, 255, 170)); }
      }
      if (f.rapid > 0) for (let k = 0; k < 3; k++) { const a = t * 9 + k * 2.1; g.rect(snap2(b.pos.x + Math.cos(a) * 18), snap2(b.pos.y + Math.sin(a) * 20), 2, 2, C(255, 190, 60, 230)); }
    }
    g.additive = false;
    // floating health and energy bars, and a marker so you always know which one is you
    for (const f of this.fighters) {
      const b = f.body;
      if (b.dead) continue;
      const x = Math.round(b.pos.x) - 20, y = Math.round(b.pos.y) - 36, hp = clamp01(f.hp / f.max);
      g.rect(x - 2, y - 2, 44, 10, C(8, 10, 14, 220));
      g.rect(x, y, Math.round(40 * hp), 4, f.isYou ? (hp > 0.35 ? PP.OrangeRamp[2] : C(255, 90, 60)) : C(230, 60, 80));
      g.rect(x, y + 5, Math.round(40 * clamp01(f.energy / 100)), 2, f.rapid > 0 ? C(255, 190, 60) : PP.BlueRamp[2]);
      const mc = f.isYou ? PP.BlueRamp[3] : C(255, 110, 120), my = y - 10 + Math.round(Math.sin(t * 5) * 1) * 2;
      g.rect(x + 16, my, 8, 2, mc); g.rect(x + 18, my + 2, 4, 2, mc); g.rect(x + 19, my + 4, 2, 2, mc);
    }
    for (const fl of this.floaters) {
      const a = 1 - fl.t / 0.9;
      PixelFont.draw(g, fl.text, V(Math.round(fl.x) - 6, Math.round(fl.y - fl.t * 40)), 2, withAlpha(fl.col, clamp01(a * 1.5)));
    }
    g.additive = true;
    for (const p of this.pickups) {
      const col = PICK_COL[p.type], bob = Math.sin(p.t * 3) * 3, x = Math.round(p.x), y = snap2(p.y + bob);
      g.circleGrad(p.x, p.y + bob, 34, withAlpha(col, 0.5), withAlpha(col, 0));
      g.additive = false;
      g.rect(x - 10, y - 10, 20, 20, C(14, 16, 22, 230));
      g.rect(x - 10, y - 10, 20, 2, withAlpha(col, 0.9)); g.rect(x - 10, y + 8, 20, 2, withAlpha(col, 0.9));
      g.rect(x - 10, y - 8, 2, 16, withAlpha(col, 0.9)); g.rect(x + 8, y - 8, 2, 16, withAlpha(col, 0.9));
      if (p.type === 'heal') { g.rect(x - 2, y - 6, 4, 12, col); g.rect(x - 6, y - 2, 12, 4, col); }
      else if (p.type === 'rapid') { g.rect(x - 4, y - 7, 4, 7, col); g.rect(x - 2, y - 2, 6, 4, col); g.rect(x, y + 2, 4, 6, col); }
      else { g.rect(x - 6, y - 6, 12, 4, col); g.rect(x - 6, y - 6, 4, 10, col); g.rect(x + 2, y - 6, 4, 10, col); g.rect(x - 4, y + 4, 8, 4, col); }
      g.additive = true;
    }
    for (const b of this.bolts) {
      const col = b.owner.isYou ? PP.BlueRamp[3] : C(255, 110, 90);
      for (let i = 0; i < b.trail.length; i++) { const q = b.trail[i], a = (i + 1) / b.trail.length; g.circleGrad(q.x, q.y, 8 + a * 8, withAlpha(col, 0.3 * a), withAlpha(col, 0)); }
      g.circleGrad(b.x, b.y, 28, withAlpha(col, 0.8), withAlpha(col, 0));
      g.rect(snap2(b.x) - 4, snap2(b.y) - 4, 8, 8, withAlpha(col, 0.9));
      g.rect(snap2(b.x) - 2, snap2(b.y) - 2, 4, 4, C(255, 255, 255, 255));
    }
    g.additive = false;
  }

  /** Lights for the renderer (bolts and pickups glow on the walls around them). */
  lights() {
    const out = [];
    for (const b of this.bolts) out.push({ x: b.x, y: b.y, r: 120, col: withAlpha(b.owner.isYou ? PP.BlueRamp[2] : C(255, 80, 60), 0.75) });
    for (const p of this.pickups) out.push({ x: p.x, y: p.y, r: 90, col: withAlpha(PICK_COL[p.type], 0.45) });
    const e = this.evil.body;
    if (!e.dead) out.push({ x: e.pos.x, y: e.pos.y - 4, r: 130, col: C(255, 40, 60, 70) });
    return out;
  }

  // ---------------------------------------------------------------- HUD (screen space, in the margin above the arena)

  drawHud(g, UI, Align) {
    const W = K.ScreenW, mid = W / 2, left = (W - ARENA_W * T) / 2, right = W - left, bw = 400;
    const bar = (x, y, f, fillCol, alignRight) => {
      g.rect(x - 2, y - 2, bw + 4, 22, C(10, 12, 18, 230));
      const fw = Math.round(bw * clamp01(f.hp / f.max));
      g.rect(alignRight ? x + bw - fw : x, y, fw, 18, fillCol);
      g.rect(alignRight ? x + bw - fw : x, y, fw, 4, withAlpha(C(255, 255, 255), 0.35));
      const ew = Math.round(bw * 0.55 * clamp01(f.energy / 100));
      g.rect(alignRight ? x + bw - bw * 0.55 : x, y + 22, Math.round(bw * 0.55), 6, C(10, 12, 18, 230));
      g.rect(alignRight ? x + bw - ew : x, y + 23, ew, 4, f.rapid > 0 ? C(255, 190, 60) : PP.BlueRamp[2]);
    };
    bar(left, 12, this.you, PP.OrangeRamp[2], false);
    bar(right - bw, 12, this.evil, C(220, 50, 70), true);
    UI.text(`YOU  ${Math.ceil(this.you.hp)}`, V(left, 46), 16, PP.CreamHi);
    UI.text(`${Math.ceil(this.evil.hp)}  EVIL TWIN`, V(right, 46), 16, C(255, 150, 160), Align.Right);
    const tags = (f, x, dir) => {
      let ox = x;
      if (f.shield > 0) { UI.text('SHIELD', V(ox, 46), 16, PP.BlueRamp[3], dir < 0 ? Align.Right : Align.Left); ox += dir * 90; }
      if (f.rapid > 0) UI.text(`RAPID ${Math.ceil(f.rapid)}`, V(ox, 46), 16, C(255, 190, 60), dir < 0 ? Align.Right : Align.Left);
    };
    tags(this.you, left + 130, 1); tags(this.evil, right - 170, -1);
    for (let i = 0; i < 3; i++) {
      g.rect(mid - 52 - i * 22, 16, 16, 16, i < this.wins ? PP.OrangeRamp[2] : C(40, 44, 56));
      g.rect(mid + 36 + i * 22, 16, 16, 16, i < this.losses ? C(220, 50, 70) : C(40, 44, 56));
    }
    UI.text(`ROUND ${this.roundNo}`, V(mid, 42), 16, C(190, 196, 210), Align.Center);
    if (this.modifier.name) UI.text(this.modifier.name, V(mid, 62), 16, PP.Hazard, Align.Center);
    UI.text('CLICK  PORTALS      Q  BOLT      E  GRAB A CUBE      ESC  PAUSE', V(mid, K.ScreenH - 36), 16, withAlpha(C(190, 196, 210), 0.55), Align.Center);

    const cy = K.ScreenH / 2 - 40;
    if (this.phase === 'intro') {
      const pt = this.phaseT, a = clamp01(pt * 5);
      let txt = `ROUND ${this.roundNo}`, size = 72, col = PP.CreamHi;
      if (pt > 2.7) { txt = 'FIGHT!'; size = 100; col = PP.Hazard; }
      else if (pt > 2.2) { txt = '1'; size = 120; }
      else if (pt > 1.7) { txt = '2'; size = 120; }
      else if (pt > 1.2) { txt = '3'; size = 120; }
      UI.textShadow(txt, V(mid, cy), size, withAlpha(col, a), Align.Center);
      if (pt < 1.2 && this.modifier.name) UI.text(this.modifier.name, V(mid, cy + 70), 30, withAlpha(PP.Hazard, a), Align.Center);
    } else if (this.phase === 'roundEnd') {
      UI.textShadow(this.message, V(mid, cy), 100, withAlpha(this.message === 'K.O.!' ? PP.Exit : C(255, 90, 80), clamp01(this.phaseT * 5)), Align.Center);
    }
  }
}

const PICK_COL = { heal: C(94, 224, 138), rapid: C(255, 190, 60), shield: C(120, 200, 255) };
function Palette_orange() { return PP.OrangeRamp[2]; }
function Palette_red() { return C(255, 70, 80); }
