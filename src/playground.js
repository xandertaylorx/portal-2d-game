// The playground: an endless world generated from noise, with biomes (wetlands, meadows, forests, deserts, mountains),
// caves with sinkholes and crystal chambers, lakes and lava, and portal toys scattered through it: gel bounce pads,
// speed-gel runs, launch tower courses, lift towers and lab ruins. Every chunk is a pure function of (index, seed), so
// terrain is seamless across chunk edges and far-away portal destinations never have to stay loaded.
import { K, V, hash, clamp, lerp, approach } from './core.js';
import { World, Player, Cube, GunMode, makeDef, Sfx } from './world.js';

export const CHUNK_W = 80;
export const WORLD_H = 96;
export const DAY_SECONDS = 240;
export const SEA = 38;          // waterline row
export const LAVA_Y = 88;       // everything open at or below this row is lava
const T = K.Tile;

// ------------------------------------------------------------------ noise

const smooth = (t) => t * t * (3 - 2 * t);
const n1 = (x, seed) => { const i = Math.floor(x), f = x - i; return lerp(hash(i, 3, seed), hash(i + 1, 3, seed), smooth(f)); };
function fbm1(x, seed, oct = 3) {
  let a = 0, amp = 1, tot = 0, f = 1;
  for (let o = 0; o < oct; o++) { a += n1(x * f + o * 31.7, seed + o * 17) * amp; tot += amp; amp *= 0.5; f *= 2.07; }
  return a / tot;
}
const n2 = (x, y, seed) => {
  const ix = Math.floor(x), iy = Math.floor(y), fx = smooth(x - ix), fy = smooth(y - iy);
  return lerp(lerp(hash(ix, iy, seed), hash(ix + 1, iy, seed), fx), lerp(hash(ix, iy + 1, seed), hash(ix + 1, iy + 1, seed), fx), fy);
};
function fbm2(x, y, seed, oct = 3) {
  let a = 0, amp = 1, tot = 0, f = 1;
  for (let o = 0; o < oct; o++) { a += n2(x * f + o * 13.3, y * f + o * 7.1, seed + o * 23) * amp; tot += amp; amp *= 0.5; f *= 2.1; }
  return a / tot;
}

/** 0..1 across the world: wetland, meadow, forest, desert, mountain. Calm meadow around the start. */
export function biomeAt(ax, seed) {
  const raw = fbm1(ax / 240 + 0.5, seed + 101, 3);
  const b = clamp((raw - 0.5) * 1.75 + 0.5, 0, 1);
  return lerp(0.36, b, clamp((Math.abs(ax) - 20) / 110, 0, 1));
}
export const biomeName = (b) => (b < 0.25 ? 'wetland' : b < 0.43 ? 'meadow' : b < 0.6 ? 'forest' : b < 0.76 ? 'desert' : 'mountain');

/** The natural ground row at world column ax (before any structure flattens it). */
export function naturalSurface(ax, seed) {
  const b = biomeAt(ax, seed);
  const m = smooth(clamp((b - 0.6) / 0.3, 0, 1)), wet = smooth(clamp((0.32 - b) / 0.3, 0, 1));
  const slow = fbm1(ax / 60, seed + 7, 3), fast = fbm1(ax / 11, seed + 9, 2);
  let y = 33 + wet * 4 - m * 11 + (0.5 - slow) * (7 + m * 30) + (0.5 - fast) * (2 + m * 3);
  y = lerp(33, y, clamp((Math.abs(ax) - 20) / 90, 0, 1));
  return clamp(Math.round(y), 8, 56);
}

/** Sinkholes: a 3-wide shaft from the surface down to a crystal chamber with a lift back up. One candidate per 60 columns. */
function sinkholeIn(cell, seed) {
  if (hash(cell, 2, seed) > 0.72) return null;
  const sx = cell * 60 + 10 + Math.floor(hash(cell, 1, seed) * 40);
  if (Math.abs(sx) < 70) return null;                         // keep the start area clear
  return {
    cell, sx, chamberY: 60 + Math.floor(hash(cell, 3, seed) * 14),
    rx: 7 + Math.floor(hash(cell, 4, seed) * 4), ry: 4 + Math.floor(hash(cell, 5, seed) * 2),
  };
}

const STRUCTURES = {
  lab: { w: 18, biomes: null },
  tower: { w: 14, biomes: null },
  fling: { w: 44, biomes: null },
  bounce: { w: 26, biomes: null },
  speed: { w: 46, biomes: null },
  park: { w: 28, biomes: null },
};

function pickStructure(index, slot, b, r) {
  if (index === 0 && slot === 0) return 'park';
  const roll = r(40 + slot);
  if (roll < 0.28) return null;
  const pool = b > 0.76 ? ['tower', 'fling', 'lab', 'bounce'] : b > 0.6 ? ['speed', 'bounce', 'tower', 'lab'] : b > 0.43 ? ['lab', 'fling', 'bounce', 'tower'] : b > 0.25 ? ['bounce', 'speed', 'lab', 'fling', 'tower'] : ['tower', 'bounce', 'lab'];
  return pool[Math.floor(r(50 + slot) * pool.length)];
}

// ------------------------------------------------------------------ chunk generation

export function generateChunk(index, seed) {
  const start = index * CHUNK_W;
  const r = (n) => hash(index, n, seed);
  const tiles = Array.from({ length: CHUNK_W }, () => new Array(WORLD_H).fill(' '));
  const surf = [], bio = [];
  for (let x = 0; x < CHUNK_W; x++) { bio[x] = biomeAt(start + x, seed); surf[x] = naturalSurface(start + x, seed); }
  const set = (x, y, c) => { if (x >= 0 && x < CHUNK_W && y >= 0 && y < WORLD_H) tiles[x][y] = c; };
  const fill = (x, y, w, h, c) => { for (let i = x; i < x + w; i++) for (let j = y; j < y + h; j++) set(i, j, c); };
  const out = { cubes: [], beams: [], relics: [], signs: [], lamps: [], props: [], structures: [], chambers: [], crystals: [] };

  // sinkholes that touch this chunk (including chambers that spill over a seam)
  const holes = [];
  for (let cell = Math.floor((start - 14) / 60); cell <= Math.floor((start + CHUNK_W + 14) / 60); cell++) { const h = sinkholeIn(cell, seed); if (h) holes.push(h); }

  // structure slots: choose, flatten the ground under them
  const slots = [[10, 36], [44, 70]];
  slots.forEach(([a, bEnd], slot) => {
    const mid = Math.floor((a + bEnd) / 2);
    let kind = pickStructure(index, slot, bio[mid], r);
    if (!kind) return;
    if (slot === 1 && STRUCTURES[kind].w > bEnd - a) kind = 'lab';       // the second slot only has room for a small structure
    const w = STRUCTURES[kind].w;
    const x0 = a + Math.floor(r(60 + slot) * Math.max(1, bEnd - a - w + 8));
    const x1 = x0 + w;
    if (x0 < 10 || x1 > CHUNK_W - 10) return;                              // ground flattening never reaches a chunk edge
    if (holes.some((h) => h.sx + 1 + h.rx + 6 > start + x0 && h.sx - h.rx - 6 < start + x1)) return;
    let sum = 0, n = 0;
    for (let x = x0; x < x1; x++) { sum += surf[x]; n++; }
    const fy = clamp(Math.round(sum / n), 16, SEA - 2);
    for (let x = Math.max(0, x0 - 10); x < Math.min(CHUNK_W, x1 + 10); x++) {
      const dist = x < x0 ? x0 - x : x >= x1 ? x - x1 + 1 : 0;
      surf[x] = Math.round(lerp(surf[x], fy, smooth(clamp(1 - dist / 10, 0, 1))));
    }
    out.structures.push({ kind, x0, x1, fy, slot });
  });

  // special ground shapes for the structures that need a gap
  for (const s of out.structures) {
    if (s.kind === 'fling') for (let x = s.x0 + 9; x < s.x0 + 25; x++) surf[x] = s.fy + 10;
    if (s.kind === 'speed') for (let x = s.x0 + 22; x < s.x0 + 31; x++) surf[x] = s.fy + 9;
  }

  // terrain, water
  for (let x = 0; x < CHUNK_W; x++) {
    for (let y = surf[x]; y < WORLD_H; y++) tiles[x][y] = 'X';
    for (let y = SEA; y < surf[x]; y++) tiles[x][y] = ':';
  }
  for (const s of out.structures) {
    if (s.kind === 'fling') for (let x = s.x0 + 9; x < s.x0 + 25; x++) for (let y = s.fy + 6; y < s.fy + 10; y++) tiles[x][y] = ':';
    if (s.kind === 'speed') for (let x = s.x0 + 22; x < s.x0 + 31; x++) for (let y = s.fy + 5; y < s.fy + 9; y++) tiles[x][y] = ':';
  }

  // caves: open pockets of noise below the surface
  for (let x = 0; x < CHUNK_W; x++) {
    const ax = start + x;
    for (let y = surf[x] + 6; y < LAVA_Y + 2; y++) {
      const c = fbm2(ax * 0.05, y * 0.085, seed + 31, 3);
      if (c > 0.605 - 0.05 * Math.min(1, (y - surf[x]) / 40)) tiles[x][y] = ' ';
    }
  }
  // sinkhole shafts and their chambers
  for (const h of holes) {
    for (let x = 0; x < CHUNK_W; x++) {
      const ax = start + x, dx = ax - (h.sx + 1);
      if (Math.abs(dx) <= 1) for (let y = Math.max(0, naturalSurface(ax, seed) - 2); y < h.chamberY + h.ry; y++) tiles[x][y] = ' ';
      for (let y = h.chamberY - h.ry; y <= h.chamberY + h.ry; y++) {
        const ex = dx / h.rx, ey = (y - h.chamberY) / h.ry;
        if (ex * ex + ey * ey <= 1) tiles[x][y] = ' ';
      }
    }
  }
  // lava lakes at the bottom, bedrock under them
  for (let x = 0; x < CHUNK_W; x++) {
    for (let y = LAVA_Y; y < WORLD_H - 3; y++) if (tiles[x][y] === ' ') tiles[x][y] = '~';
    for (let y = WORLD_H - 3; y < WORLD_H; y++) tiles[x][y] = 'X';
  }

  // chambers: floor, panels on the walls, a lift to the surface, a relic and crystals
  for (const h of holes) {
    const lx = h.sx + 1 - start;
    if (lx < 0 || lx >= CHUNK_W) continue;
    const floorRow = h.chamberY + h.ry;
    for (let dx = -h.rx - 1; dx <= h.rx + 1; dx++) { set(lx + dx, floorRow + 1, 'X'); set(lx + dx, floorRow + 2, 'X'); }
    for (let dx = -2; dx <= 2; dx++) set(lx + dx, floorRow, 'X');          // a flat spot for the lift
    set(lx, floorRow, 'B');
    set(lx - h.rx + 1, floorRow - 2, '#'); set(lx - h.rx + 1, floorRow - 3, '#');
    set(lx + h.rx - 1, floorRow - 2, '#'); set(lx + h.rx - 1, floorRow - 3, '#');
    for (const dx of [-h.rx, h.rx]) for (let k = 0; k < 4; k++) set(lx + dx, floorRow - 1 - k, '#');
    out.beams.push({ x: start + lx, y: floorRow, dir: -1 });
    out.relics.push({ id: `${index}:s${h.cell}`, x: (start + lx + (h.cell % 2 ? 4 : -4) + 0.5) * T, y: (floorRow - 1) * T + 6 });
    out.lamps.push([start + lx, h.chamberY - h.ry + 1]);
    out.chambers.push({ x: start + lx, y: h.chamberY, rx: h.rx, ry: h.ry });
    out.signs.push({ x: start + lx - 3, y: floorRow - 3, text: 'STEP IN THE BEAM' });
  }

  // structures
  for (const s of out.structures) {
    const { kind, x0, fy } = s;
    const wx = (x) => start + x;
    if (kind === 'park') {
      fill(x0 + 4, fy - 5, 2, 5, '#');                                       // panel posts to practise on
      set(x0 + 4, fy - 6, '#'); set(x0 + 5, fy - 6, '#');
      fill(x0 + 10, fy, 3, 1, 'J');                                          // bounce pad
      fill(x0 + 14, fy - 7, 5, 1, '#');                                      // shelf you can bounce onto
      out.relics.push({ id: `${index}:park`, x: (wx(x0 + 16) + 0.5) * T, y: (fy - 8) * T + 6 });
      fill(x0 + 21, fy - 8, 2, 8, '#');                                      // a tall wall for flinging
      out.cubes.push({ x: wx(x0 + 8), y: fy });
      out.signs.push({ x: wx(x0 + 1), y: fy - 3, text: 'PLAYGROUND' }, { x: wx(x0 + 8), y: fy - 2, text: 'SHOOT PANELS. FALL IN.' }, { x: wx(x0 + 11), y: fy - 4, text: 'BOUNCE UP' });
    } else if (kind === 'lab') {
      const w = 18, h = 8;
      fill(x0, fy - h, w, 1, '#');                                           // roof
      fill(x0, fy - h, 1, h - 3, '#'); fill(x0 + w - 1, fy - h, 1, h - 3, '#');   // walls with doorways at the bottom
      fill(x0 + 3, fy - 4, 5, 1, '#');                                       // an inner shelf
      fill(x0 + 11, fy, 3, 1, 'J');                                          // bounce pad under the high ledge
      fill(x0 + 10, fy - 6, 6, 1, '#');                                      // high ledge
      out.relics.push({ id: `${index}:lab${s.slot}`, x: (wx(x0 + 13) + 0.5) * T, y: (fy - 7) * T + 6 });
      out.cubes.push({ x: wx(x0 + 5), y: fy });
      out.lamps.push([wx(x0 + 4), fy - h + 1], [wx(x0 + 13), fy - h + 1]);
      out.signs.push({ x: wx(x0 + 1), y: fy - 2, text: 'TEST ROOM' });
    } else if (kind === 'tower') {
      fill(x0 + 5, fy - 18, 4, 18, '#');                                     // tower
      fill(x0 + 5, fy - 19, 9, 1, '#');                                      // platform
      set(x0 + 1, fy, 'B');
      out.beams.push({ x: wx(x0 + 1), y: fy, dir: -1 });
      out.relics.push({ id: `${index}:tower${s.slot}`, x: (wx(x0 + 10) + 0.5) * T, y: (fy - 20) * T + 6 });
      out.signs.push({ x: wx(x0 - 1), y: fy - 2, text: 'LIFT UP' });
    } else if (kind === 'bounce') {
      for (let k = 0; k < 4; k++) fill(x0 + 3 + k * 6, fy, 3, 1, 'J');
      fill(x0 + 5, fy - 9, 4, 1, '#'); fill(x0 + 17, fy - 12, 4, 1, '#');   // high panels to land on
      out.relics.push({ id: `${index}:bounce${s.slot}`, x: (wx(x0 + 19) + 0.5) * T, y: (fy - 13) * T + 6 });
      out.signs.push({ x: wx(x0 + 2), y: fy - 3, text: 'BOUNCE HILLS' });
    } else if (kind === 'speed') {
      fill(x0 + 2, fy, 20, 1, 'G');                                          // orange gel runway
      fill(x0 + 31, fy - 1, 8, 1, '#'); fill(x0 + 31, fy, 8, 1, '#');       // landing deck beyond the pit
      out.relics.push({ id: `${index}:speed${s.slot}`, x: (wx(x0 + 36) + 0.5) * T, y: (fy - 2) * T + 6 });
      out.signs.push({ x: wx(x0 + 1), y: fy - 3, text: 'RUN FAST. JUMP FAR.' });
    } else if (kind === 'fling') {
      // launch tower: ride the lift up, open a portal on the tower wall and one on the ground, then step off
      const tx = x0 + 3;
      fill(tx, fy - 20, 4, 20, '#');
      set(x0, fy, 'B'); out.beams.push({ x: wx(x0), y: fy, dir: -1 });
      // landing mesa across the ravine
      fill(x0 + 25, fy - 2, 13, 12, '#');
      out.relics.push({ id: `${index}:fling${s.slot}`, x: (wx(x0 + 31) + 0.5) * T, y: (fy - 3) * T + 6 });
      out.signs.push({ x: wx(x0 - 1), y: fy - 3, text: 'LAUNCH TOWER' }, { x: wx(tx + 5), y: fy - 3, text: 'PORTAL HIGH ON THE WALL. PORTAL HERE. STEP OFF.' });
    }
  }

  // surface dressing and cave decoration
  const spots = new Set(out.structures.flatMap((s) => Array.from({ length: s.x1 - s.x0 + 6 }, (_, i) => s.x0 - 3 + i)));
  for (let x = 2; x < CHUNK_W - 2; x++) {
    const ax = start + x, b = bio[x], y = surf[x];
    if (spots.has(x) || tiles[x][y - 1] === ':' || y > SEA - 1) continue;
    if (holes.some((h) => Math.abs(ax - (h.sx + 1)) < 4)) continue;
    const rr = hash(ax, 11, seed);
    if (b < 0.25 && rr > 0.62) out.props.push({ kind: 'reed', x: ax, y, size: 2 + Math.floor(hash(ax, 12, seed) * 3) });
    else if (b < 0.43 && rr > 0.93) out.props.push({ kind: 'tree', x: ax, y, size: 3 + Math.floor(hash(ax, 12, seed) * 3) });
    else if (b >= 0.43 && b < 0.6 && rr > 0.8) out.props.push({ kind: 'tree', x: ax, y, size: 3 + Math.floor(hash(ax, 12, seed) * 4) });
    else if (b >= 0.6 && b < 0.76 && rr > 0.9) out.props.push({ kind: 'cactus', x: ax, y, size: 2 + Math.floor(hash(ax, 12, seed) * 3) });
    else if (b >= 0.76 && rr > 0.88 && y > 18) out.props.push({ kind: 'pine', x: ax, y, size: 3 + Math.floor(hash(ax, 12, seed) * 4) });
    else if (rr > 0.5 && rr < 0.62 && b < 0.6) out.props.push({ kind: 'flower', x: ax, y, size: 1 });
    else if (rr > 0.45 && rr < 0.5) out.props.push({ kind: 'rock', x: ax, y, size: 1 + Math.floor(hash(ax, 13, seed) * 2) });
  }
  for (let x = 1; x < CHUNK_W - 1; x++) for (let y = surf[x] + 8; y < LAVA_Y - 1; y++) {
    if (tiles[x][y] !== ' ' || tiles[x][y + 1] !== 'X') continue;
    const rr = hash(start + x, y, seed + 5);
    if (rr > 0.965) out.crystals.push({ x: start + x, y, hue: Math.floor(hash(start + x, y, seed + 6) * 3) });
    else if (rr > 0.94 && y < 80) out.props.push({ kind: 'mushroom', x: start + x, y: y + 1, size: 1 + Math.floor(hash(start + x, y, seed + 7) * 2) });
  }

  return {
    index, start, tiles, surface: surf, biomes: bio, ...out,
    biome: bio[40], lake: surf.some((v) => v > SEA + 1),
  };
}

// ------------------------------------------------------------------ the world

export class PlaygroundWorld extends World {
  constructor(seed = Math.floor(Math.random() * 0x7fffffff)) {
    super(makeDef({ name: 'Playground', map: [' '], gun: GunMode.Both, blue: [0, 0], orange: [0, 0] }));
    this.playground = true;
    this.seed = seed;
    this.h = WORLD_H;
    this.chunks = new Map();
    this.entityChunks = new Map();     // chunk index -> cubes spawned for it
    this.wind = 0;                     // -1..1, drifts slowly
    this.gustT = 0;
    this.rain = 0;                     // 0..1, builds and fades
    this.rainPhase = 0;
    this.got = new Set();              // relic ids collected this visit
    this.relicCount = 0;
    this.flingBest = 0; this.flingNow = null; this.lastFling = 0; this.flingT = 0;
    this.respawn();
  }

  inBounds(x, y) { return y >= 0 && y < WORLD_H; }
  rayInBounds(x, y) { return y >= -1 && y <= WORLD_H; }

  chunk(index) {
    let c = this.chunks.get(index);
    if (!c) {
      c = generateChunk(index, this.seed);
      this.chunks.set(index, c);
      if (this.chunks.size > 28) this.chunks.delete(this.chunks.keys().next().value);
    }
    return c;
  }

  at(x, y) {
    if (y < 0) return ' ';
    if (y >= WORLD_H) return 'X';
    const index = Math.floor(x / CHUNK_W);
    return this.chunk(index).tiles[x - index * CHUNK_W][y];
  }

  isPortalable(x, y) { const c = this.at(x, y); return c === '#' || c === 'X'; }
  get daylight() { return clamp((Math.sin((this.time / DAY_SECONDS + 0.12) * Math.PI * 2) + 0.16) / 0.8, 0, 1); }
  get clock() { return (this.time / DAY_SECONDS * 24 + 8) % 24; }

  chunksFor(view) {
    const out = [];
    for (let i = Math.floor(view.x / (CHUNK_W * T)); i <= Math.floor((view.x + view.w) / (CHUNK_W * T)); i++) out.push(this.chunk(i));
    return out;
  }

  lampsFor(view) { return this.chunksFor(view).flatMap((c) => c.lamps); }

  /** Lights for things that glow: relics, crystals, lava. The renderer takes these as extra lights. */
  glowLights(view) {
    const out = [];
    for (const c of this.chunksFor({ x: view.x - 200, w: view.w + 400 })) {
      for (const rl of c.relics) if (!this.got.has(rl.id)) out.push({ x: rl.x, y: rl.y, r: 150, col: { r: 120, g: 230, b: 255, a: 170, css: 'rgba(120,230,255,0.67)' } });
      for (const cr of c.crystals) if (cr.x * T > view.x - 100 && cr.x * T < view.x + view.w + 100) out.push({ x: (cr.x + 0.5) * T, y: cr.y * T + 8, r: 90, col: CRYSTAL_LIGHT[cr.hue] });
    }
    // lava glows on the walls and ceilings around it
    const lavaTop = LAVA_Y * T;
    if (view.y + view.h > lavaTop - 400) for (let x = Math.floor(view.x / T); x <= Math.ceil((view.x + view.w) / T); x += 3) {
      for (let y = LAVA_Y - 1; y < LAVA_Y + 2; y++) if (this.at(x, y) === '~' && this.at(x, y - 1) !== '~') { out.push({ x: (x + 0.5) * T, y: y * T - 4, r: 130, col: { r: 255, g: 130, b: 40, a: 150, css: 'rgba(255,130,40,0.6)' } }); break; }
    }
    return out;
  }

  respawn() {
    this.dropCube(this.player || {}, false);
    const c = this.chunk(0);
    this.player = new Player(V(4.5 * T, c.surface[4] * T - 15));
    this.clearPlayerPortals();
    this.flingNow = null; this.lastTeleports = this.player.teleports;
  }

  /** Cubes and lift beams belong to chunks near the player or near an open portal; everything else is unloaded. */
  streamEntities() {
    const active = new Set([Math.floor(this.player.pos.x / (CHUNK_W * T))]);
    for (const p of this.portals) if (p) active.add(Math.floor(p.center.x / (CHUNK_W * T)));
    for (const i of [...active]) { active.add(i - 1); active.add(i + 1); }
    for (const [i, list] of this.entityChunks) if (!active.has(i)) {
      for (const c of list) if (c !== this.player.holding) { const k = this.cubes.indexOf(c); if (k >= 0) this.cubes.splice(k, 1); }
      this.entityChunks.delete(i);
    }
    for (const i of active) if (!this.entityChunks.has(i)) {
      const ch = this.chunk(i), list = ch.cubes.map((q) => new Cube(V((q.x + 0.5) * T, q.y * T - 14)));
      this.cubes.push(...list); this.entityChunks.set(i, list);
    }
    const alive = new Set([...this.entityChunks.values()].flat());
    this.cubes = this.cubes.filter((c) => c.held || alive.has(c));
    this.beams = [...active].flatMap((i) => this.chunk(i).beams.map((b) => ({ x: b.x, y: b.y, dir: b.dir, len: 0, active: false })));
  }

  updatePlayer(p, dt, input) {
    if (!p.dead && this.at(Math.floor(p.pos.x / T), Math.floor(p.pos.y / T)) === ':') {
      p.vel.x *= Math.exp(-dt * 2);
      p.vel.y = clamp(p.vel.y - World.Gravity * dt * (input.jumpHeld ? 1.65 : 0.94), -210, 150);
      super.updatePlayer(p, dt, { ...input, jumpPressed: false, jumpHeld: true });
    } else super.updatePlayer(p, dt, input);
  }

  /** Picks up relics the player touches. Returns the new relic count when one was collected. */
  collectRelics() {
    const p = this.player, ci = Math.floor(p.pos.x / (CHUNK_W * T));
    for (let i = ci - 1; i <= ci + 1; i++) for (const rl of this.chunk(i).relics) {
      if (this.got.has(rl.id) || Math.hypot(rl.x - p.pos.x, rl.y - p.pos.y) > 30) continue;
      this.got.add(rl.id); this.relicCount++;
      this.fx.burst(V(rl.x, rl.y), { r: 150, g: 240, b: 255, a: 255, css: '#96f0ff' }, 40, 360, 0.9, 3.5);
      this.emit(Sfx.Pickup, V(rl.x, rl.y), 4);
      this.emit(Sfx.Win, V(rl.x, rl.y), 0);
      this.relicEvent = { count: this.relicCount, t: this.time };
    }
  }

  /** The nearest relic you haven't taken, within a few chunks. */
  nearestRelic() {
    const p = this.player, ci = Math.floor(p.pos.x / (CHUNK_W * T));
    let best = null, bd = Infinity;
    for (let i = ci - 3; i <= ci + 3; i++) for (const rl of this.chunk(i).relics) {
      if (this.got.has(rl.id)) continue;
      const d = Math.hypot(rl.x - p.pos.x, rl.y - p.pos.y);
      if (d < bd) { bd = d; best = rl; }
    }
    return best ? { relic: best, dist: bd } : null;
  }

  /** Fling tracking: a flight that starts at a portal and ends on the ground is measured in tiles. */
  trackFling() {
    const p = this.player;
    if (p.teleports !== this.lastTeleports) {
      this.lastTeleports = p.teleports;
      this.flingNow = { x: p.pos.x, y: p.pos.y, t: this.time };
    }
    if (this.flingNow && p.onGround && this.time - this.flingNow.t > 0.25) {
      const dist = Math.hypot(p.pos.x - this.flingNow.x, p.pos.y - this.flingNow.y) / T;
      this.flingNow = null;
      if (dist >= 14) {
        this.lastFling = dist; this.flingT = this.time;
        if (dist > this.flingBest) this.flingBest = dist;
      }
    }
  }

  update(dt, input) {
    this.streamT = (this.streamT || 0) - dt;
    if (this.streamT <= 0) { this.streamEntities(); this.streamT = 0.25; }
    // weather: wind drifts, gusts kick in, rain builds and fades
    this.gustT -= dt;
    if (this.gustT <= 0) { this.gustT = 2 + Math.random() * 5; this.wind = (Math.random() - 0.5) * 2; }
    this.wind = approach(this.wind, 0, dt * 0.4);
    const storm = Math.sin(this.time * 0.01) > 0.94;
    this.rain = approach(this.rain, storm ? 1 : 0, dt * (storm ? 0.5 : 0.3));
    this.rainPhase += dt * (1 + this.rain);
    super.update(dt, input);
    if (!this.player.dead) { this.collectRelics(); this.trackFling(); }
    if (this.player.dead && this.player.deadTimer > 1) this.respawn();
  }
}

const CRYSTAL_LIGHT = [
  { r: 120, g: 160, b: 255, a: 110, css: 'rgba(120,160,255,0.43)' },
  { r: 200, g: 120, b: 255, a: 110, css: 'rgba(200,120,255,0.43)' },
  { r: 110, g: 255, b: 200, a: 110, css: 'rgba(110,255,200,0.43)' },
];
