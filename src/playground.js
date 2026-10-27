// Endless horizontal terrain. Chunks are deterministic, so distant portal destinations never need to stay resident.
import { K, V, hash, clamp, lerp } from './core.js';
import { World, Player, Cube, GunMode, makeDef } from './world.js';

export const CHUNK_W = 80;
export const WORLD_H = 96;
export const DAY_SECONDS = 240;
const T = K.Tile;

export function generateChunk(index, seed) {
  const start = index * CHUNK_W;
  const r = (n) => hash(index, n, seed);
  const base = 23 + Math.floor(r(1) * 4);
  const roomY = 44 + Math.floor(r(2) * 5);
  const houseW = 10 + Math.floor(r(3) * 6), houseH = 5 + Math.floor(r(4) * 4);
  const lake = index !== 0 && r(5) > 0.28;
  const surface = [], tiles = [], lamps = [], trees = [];
  for (let x = 0; x < CHUNK_W; x++) {
    const ax = start + x;
    const hill = 25 + Math.floor(Math.sin(ax * 0.035 + seed) * 2 + Math.sin(ax * 0.013) * 3);
    const blend = Math.min(1, Math.min(x, CHUNK_W - 1 - x) / 8);
    let top = Math.round(lerp(hill, base + Math.floor(Math.sin(x * 0.13) * 1.4), blend));
    if (x >= 6 && x <= 38) top = base;
    if (lake && x >= 48 && x <= 68) top = base + Math.min(6, x - 47, 69 - x);
    surface[x] = top;
    tiles[x] = Array.from({ length: WORLD_H }, (_, y) => y >= top ? 'X' : lake && x >= 48 && x <= 68 && y >= base + 1 ? ':' : ' ');
  }
  const fill = (x, y, w, h, c) => {
    for (let i = x; i < x + w; i++) for (let j = y; j < y + h; j++) tiles[i][j] = c;
  };
  // Open doorways at both ends keep the buildings part of the landscape, not barriers.
  fill(8, base - houseH, houseW, houseH + 1, '#');
  fill(9, base - houseH + 1, houseW - 2, houseH - 1, ' ');
  fill(8, base - 3, 1, 3, ' '); fill(7 + houseW, base - 3, 1, 3, ' ');
  lamps.push([11, base - houseH + 1]);
  // A drop shaft and a separate lift lane connect the surface to an abandoned test room.
  fill(29, base, 7, roomY - base + 14, '#');
  fill(30, base, 5, roomY - base + 13, ' ');
  fill(6, roomY, 40, 15, '#');
  fill(7, roomY + 1, 38, 13, ' ');
  fill(30, roomY, 5, 1, ' ');
  fill(30, roomY + 14, 5, 1, '#');
  tiles[34][roomY + 14] = 'B';
  for (let x = 10; x < 44; x += 11) lamps.push([x, roomY + 1]);
  const variant = Math.floor(r(6) * 3);
  if (variant === 0) { fill(12, roomY + 14, 4, 1, 'J'); fill(22, roomY + 10, 5, 1, '#'); }
  if (variant === 1) { fill(12, roomY + 14, 10, 1, 'G'); fill(23, roomY + 14, 2, 1, 'Q'); }
  if (variant === 2) { fill(18, roomY + 8, 2, 6, '#'); fill(23, roomY + 5, 5, 1, '#'); }
  // Lower caverns meet at chunk edges; a second opening connects them to the test rooms.
  for (let x = 0; x < CHUNK_W; x++) {
    const roof = 72 + Math.floor(Math.sin((start + x) * 0.09) * 3);
    fill(x, roof, 1, 7 + Math.floor(hash(start + x, 9, seed) * 3), ' ');
  }
  fill(41, roomY + 14, 3, 77 - roomY - 14, ' ');
  for (let x = 41; x < 44; x++) tiles[x][77] = 'J';
  for (let x = 2; x < CHUNK_W - 2; x += 5) {
    if (x >= 5 && x <= 38 || lake && x >= 46 && x <= 70) continue;
    if (r(20 + x) > 0.3) trees.push({ x: start + x, y: surface[x], size: 3 + Math.floor(r(80 + x) * 3) });
  }
  return { index, start, tiles, surface, lamps: lamps.map(([x, y]) => [start + x, y]), trees, base, roomY, houseW, houseH, variant, lake };
}

export class PlaygroundWorld extends World {
  constructor(seed = Math.floor(Math.random() * 0x7fffffff)) {
    super(makeDef({ name: 'Playground', map: [' '], gun: GunMode.Both, blue: [0, 0], orange: [0, 0] }));
    this.playground = true;
    this.seed = seed;
    this.h = WORLD_H;
    this.chunks = new Map();
    this.entityChunks = new Map();
    this.respawn();
  }

  inBounds(x, y) { return y >= 0 && y < WORLD_H; }
  rayInBounds(x, y) { return y >= -1 && y <= WORLD_H; }

  chunk(index) {
    let c = this.chunks.get(index);
    if (!c) {
      c = generateChunk(index, this.seed);
      this.chunks.set(index, c);
      if (this.chunks.size > 24) this.chunks.delete(this.chunks.keys().next().value);
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

  respawn() {
    this.dropCube(this.player || {}, false);
    const c = this.chunk(0);
    this.player = new Player(V(4.5 * T, c.surface[4] * T - 15));
    this.clearPlayerPortals();
  }

  streamEntities() {
    const active = new Set([Math.floor(this.player.pos.x / (CHUNK_W * T))]);
    for (const p of this.portals) if (p) active.add(Math.floor(p.center.x / (CHUNK_W * T)));
    for (const i of [...active]) { active.add(i - 1); active.add(i + 1); }
    for (const i of this.entityChunks.keys()) if (!active.has(i)) {
      const c = this.entityChunks.get(i);
      if (c !== this.player.holding) this.cubes.splice(this.cubes.indexOf(c), 1);
      this.entityChunks.delete(i);
    }
    for (const i of active) if (!this.entityChunks.has(i)) {
      const ch = this.chunk(i), cube = new Cube(V((ch.start + 11.5) * T, (ch.roomY + 14) * T - 14));
      this.cubes.push(cube); this.entityChunks.set(i, cube);
    }
    this.cubes = this.cubes.filter((c) => c.held || [...this.entityChunks.values()].includes(c));
    this.beams = [...active].map((i) => ({ x: i * CHUNK_W + 34, y: this.chunk(i).roomY + 14, dir: -1, len: 0, active: false }));
  }

  updatePlayer(p, dt, input) {
    if (!p.dead && this.at(Math.floor(p.pos.x / T), Math.floor(p.pos.y / T)) === ':') {
      p.vel.x *= Math.exp(-dt * 2);
      p.vel.y = clamp(p.vel.y - World.Gravity * dt * (input.jumpHeld ? 1.65 : 0.94), -210, 150);
      super.updatePlayer(p, dt, { ...input, jumpPressed: false, jumpHeld: true });
    } else super.updatePlayer(p, dt, input);
  }

  update(dt, input) {
    this.streamT = (this.streamT || 0) - dt;
    if (this.streamT <= 0) { this.streamEntities(); this.streamT = 0.25; }
    super.update(dt, input);
    if (this.player.dead && this.player.deadTimer > 1) this.respawn();
  }
}
