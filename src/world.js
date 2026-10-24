// The simulation: tiles, physics, portals, lasers, doors, hazards. A direct port of Game/World.cs (no DOM).
import {
  K, Palette, PortalColor, Portal, HALF_LEN, V, vdist, safeNormalize, approach, overlaps, rayAabb, mix, TAU,
} from './core.js';

const T = K.Tile;

export const Sfx = {
  PortalBlue: 'PortalBlue', PortalOrange: 'PortalOrange', PortalFail: 'PortalFail', Jump: 'Jump', Land: 'Land',
  Teleport: 'Teleport', ButtonDown: 'ButtonDown', ButtonUp: 'ButtonUp', DoorOpen: 'DoorOpen', DoorClose: 'DoorClose',
  Pickup: 'Pickup', Drop: 'Drop', Death: 'Death', Win: 'Win', Fizzle: 'Fizzle', CubeDissolve: 'CubeDissolve',
  CubeSpawn: 'CubeSpawn', ReceiverOn: 'ReceiverOn',
};

export const GunMode = { None: 0, BlueOnly: 1, Both: 2 };

/** Turns the JSON the exporter wrote into the LevelDef shape the world uses. */
export function makeDef(j) {
  return {
    name: j.name, map: j.map, gun: j.gun,
    blueDir: { x: j.blue[0], y: j.blue[1] }, orangeDir: { x: j.orange[0], y: j.orange[1] },
    hints: j.hints || [], fling: !!j.fling, label: j.label || '', theme: j.theme || 0, seed: j.seed || 0,
    family: j.family || 'sequence', layoutId: j.layoutId || '',
    overview: !!j.family && j.family !== 'classic' && j.family !== 'sequence' && !j.family.startsWith('classic-'),
  };
}

export const emptyInput = () => ({
  moveX: 0, jumpPressed: false, jumpHeld: false, usePressed: false, fireBlue: false, fireOrange: false, fireCycle: false,
  aimWorld: { x: 0, y: 0 },
});

// ------------------------------------------------------------------ particles

export const PKind = { Dot: 0, Spark: 1, Square: 2, Ring: 3 };

export class Particles {
  constructor() { this.items = []; this.trails = []; }
  r(a, b) { return a + Math.random() * (b - a); }
  add(p) { if (this.items.length < 3000) this.items.push(p); }
  burst(pos, color, count, speed, life, size, gravity = 0, kind = PKind.Dot, additive = true, bias = null, spread = Math.PI) {
    const baseAng = bias ? Math.atan2(bias.y, bias.x) : 0;
    for (let i = 0; i < count; i++) {
      const a = bias ? baseAng + this.r(-spread, spread) : this.r(0, TAU);
      const s = this.r(speed * 0.3, speed);
      this.add({
        px: pos.x, py: pos.y, vx: Math.cos(a) * s, vy: Math.sin(a) * s,
        life: this.r(life * 0.6, life), maxLife: life, size: this.r(size * 0.6, size),
        gravity, drag: 2, color, kind, additive,
      });
    }
  }
  trail(a, b, c) { this.trails.push({ a, b, color: c, life: 0.25 }); }
  update(dt) {
    const it = this.items;
    for (let i = it.length - 1; i >= 0; i--) {
      const p = it[i];
      p.life -= dt;
      if (p.life <= 0) { it[i] = it[it.length - 1]; it.pop(); continue; }
      p.vy += p.gravity * dt;
      const k = Math.max(0, 1 - p.drag * dt);
      p.vx *= k; p.vy *= k;
      p.px += p.vx * dt; p.py += p.vy * dt;
    }
    for (let i = this.trails.length - 1; i >= 0; i--) {
      const t = this.trails[i];
      t.life -= dt;
      if (t.life <= 0) this.trails.splice(i, 1);
    }
  }
}

// ------------------------------------------------------------------ bodies

class Body {
  constructor() {
    this.pos = V(0, 0); this.vel = V(0, 0); this.half = V(0, 0);
    this.onGround = false; this.teleportFlash = 0; this.teleports = 0;
  }
  get rect() { return { x: this.pos.x - this.half.x, y: this.pos.y - this.half.y, w: this.half.x * 2, h: this.half.y * 2 }; }
  extentAlong(axis) { return Math.abs(axis.x) * this.half.x + Math.abs(axis.y) * this.half.y; }
}

export class Player extends Body {
  constructor(pos) {
    super();
    this.pos = pos; this.half = V(11, 15);
    this.dead = false; this.deadTimer = 0; this.facing = 1; this.walkPhase = 0;
    this.coyote = 0; this.jumpBuffer = 0; this.jumpCut = false; this.holding = null;
    this.aim = V(1, 0); this.shootCooldown = 0; this.lastShot = PortalColor.Blue; this.gunKick = 0; this.squash = 0; this.won = false;
  }
}

export class Cube extends Body {
  constructor(spawn) {
    super();
    this.spawn = spawn; this.pos = V(spawn.x, spawn.y); this.half = V(14, 14);
    this.held = false; this.dissolving = false; this.dissolveT = 0; this.respawnT = 0; this.gone = false;
    this.onButtonGlow = 0; this.mirror = 0;
  }
  get solid() { return !this.gone && !this.dissolving; }
}

// ------------------------------------------------------------------ world

export class World {
  static Gravity = 1900; static JumpVel = 640; static MaxWalk = 250; static MaxFall = 1500; static GelBounce = 1000;
  static PlateVy = 1060; static PlateVx = 340; static FastWalk = 520; static ButtonHold = 4; static Groups = 6;

  // circuit groups 0-5: doors a-f, buttons 1 2 3 x y z, laser receivers 4 5 6 r s t
  static isDoor(c) { return c >= 'a' && c <= 'f'; }
  static buttonGroup(c) {
    switch (c) { case '1': case 'T': return 0; case '2': case 'U': return 1; case '3': case 'V': return 2;
      case 'x': case 'W': return 3; case 'y': case 'Y': return 4; case 'z': case 'Z': return 5; default: return -1; }
  }
  static isTimedButton(c) { return 'TUVWYZ'.includes(c) && c !== ' '; }
  static timedButtonChar(g) { return 'TUVWYZ'[g]; }
  static isPlate(c) { return c === 'Q' || c === 'q'; }
  static receiverGroup(c) {
    switch (c) { case '4': return 0; case '5': return 1; case '6': return 2; case 'r': return 3; case 's': return 4; case 't': return 5; default: return -1; }
  }
  static isReceiver(c) { return World.receiverGroup(c) >= 0; }
  static doorChar(g) { return String.fromCharCode(97 + g); }
  static buttonChar(g) { return '123xyz'[g]; }
  static receiverChar(g) { return '456rst'[g]; }

  constructor(def) {
    this.def = def;
    this.h = def.map.length;
    this.w = Math.max(...def.map.map((r) => r.length));
    this.tiles = [];
    this.player = null;
    this.cubes = []; this.buttons = []; this.emitters = []; this.receivers = [];
    this.portals = [null, null];
    this.doorOpen = new Array(World.Groups).fill(0);
    this.groupActive = new Array(World.Groups).fill(false);
    this.lasers = []; this.laserImpacts = []; this.events = [];
    this.fx = new Particles();
    this.exit = V(-10, -10); this.hasExit = false;
    this.time = 0; this.portalsPlaced = 0;
    this.nextShot = PortalColor.Blue;
    let blueMarker = null, orangeMarker = null;

    for (let x = 0; x < this.w; x++) this.tiles.push(new Array(this.h));
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        let c = x < def.map[y].length ? def.map[y][x] : 'X';
        const sx = x * T + T / 2, sy = (y + 1) * T;
        const bg = World.buttonGroup(c), rg = World.receiverGroup(c);
        if (c === 'P') { this.player = new Player(V(sx, sy - 15)); c = ' '; }
        else if (c === 'C') { this.cubes.push(new Cube(V(sx, sy - 14))); c = ' '; }
        else if (c === 'M') { const q = new Cube(V(sx, sy - 14)); q.mirror = 1; this.cubes.push(q); c = ' '; }
        else if (c === 'N') { const q = new Cube(V(sx, sy - 14)); q.mirror = -1; this.cubes.push(q); c = ' '; }
        else if (c === 'E') { this.exit = V(x, y); this.hasExit = true; c = ' '; }
        else if (bg >= 0) { this.buttons.push({ x, y, group: bg, pressed: false, timed: World.isTimedButton(c), holdT: 0, press: 0 }); c = ' '; }
        else if (rg >= 0) { this.receivers.push({ x, y, group: rg, active: false, glow: 0 }); }
        else if (c === '>') this.emitters.push({ x, y, dir: V(1, 0) });
        else if (c === '<') this.emitters.push({ x, y, dir: V(-1, 0) });
        else if (c === '^') this.emitters.push({ x, y, dir: V(0, -1) });
        else if (c === 'v') this.emitters.push({ x, y, dir: V(0, 1) });
        else if (c === 'u') { blueMarker = V(x, y); c = ' '; }
        else if (c === 'o') { orangeMarker = V(x, y); c = ' '; }
        this.tiles[x][y] = c;
      }
    }
    if (blueMarker) this.portals[0] = this.makeFixed(PortalColor.Blue, blueMarker, def.blueDir);
    if (orangeMarker) this.portals[1] = this.makeFixed(PortalColor.Orange, orangeMarker, def.orangeDir);
    for (const p of this.portals) if (p) p.open = 1;
    this.updateGroups(0, true);
  }

  get linked() { return !!(this.portals[0] && this.portals[1]); }
  get widthPx() { return this.w * T; }
  get heightPx() { return this.h * T; }

  makeFixed(color, marker, dir) {
    const a = V(marker.x + dir.x, marker.y + dir.y);
    const normal = V(-dir.x, -dir.y);
    const hit = V(a.x * T + T / 2 + normal.x * (T / 2), a.y * T + T / 2 + normal.y * (T / 2));
    const p = this.buildPortal(color, a, normal, hit, true, true);
    if (!p) throw new Error('Bad fixed portal in level ' + this.def.name);
    return p;
  }

  // ---------------------------------------------------------------- tiles

  at(x, y) { return x < 0 || y < 0 || x >= this.w || y >= this.h ? 'X' : this.tiles[x][y]; }

  isSolid(x, y) {
    const c = this.at(x, y);
    switch (c) {
      case '#': case 'X': case '=': case '<': case '>': case '^': case 'v': case 'J': case 'Q': case 'q': case 'G': return true;
      default:
        if (c >= 'a' && c <= 'f') return this.doorOpen[c.charCodeAt(0) - 97] < 0.98;
        return World.isReceiver(c);
    }
  }
  blocksLaser(x, y) { return this.at(x, y) !== '=' && this.isSolid(x, y); }
  isPortalable(x, y) { return this.at(x, y) === '#'; }

  castGrid(o, d, maxDist, blocks) {
    let x = Math.floor(o.x / T), y = Math.floor(o.y / T);
    if (blocks(x, y)) return { hit: true, dist: 0, tile: V(x, y), normal: V(-d.x, -d.y), point: o };
    const sx = Math.sign(d.x), sy = Math.sign(d.y);
    const tdx = sx !== 0 ? T / Math.abs(d.x) : Infinity;
    const tdy = sy !== 0 ? T / Math.abs(d.y) : Infinity;
    let tmx = sx > 0 ? ((x + 1) * T - o.x) / d.x : sx < 0 ? (x * T - o.x) / d.x : Infinity;
    let tmy = sy > 0 ? ((y + 1) * T - o.y) / d.y : sy < 0 ? (y * T - o.y) / d.y : Infinity;
    for (let i = 0; i < 4096; i++) {
      let t, n;
      if (tmx < tmy) { t = tmx; x += sx; tmx += tdx; n = V(-sx, 0); }
      else { t = tmy; y += sy; tmy += tdy; n = V(0, -sy); }
      if (t > maxDist) break;
      if (x < -1 || y < -1 || x > this.w || y > this.h) break;
      if (blocks(x, y)) return { hit: true, dist: t, tile: V(x, y), normal: n, point: V(o.x + d.x * t, o.y + d.y * t) };
    }
    return { hit: false, dist: maxDist, tile: V(0, 0), normal: V(0, 0), point: V(o.x + d.x * maxDist, o.y + d.y * maxDist) };
  }

  // ---------------------------------------------------------------- portals

  buildPortal(color, tile, normal, point, isFixed = false, forcePair = false) {
    const wall = Math.abs(normal.x) > 0.5;
    const step = wall ? V(0, 1) : V(1, 0);
    const nrm = V(Math.trunc(normal.x), Math.trunc(normal.y));
    const frac = wall ? point.y / T - tile.y : point.x / T - tile.x;
    const offsets = forcePair ? [0] : frac < 0.5 ? [-1, 0] : [0, -1];
    for (const off of offsets) {
      const a = V(tile.x + step.x * off, tile.y + step.y * off);
      const b = V(a.x + step.x, a.y + step.y);
      if (!this.pairTileOk(a, nrm, isFixed) || !this.pairTileOk(b, nrm, isFixed)) continue;
      const other = this.portals[1 - color];
      if (other && other.normal.x === normal.x && other.normal.y === normal.y && (other.covers(a.x, a.y) || other.covers(b.x, b.y))) continue;
      const center = wall
        ? V(normal.x > 0 ? (a.x + 1) * T : a.x * T, b.y * T)
        : V(b.x * T, normal.y > 0 ? (a.y + 1) * T : a.y * T);
      return new Portal(color, center, normal, a, b, isFixed);
    }
    return null;
  }

  pairTileOk(t, n, isFixed) {
    if (t.x < 0 || t.y < 0 || t.x >= this.w || t.y >= this.h) return false;
    if (!isFixed && !this.isPortalable(t.x, t.y)) return false;
    return !this.isSolid(t.x + n.x, t.y + n.y);
  }

  gunAllows(c) {
    return this.def.gun === GunMode.Both || (this.def.gun === GunMode.BlueOnly && c === PortalColor.Blue);
  }

  previewShot(origin, dir, color) {
    const hit = this.castGrid(origin, dir, 3000, (x, y) => this.isSolid(x, y));
    if (!hit.hit || hit.dist <= 0) return { hit, candidate: null };
    return { hit, candidate: this.buildPortal(color, hit.tile, hit.normal, hit.point) };
  }

  shoot(p, color) {
    const origin = World.gunOrigin(p);
    const { hit, candidate: cand } = this.previewShot(origin, p.aim, color);
    const col = color === PortalColor.Blue ? Palette.Blue : Palette.Orange;
    this.fx.trail(V(origin.x + p.aim.x * 18, origin.y + p.aim.y * 18), hit.point, col);
    p.shootCooldown = 0.22;
    p.lastShot = color;
    p.gunKick = 1;
    if (cand && !(this.portals[color]?.fixed)) {
      this.portals[color] = cand;
      this.portalsPlaced++;
      this.fx.burst(cand.center, col, 26, 260, 0.6, 3.5, 0, PKind.Spark, true, cand.normal, 1.2);
      this.fx.burst(cand.center, Palette.White, 8, 120, 0.4, 2.5);
      this.emit(color === PortalColor.Blue ? Sfx.PortalBlue : Sfx.PortalOrange, cand.center);
    } else {
      this.fx.burst(hit.point, mix(col, Palette.White, 0.5), 10, 160, 0.35, 2.5, 400, PKind.Spark, true, hit.normal, 1.0);
      this.emit(Sfx.PortalFail, hit.point);
    }
  }

  static gunOrigin(p) { return V(p.pos.x, p.pos.y - 2); }

  portalOnFace(tile, normal) {
    for (const p of this.portals) if (p && p.normal.x === normal.x && p.normal.y === normal.y && p.covers(tile.x, tile.y)) return p;
    return null;
  }

  clearPlayerPortals() {
    let any = false;
    for (let i = 0; i < 2; i++) {
      const p = this.portals[i];
      if (p && !p.fixed) {
        this.fx.burst(p.center, i === 0 ? Palette.Blue : Palette.Orange, 30, 200, 0.7, 3);
        this.portals[i] = null;
        any = true;
      }
    }
    if (any && this.player) this.emit(Sfx.Fizzle, this.player.pos);
  }

  // ---------------------------------------------------------------- update

  emit(s, pos, shake = 0) { this.events.push({ sound: s, pos: V(pos.x, pos.y), shake }); }

  update(dt, input) {
    this.time += dt;
    for (const p of this.portals) if (p) { p.open = Math.min(1, p.open + dt * 5); p.age += dt; }
    if (this.player) this.updatePlayer(this.player, dt, input);
    for (const c of this.cubes) this.updateCube(c, dt);
    this.computeLasers();
    this.updateGroups(dt, false);
    this.updateHazards();
    if (this.player && !this.player.dead) this.unstick(this.player);
    for (const c of this.cubes) if (c.solid && !c.held) this.unstick(c);
    this.ambientFx(dt);
    this.fx.update(dt);
  }

  updatePlayer(p, dt, input) {
    const G = World.Gravity;
    p.teleportFlash = Math.max(0, p.teleportFlash - dt * 3);
    p.gunKick = Math.max(0, p.gunKick - dt * 8);
    p.squash = approach(p.squash, 0, dt * 2.5);
    if (p.dead) { p.deadTimer += dt; return; }
    if (p.won) {
      p.vel.x = approach(p.vel.x, 0, 2000 * dt);
      p.vel.y = Math.min(p.vel.y + G * dt, World.MaxFall);
      this.moveBody(p, dt, true);
      return;
    }

    p.aim = safeNormalize(V(input.aimWorld.x - World.gunOrigin(p).x, input.aimWorld.y - World.gunOrigin(p).y), V(p.facing, 0));
    p.facing = p.aim.x >= 0 ? 1 : -1;
    p.shootCooldown -= dt;

    // jumping with coyote time + buffering
    if (p.onGround) p.coyote = 0.1; else p.coyote -= dt;
    if (input.jumpPressed) p.jumpBuffer = 0.14; else p.jumpBuffer -= dt;
    if (p.jumpBuffer > 0 && p.coyote > 0) {
      p.vel.y = -World.JumpVel;
      p.coyote = 0; p.jumpBuffer = 0; p.jumpCut = false;
      p.squash = -0.22;
      this.emit(Sfx.Jump, p.pos);
    }
    if (!input.jumpHeld && !p.jumpCut && p.vel.y < -200 && p.vel.y > -World.JumpVel - 1) { p.vel.y *= 0.5; p.jumpCut = true; }

    const dir = input.moveX;
    if (p.onGround) {
      const top = this.tileUnder(p) === 'G' ? World.FastWalk : World.MaxWalk;
      if (dir !== 0 && Math.abs(p.vel.x) > top && Math.sign(p.vel.x) === dir) p.vel.x = approach(p.vel.x, dir * top, 1400 * dt);
      else p.vel.x = approach(p.vel.x, dir * top, (dir === 0 ? 2600 : 2400) * dt);
    } else if (dir !== 0) {
      if (p.vel.x * dir < World.MaxWalk) p.vel.x = approach(p.vel.x, dir * World.MaxWalk, 1500 * dt);
    } else {
      p.vel.x = approach(p.vel.x, 0, (Math.abs(p.vel.x) <= World.MaxWalk * 1.05 ? 1600 : 120) * dt);
    }
    p.vel.y = Math.min(p.vel.y + G * dt, World.MaxFall);

    const wasGround = p.onGround;
    const prevVy = p.vel.y;
    this.moveBody(p, dt, true);
    if (!wasGround && p.onGround && prevVy > 350) {
      p.squash = Math.min(0.35, prevVy / 3000);
      this.emit(Sfx.Land, V(p.pos.x, p.pos.y + p.half.y), prevVy > 1000 ? 6 : 0);
      this.fx.burst(V(p.pos.x, p.pos.y + p.half.y), Palette.Dust, 8, 90, 0.4, 3, 200, PKind.Dot, false, V(0, -1), 1.3);
    }
    if (p.onGround && Math.abs(p.vel.x) > 10) p.walkPhase += Math.abs(p.vel.x) * dt * 0.045;
    else if (p.onGround) p.walkPhase = approach(p.walkPhase % Math.PI, 0, dt * 6);

    // pick up / drop
    if (input.usePressed) {
      if (p.holding) this.dropCube(p, true);
      else {
        let best = null, bestD = 80;
        for (const c of this.cubes) {
          if (!c.solid) continue;
          const d = vdist(c.pos, p.pos);
          const dirTo = safeNormalize(V(c.pos.x - p.pos.x, c.pos.y - p.pos.y), V(1, 0));
          if (d < bestD && !this.castGrid(p.pos, dirTo, d, (x, y) => this.isSolid(x, y)).hit) { best = c; bestD = d; }
        }
        if (best) { p.holding = best; best.held = true; this.emit(Sfx.Pickup, best.pos); }
      }
    }

    if (p.shootCooldown <= 0) {
      if (input.fireBlue && this.gunAllows(PortalColor.Blue)) this.shoot(p, PortalColor.Blue);
      else if (input.fireOrange && this.gunAllows(PortalColor.Orange)) this.shoot(p, PortalColor.Orange);
      else if (input.fireCycle) {
        let c = this.nextShot;
        if (!this.gunAllows(c)) c = c === PortalColor.Blue ? PortalColor.Orange : PortalColor.Blue;
        if (this.gunAllows(c)) {
          this.shoot(p, c);
          this.nextShot = c === PortalColor.Blue ? PortalColor.Orange : PortalColor.Blue;
        }
      }
    }

    // exit
    if (this.hasExit && overlaps(p.rect, { x: this.exit.x * T + 6, y: (this.exit.y - 1) * T, w: T - 12, h: T * 2 })) {
      p.won = true;
      this.dropCube(p, false);
      this.emit(Sfx.Win, p.pos);
      this.fx.burst(p.pos, Palette.Green, 40, 220, 0.9, 3);
    }
  }

  dropCube(p, toss) {
    const c = p.holding;
    if (!c) return;
    c.held = false;
    if (toss) c.vel = V(p.vel.x * 0.6 + p.aim.x * 110, p.vel.y * 0.6 + p.aim.y * 110);
    p.holding = null;
    this.emit(Sfx.Drop, c.pos);
  }

  updateCube(c, dt) {
    c.teleportFlash = Math.max(0, c.teleportFlash - dt * 3);
    if (c.gone) {
      c.respawnT -= dt;
      if (c.respawnT <= 0) {
        c.gone = false; c.dissolving = false; c.dissolveT = 0;
        c.pos = V(c.spawn.x, c.spawn.y); c.vel = V(0, 0);
        this.fx.burst(c.pos, Palette.Blue, 20, 140, 0.5, 2.5);
        this.emit(Sfx.CubeSpawn, c.pos);
      }
      return;
    }
    if (c.dissolving) {
      c.dissolveT += dt;
      c.vel.x *= 0.9; c.vel.y *= 0.9;
      c.pos.x += c.vel.x * dt; c.pos.y += c.vel.y * dt - 12 * dt;
      if (this.fx.r(0, 1) < 0.6) this.fx.add({
        px: c.pos.x + this.fx.r(-14, 14), py: c.pos.y + this.fx.r(-14, 14), vx: this.fx.r(-20, 20), vy: this.fx.r(-60, -20),
        life: 0.6, maxLife: 0.6, size: 2.5, gravity: 0, drag: 0, color: Palette.White, kind: PKind.Square, additive: true,
      });
      if (c.dissolveT > 0.8) { c.gone = true; c.respawnT = 0.7; }
      return;
    }
    if (c.held) {
      const p = this.player;
      if (!p || p.dead) { c.held = false; if (p) p.holding = null; return; }
      const target = V(p.pos.x + p.aim.x * 36, p.pos.y - 2 + p.aim.y * 36);
      c.vel = V((target.x - c.pos.x) * 60, (target.y - c.pos.y) * 60);
      const l = Math.hypot(c.vel.x, c.vel.y);
      if (l > 2400) { c.vel.x *= 2400 / l; c.vel.y *= 2400 / l; }
      this.moveBody(c, dt, false);
      if (vdist(c.pos, target) > 90) this.dropCube(p, false);
      return;
    }
    c.vel.y = Math.min(c.vel.y + World.Gravity * dt, World.MaxFall);
    c.vel.x = approach(c.vel.x, 0, (c.onGround ? 1600 : 50) * dt);
    const wasGround = c.onGround;
    const prevVy = c.vel.y;
    this.moveBody(c, dt, true);
    if (!wasGround && c.onGround && prevVy > 400) this.emit(Sfx.Land, c.pos, 0);
  }

  // ---------------------------------------------------------------- physics

  tileUnder(b) { return this.at(Math.floor(b.pos.x / T), Math.floor((b.pos.y + b.half.y + 2) / T)); }

  usesPortals(b) { return !(b instanceof Cube && b.held); }

  passableFor(b, x, y) {
    if (!this.linked || !this.usesPortals(b)) return false;
    for (const p of this.portals) {
      if (!p || !p.covers(x, y)) continue;
      const u = p.u(b.pos), v = p.v(b.pos);
      if (Math.abs(u) + b.extentAlong(p.up) <= HALF_LEN + 1.5 && v > -6) return true;
    }
    return false;
  }

  solidFor(b, x, y) { return this.isSolid(x, y) && !this.passableFor(b, x, y); }

  moveBody(b, dt, canTeleport) {
    const dx = b.vel.x * dt, dy = b.vel.y * dt;
    const steps = Math.max(1, Math.ceil(Math.max(Math.abs(dx), Math.abs(dy)) / 5));
    b.onGround = false;
    for (let i = 0; i < steps; i++) {
      const sx = b.vel.x * dt / steps, sy = b.vel.y * dt / steps;
      b.pos.x += sx; this.resolveX(b, sx);
      b.pos.y += sy; this.resolveY(b, sy);
      if (canTeleport) this.tryTeleport(b);
    }
  }

  tileRange(r) {
    return [
      Math.floor(r.x / T), Math.floor((r.x + r.w - 0.01) / T),
      Math.floor(r.y / T), Math.floor((r.y + r.h - 0.01) / T),
    ];
  }

  solidBoxesFor(b) { return this.cubes.filter((c) => c !== b && c.solid && !c.held); }

  resolveX(b, dx) {
    if (dx === 0) return;
    const [x0, x1, y0, y1] = this.tileRange(b.rect);
    outer: {
      if (dx > 0) {
        for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++)
          if (this.solidFor(b, x, y)) { b.pos.x = x * T - b.half.x - 0.01; if (b.vel.x > 0) b.vel.x = 0; break outer; }
      } else {
        for (let x = x1; x >= x0; x--) for (let y = y0; y <= y1; y++)
          if (this.solidFor(b, x, y)) { b.pos.x = (x + 1) * T + b.half.x + 0.01; if (b.vel.x < 0) b.vel.x = 0; break outer; }
      }
    }
    for (const c of this.solidBoxesFor(b)) {
      if (!overlaps(b.rect, c.rect)) continue;
      if (b instanceof Player && b.pos.y + b.half.y > c.pos.y - c.half.y + 6) {
        // push the cube
        const pen = dx > 0 ? (b.pos.x + b.half.x) - (c.pos.x - c.half.x) : (b.pos.x - b.half.x) - (c.pos.x + c.half.x);
        c.pos.x += pen;
        this.resolveX(c, pen);
        c.vel.x = b.vel.x * 0.8;
      }
      if (!overlaps(b.rect, c.rect)) continue;
      if (dx > 0) { b.pos.x = c.pos.x - c.half.x - b.half.x - 0.01; if (b.vel.x > 0) b.vel.x = 0; }
      else { b.pos.x = c.pos.x + c.half.x + b.half.x + 0.01; if (b.vel.x < 0) b.vel.x = 0; }
    }
  }

  resolveY(b, dy) {
    if (dy === 0) return;
    const [x0, x1, y0, y1] = this.tileRange(b.rect);
    const heldCube = b instanceof Cube && b.held;
    outer: {
      if (dy > 0) {
        for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
          if (!this.solidFor(b, x, y)) continue;
          b.pos.y = y * T - b.half.y - 0.01;
          const ch = this.at(x, y);
          if (ch === 'J' && !heldCube) {
            // repulsion gel: bounce back up, keeping (most of) the impact speed
            const speed = Math.max(b.vel.y * 0.95, World.GelBounce);
            b.vel.y = -speed;
            if (b instanceof Player) b.jumpCut = true;
            this.fx.burst(V(b.pos.x, y * T), Palette.Blue, 10, 160, 0.4, 2.5, 300, PKind.Dot, true, V(0, -1), 0.9);
            this.emit(Sfx.Jump, b.pos);
            break outer;
          }
          if (World.isPlate(ch) && !heldCube) {
            // launch plate: a fixed kick up and sideways, whatever the impact speed
            b.vel = V(ch === 'Q' ? World.PlateVx : -World.PlateVx, -World.PlateVy);
            if (b instanceof Player) b.jumpCut = true;
            this.fx.burst(V(b.pos.x, y * T), Palette.Orange, 12, 200, 0.4, 2.5, 300, PKind.Dot, true, V(0, -1), 0.9);
            this.emit(Sfx.Jump, b.pos, 2);
            break outer;
          }
          if (b.vel.y > 0) b.vel.y = 0;
          b.onGround = true;
          break outer;
        }
      } else {
        for (let y = y1; y >= y0; y--) for (let x = x0; x <= x1; x++)
          if (this.solidFor(b, x, y)) { b.pos.y = (y + 1) * T + b.half.y + 0.01; if (b.vel.y < 0) b.vel.y = 0; break outer; }
      }
    }
    for (const c of this.solidBoxesFor(b)) {
      if (!overlaps(b.rect, c.rect)) continue;
      if (dy > 0) { b.pos.y = c.pos.y - c.half.y - b.half.y - 0.01; if (b.vel.y > 0) b.vel.y = 0; b.onGround = true; }
      else { b.pos.y = c.pos.y + c.half.y + b.half.y + 0.01; if (b.vel.y < 0) b.vel.y = 0; }
    }
  }

  tryTeleport(b) {
    if (!this.linked || !this.usesPortals(b)) return false;
    for (let i = 0; i < 2; i++) {
      const p = this.portals[i], q = this.portals[1 - i];
      const v = p.v(b.pos);
      if (v >= 0 || v < -24) continue;
      if (Math.abs(p.u(b.pos)) + b.extentAlong(p.up) > HALF_LEN + 1.5) continue;

      let np = Portal.mapPoint(p, q, b.pos);
      let nv = Portal.mapDir(p, q, b.vel);
      const vout = q.v(np);
      if (vout < 0.5) np = V(np.x + q.normal.x * (0.5 - vout), np.y + q.normal.y * (0.5 - vout));
      const minOut = q.normal.y < -0.5 ? 330 : 160;
      const along = nv.x * q.normal.x + nv.y * q.normal.y;
      if (along < minOut) nv = V(nv.x + q.normal.x * (minOut - along), nv.y + q.normal.y * (minOut - along));
      const speed = Math.hypot(nv.x, nv.y);
      if (speed > 1800) nv = V(nv.x * 1800 / speed, nv.y * 1800 / speed);

      b.pos = np;
      b.vel = nv;
      b.teleportFlash = 1;
      b.teleports++;
      b.onGround = false;
      const colA = i === 0 ? Palette.Blue : Palette.Orange;
      const colB = i === 0 ? Palette.Orange : Palette.Blue;
      this.fx.burst(p.center, colA, 12, 150, 0.4, 2.5, 0, PKind.Spark, true, V(-p.normal.x, -p.normal.y), 1.0);
      this.fx.burst(q.center, colB, 16, 220, 0.5, 2.5, 0, PKind.Spark, true, q.normal, 1.0);
      if (b instanceof Player) {
        b.jumpCut = true;
        this.emit(Sfx.Teleport, q.center);
        const held = b.holding;
        if (held) {
          held.pos = V(np.x + q.normal.x * 18, np.y + q.normal.y * 18);
          held.vel = V(nv.x, nv.y);
          held.teleportFlash = 1;
        }
      } else this.emit(Sfx.Teleport, q.center);
      return true;
    }
    return false;
  }

  overlapsSolid(b, pos) {
    const r = { x: pos.x - b.half.x, y: pos.y - b.half.y, w: b.half.x * 2, h: b.half.y * 2 };
    const [x0, x1, y0, y1] = this.tileRange(r);
    const saved = b.pos;
    b.pos = pos;
    let hit = false;
    for (let y = y0; y <= y1 && !hit; y++) for (let x = x0; x <= x1 && !hit; x++) if (this.solidFor(b, x, y)) hit = true;
    b.pos = saved;
    return hit;
  }

  unstick(b) {
    if (!this.overlapsSolid(b, b.pos)) return;
    const dirs = [V(0, -1), V(0, 1), V(-1, 0), V(1, 0), V(-1, -1), V(1, -1)];
    for (let d = 2; d <= 64; d += 2)
      for (const dir of dirs) {
        const np = V(b.pos.x + dir.x * d, b.pos.y + dir.y * d);
        if (!this.overlapsSolid(b, np)) { b.pos = np; return; }
      }
  }

  // ---------------------------------------------------------------- lasers, groups, hazards

  computeLasers() {
    this.lasers.length = 0;
    this.laserImpacts.length = 0;
    for (const r of this.receivers) r.active = false;
    let hitPlayer = false;
    const blocks = (x, y) => this.blocksLaser(x, y);

    for (const e of this.emitters) {
      let origin = V(e.x * T + T / 2 + e.dir.x * (T / 2 + 0.05), e.y * T + T / 2 + e.dir.y * (T / 2 + 0.05));
      let dir = e.dir;
      for (let bounce = 0; bounce < 10; bounce++) {
        const hit = this.castGrid(origin, dir, 5000, blocks);
        let dist = hit.dist;
        let kind = 0; // 0 tile/none, 1 cube, 2 player
        let hitCube = null;
        for (const c of this.cubes) {
          if (!c.solid) continue;
          const t = rayAabb(origin, dir, c.rect);
          if (t >= 0 && t < dist) { dist = t; kind = 1; hitCube = c; }
        }
        const pl = this.player;
        if (pl && !pl.dead && !pl.won) {
          const r0 = pl.rect;
          const r = { x: r0.x + 3, y: r0.y + 3, w: r0.w - 6, h: r0.h - 6 };
          const t = rayAabb(origin, dir, r);
          if (t >= 0 && t < dist) { dist = t; kind = 2; }
        }
        const end = V(origin.x + dir.x * dist, origin.y + dir.y * dist);
        this.lasers.push({ a: origin, b: end });
        if (kind === 2) { hitPlayer = true; this.laserImpacts.push(end); break; }
        if (kind === 1 && hitCube.mirror !== 0) {
          // mirror cube: '/' turns a beam (dx,dy) into (-dy,-dx), '\' into (dy,dx)
          dir = hitCube.mirror > 0 ? V(-dir.y, -dir.x) : V(dir.y, dir.x);
          origin = V(hitCube.pos.x + dir.x * (hitCube.half.x + 0.6), hitCube.pos.y + dir.y * (hitCube.half.x + 0.6));
          this.laserImpacts.push(end);
          continue;
        }
        if (kind === 1 || !hit.hit) { this.laserImpacts.push(end); break; }

        const p = this.linked ? this.portalOnFace(hit.tile, hit.normal) : null;
        if (p) {
          const q = this.portals[1 - p.color];
          dir = Portal.mapDir(p, q, dir);
          const mp = Portal.mapPoint(p, q, end);
          origin = V(mp.x + dir.x * 0.05, mp.y + dir.y * 0.05);
          continue;
        }
        for (const r of this.receivers) if (r.x === hit.tile.x && r.y === hit.tile.y) r.active = true;
        this.laserImpacts.push(end);
        break;
      }
    }
    if (hitPlayer && this.player) this.kill(this.player, Palette.LaserRed);
  }

  updateGroups(dt, initial) {
    const hold = World.ButtonHold;
    for (const b of this.buttons) {
      let pressed = false;
      const trig = { x: b.x * T - 10, y: b.y * T + T - 12, w: T + 20, h: 12 };
      const pl = this.player;
      if (pl && !pl.dead && overlaps(pl.rect, trig)) pressed = true;
      for (const c of this.cubes) if (c.solid && !c.held && overlaps(c.rect, trig)) { pressed = true; c.onButtonGlow = 1; }
      if (b.timed) {
        if (pressed) b.holdT = hold; else b.holdT = Math.max(0, b.holdT - dt);
        pressed = b.holdT > 0;
      }
      if (pressed !== b.pressed && !initial) this.emit(pressed ? Sfx.ButtonDown : Sfx.ButtonUp, V(b.x * T + 16, b.y * T + 28));
      b.pressed = pressed;
      b.press = approach(b.press, pressed ? 1 : 0, dt * 10);
    }
    for (const c of this.cubes) c.onButtonGlow = Math.max(0, c.onButtonGlow - dt * 2);
    for (const r of this.receivers) {
      if (r.active && r.glow < 0.05 && !initial) this.emit(Sfx.ReceiverOn, V(r.x * T + 16, r.y * T + 16));
      r.glow = approach(r.glow, r.active ? 1 : 0, dt * 4);
    }
    for (let g = 0; g < World.Groups; g++) {
      let total = 0, on = 0;
      for (const b of this.buttons) if (b.group === g) { total++; if (b.pressed) on++; }
      for (const r of this.receivers) if (r.group === g) { total++; if (r.active) on++; }
      const active = total > 0 && on === total;
      if (active !== this.groupActive[g] && !initial) this.emit(active ? Sfx.DoorOpen : Sfx.DoorClose, this.doorCenter(g));
      this.groupActive[g] = active;
      if (active) this.doorOpen[g] = Math.min(1, this.doorOpen[g] + dt * 2.5);
      else if (!this.doorBlocked(g)) this.doorOpen[g] = Math.max(0, this.doorOpen[g] - dt * 2.5);
    }
  }

  doorCenter(g) {
    let sx = 0, sy = 0, n = 0;
    const dc = World.doorChar(g);
    for (let y = 0; y < this.h; y++) for (let x = 0; x < this.w; x++)
      if (this.tiles[x][y] === dc) { sx += x * T + 16; sy += y * T + 16; n++; }
    return n > 0 ? V(sx / n, sy / n) : this.player ? this.player.pos : V(0, 0);
  }

  doorBlocked(g) {
    const dc = World.doorChar(g);
    const bodies = this.cubes.filter((c) => c.solid);
    if (this.player && !this.player.dead) bodies.push(this.player);
    for (const b of bodies) {
      const [x0, x1, y0, y1] = this.tileRange(b.rect);
      for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) if (this.at(x, y) === dc) return true;
    }
    return false;
  }

  updateHazards() {
    const p = this.player;
    if (p && !p.dead && !p.won) {
      const r = p.rect;
      if (this.touchesTile({ x: r.x + 3, y: r.y, w: r.w - 6, h: r.h - 8 }, '~')) this.kill(p, Palette.Goo);
      else if (this.touchesTile(r, '|')) {
        this.clearPlayerPortals();
        const held = p.holding;
        if (held) { p.holding = null; held.held = false; this.dissolve(held); }
      }
    }
    for (const c of this.cubes) {
      if (!c.solid) continue;
      const r = c.rect;
      if (this.touchesTile({ x: r.x + 2, y: r.y, w: r.w - 4, h: r.h - 6 }, '~') || this.touchesTile(r, '|')) {
        if (c.held && this.player) { this.player.holding = null; c.held = false; }
        this.dissolve(c);
      }
    }
  }

  dissolve(c) {
    if (c.dissolving) return;
    c.dissolving = true;
    c.dissolveT = 0;
    this.emit(Sfx.CubeDissolve, c.pos);
  }

  touchesTile(r, ch) {
    const [x0, x1, y0, y1] = this.tileRange(r);
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) if (this.at(x, y) === ch) return true;
    return false;
  }

  kill(p, color) {
    if (p.dead || p.won) return;
    p.dead = true;
    p.deadTimer = 0;
    if (p.holding) { p.holding.held = false; p.holding = null; }
    this.fx.burst(p.pos, color, 50, 320, 0.9, 4, 600, PKind.Square, false);
    this.fx.burst(p.pos, Palette.White, 20, 200, 0.5, 3);
    this.emit(Sfx.Death, p.pos, 10);
  }

  ambientFx() {
    const fx = this.fx;
    for (const p of this.portals) {
      if (!p || fx.r(0, 1) > 0.5) continue;
      const col = p.color === PortalColor.Blue ? Palette.Blue : Palette.Orange;
      const u = fx.r(-1, 1) * HALF_LEN, n = fx.r(0, 4), sp = fx.r(15, 50), side = fx.r(-10, 10);
      fx.add({
        px: p.center.x + p.up.x * u + p.normal.x * n, py: p.center.y + p.up.y * u + p.normal.y * n,
        vx: p.normal.x * sp + p.up.x * side, vy: p.normal.y * sp + p.up.y * side,
        life: 0.8, maxLife: 0.8, size: fx.r(1, 2.2), gravity: 0, drag: 1, color: col, kind: PKind.Dot, additive: true,
      });
    }
    if (fx.r(0, 1) < 0.35) {
      const x = Math.floor(fx.r(0, this.w)), y = Math.floor(fx.r(0, this.h));
      if (this.at(x, y) === '~' && this.at(x, y - 1) !== '~')
        fx.add({
          px: x * T + fx.r(4, 28), py: y * T + 4, vx: 0, vy: -fx.r(10, 30),
          life: 0.7, maxLife: 0.7, size: fx.r(1.5, 3.5), gravity: 0, drag: 0, color: Palette.GooLight, kind: PKind.Ring, additive: true,
        });
    }
    for (let i = 0; i < 3; i++) {
      const x = Math.floor(fx.r(0, this.w)), y = Math.floor(fx.r(0, this.h));
      if (this.at(x, y) === '|')
        fx.add({
          px: x * T + fx.r(8, 24), py: y * T + fx.r(0, 32), vx: fx.r(-8, 8), vy: fx.r(-30, 30),
          life: 0.5, maxLife: 0.5, size: fx.r(1, 2), gravity: 0, drag: 0, color: Palette.Fizzler, kind: PKind.Dot, additive: true,
        });
    }
    for (const imp of this.laserImpacts)
      if (fx.r(0, 1) < 0.4) fx.burst(imp, Palette.LaserRed, 1, 120, 0.3, 2, 300, PKind.Spark);
  }
}

/** One game frame at 60 fps = two sim steps; presses only count in the first (mirrors TestRunner.StepFrame). */
export function stepFrame(w, input) {
  w.update(K.SimDt, input);
  const i2 = { ...input, jumpPressed: false, usePressed: false, fireBlue: false, fireOrange: false, fireCycle: false };
  w.update(K.SimDt, i2);
  w.events.length = 0;
}
