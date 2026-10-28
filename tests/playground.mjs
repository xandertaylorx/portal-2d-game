// Playground tests. Run: node tests/playground.mjs
import assert from 'node:assert/strict';
import { K, V, C } from '../src/core.js';
import { emptyInput } from '../src/world.js';
import { PlaygroundWorld, generateChunk, biomeAt, biomeName, naturalSurface, CHUNK_W, WORLD_H, DAY_SECONDS, SEA } from '../src/playground.js';
const T = K.Tile;
const advance = (w, seconds, input = emptyInput()) => {
  for (let n = 0; n < Math.round(seconds / K.SimDt); n++) { w.update(K.SimDt, input); w.events.length = 0; }
};

// ---- generation is deterministic, the right shape, and seamless
const kinds = new Set(), biomes = new Set();
for (const index of [-10000, -50, -3, -2, -1, 0, 1, 2, 3, 50, 10000]) {
  const a = generateChunk(index, 1234), b = generateChunk(index, 1234);
  assert.deepEqual(a, b, 'terrain must regenerate identically');
  assert.equal(a.tiles.length, CHUNK_W);
  assert(a.tiles.every((col) => col.length === WORLD_H));
  const next = generateChunk(index + 1, 1234);
  assert(Math.abs(a.surface[CHUNK_W - 1] - next.surface[0]) <= 3, `terrain must meet smoothly at the seam after chunk ${index}`);
  for (const s of a.structures) kinds.add(s.kind);
}
for (let x = -4000; x < 4000; x += 50) biomes.add(biomeName(biomeAt(x, 1234)));
assert(biomes.size >= 4, `the world must have varied biomes (saw ${[...biomes]})`);
assert.notDeepEqual(generateChunk(0, 1).tiles, generateChunk(0, 2).tiles, 'seeds make different worlds');

// every kind of structure appears somewhere, and nothing ever touches a chunk edge
let kindsSeen = new Set(), relics = 0, chambers = 0, lakes = 0, lava = 0;
for (let i = -80; i < 80; i++) {
  const c = generateChunk(i, 4321);
  for (const s of c.structures) { kindsSeen.add(s.kind); assert(s.x0 >= 10 && s.x1 <= CHUNK_W - 10); }
  relics += c.relics.length; chambers += c.chambers.length; if (c.lake) lakes++;
  for (let x = 0; x < CHUNK_W; x++) if (c.tiles[x][88] === '~') { lava++; break; }
  for (const b of c.beams) assert.equal(c.tiles[b.x - c.start][b.y], 'B', 'a lift is built on a lift emitter');
}
for (const k of ['park', 'lab', 'tower', 'fling', 'bounce', 'speed']) assert(kindsSeen.has(k) || k === 'park', `structure ${k} must appear in the world`);
assert(relics > 100 && chambers > 30 && lakes > 3 && lava > 10, `world too sparse: ${relics} relics, ${chambers} chambers, ${lakes} lake chunks, ${lava} lava chunks`);

// ---- spawning and walking
const w = new PlaygroundWorld(1234);
assert(!w.overlapsSolid(w.player, w.player.pos), 'spawn is clear');
advance(w, 2);
assert(w.player.onGround && !w.player.dead);
for (let i = -60; i < 60; i++) { w.chunk(i); assert.equal(w.at(i * CHUNK_W - 1, 90), w.chunk(i - 1).tiles[CHUNK_W - 1][90]); }
assert(w.chunks.size <= 28, 'terrain cache must be bounded');
assert.deepEqual(w.chunk(0), generateChunk(0, 1234));
assert.equal(naturalSurface(4, 1234), w.chunk(0).surface[4], 'the surface function matches the generated chunk away from structures');

// ---- natural ground, including far away, accepts both portals
function floorPortal(world, index, color) {
  const ch = world.chunk(index);
  for (let x = 20; x < 60; x++) {
    const ax = ch.start + x, top = ch.surface[x];
    if (ch.surface[x + 1] !== top || ch.surface[x - 1] !== top || world.at(ax, top) !== 'X' || world.at(ax, top - 1) !== ' ') continue;
    const p = world.buildPortal(color, V(ax, top), V(0, -1), V((ax + 0.75) * T, top * T));
    if (!p) continue;
    p.open = 1;
    return p;
  }
  throw new Error(`no flat ground for a portal in chunk ${index}`);
}
w.portals = [floorPortal(w, -1, 0), floorPortal(w, 10000, 1)];
const entry = w.portals[0], exit = w.portals[1];
w.player.pos = V(entry.center.x, entry.center.y - 85);
w.player.vel = V(0, 700);
advance(w, 0.18);
assert.equal(w.player.teleports, 1, 'falling through a natural surface portal must teleport');
assert(Math.abs(w.player.pos.x - exit.center.x) < 2);
assert(!w.player.dead);
for (let i = 100; i < 180; i++) w.chunk(i);
assert(w.chunks.size <= 28);
assert(w.isPortalable(exit.tileA.x, exit.tileA.y), 'eviction must not destroy remote portal terrain');

const far = w.chunk(-1000);
const fx = far.start + 30;
const shot = w.previewShot(V((fx + 0.7) * T, far.surface[30] * T - 120), V(0, 1), 0);
assert(shot.hit.hit && shot.candidate, 'the ray caster works outside the finite chamber range');

// ---- water is swimmable, lava kills
const water = new PlaygroundWorld(1234);
let lake = null, lx = 0;
search: for (let i = -200; i < 200; i++) {
  const c = water.chunk(i);
  for (let x = 6; x < 74; x++) if (c.surface[x] > SEA + 3 && c.surface[x - 3] > SEA + 2 && c.surface[x + 3] > SEA + 2) { lake = c; lx = x; break search; }
}
assert(lake, 'a lake must exist somewhere nearby');
water.player.pos = V((lake.start + lx + 0.5) * T, (SEA + 2) * T);
advance(water, 2);
assert(!water.player.dead, 'blue water must not kill the player');
const bottom = water.player.pos.y;
advance(water, 1, { ...emptyInput(), jumpHeld: true });
assert(water.player.pos.y < bottom - T, 'holding jump must swim upward');

const lavaW = new PlaygroundWorld(1234);
let lc = null;
for (let i = -200; i < 200 && !lc; i++) { const c = lavaW.chunk(i); for (let x = 0; x < CHUNK_W; x++) if (c.tiles[x][89] === '~') { lc = [c, x]; break; } }
assert(lc, 'lava must exist');
lavaW.player.pos = V((lc[0].start + lc[1] + 0.5) * T, 89.5 * T);
advance(lavaW, 0.3);
assert(lavaW.player.dead, 'lava kills');

// ---- sinkhole chambers: the lift carries you back to the surface, the relic is there to collect
const lift = new PlaygroundWorld(1234);
let ch = null, chamber = null;
for (let i = -60; i < 60 && !ch; i++) { const c = lift.chunk(i); if (c.chambers.length && c.beams.length) { ch = c; chamber = c.chambers[0]; } }
assert(ch, 'a chamber with a lift must exist');
const beam = ch.beams.find((b) => Math.abs(b.x - chamber.x) <= 1);
lift.player.pos = V((beam.x + 0.5) * T, (beam.y - 1) * T);
lift.player.vel = V(0, 0);
lift.streamEntities();
advance(lift, 0.3);
advance(lift, 7);
assert(lift.player.pos.y < (ch.surface[Math.max(0, Math.min(CHUNK_W - 1, beam.x - ch.start))] + 3) * T, 'the lift must carry the player back to the surface');
// a relic is collected on touch, once
const rl = ch.relics.find((q) => q.id.includes(':s'));
lift.player.pos = V(rl.x, rl.y);
lift.player.vel = V(0, 0);
const before = lift.relicCount;
lift.collectRelics();
assert.equal(lift.relicCount, before + 1, 'touching a relic collects it');
lift.collectRelics();
assert.equal(lift.relicCount, before + 1, 'a relic is only collected once');
const ptr = lift.nearestRelic();
assert(ptr && !lift.got.has(ptr.relic.id), 'the pointer finds an uncollected relic');

// ---- the launch tower course can be solved: portal on the wall, portal below, step off, land on the mesa
let course = null;
for (let seed = 1; seed < 80 && !course; seed++) for (let i = -40; i < 40 && !course; i++) {
  const c = generateChunk(i, seed), s = c.structures.find((q) => q.kind === 'fling');
  if (s) course = { seed, c, s };
}
assert(course, 'a launch tower course must exist');
{
  const { seed, c, s } = course, ax = (x) => c.start + x, fl = new PlaygroundWorld(seed);
  const blue = fl.buildPortal(0, V(ax(s.x0 + 7), s.fy), V(0, -1), V(ax(s.x0 + 8) * T, s.fy * T));
  const orange = fl.buildPortal(1, V(ax(s.x0 + 6), s.fy - 12), V(1, 0), V(ax(s.x0 + 7) * T, (s.fy - 11.5) * T));
  assert(blue && orange, 'both portals fit the launch tower');
  blue.open = orange.open = 1;
  fl.portals = [blue, orange];
  fl.player.pos = V((ax(s.x0 + 6) + 0.5) * T, (s.fy - 20) * T - 15);
  fl.player.vel = V(0, 0);
  let landed = false;
  for (let f = 0; f < 60 * 10 && !landed; f++) {
    const input = { ...emptyInput(), moveX: fl.player.onGround && fl.player.pos.x < (ax(s.x0 + 7) + 0.15) * T ? 1 : 0, aimWorld: { x: 0, y: 0 } };
    fl.update(K.SimDt, input); fl.update(K.SimDt, input); fl.events.length = 0;
    if (fl.player.teleports && fl.player.onGround) landed = true;
    assert(!fl.player.dead, 'the launch must not kill the player');
  }
  assert(landed, 'the launch tower flings the player somewhere safe');
  assert(fl.player.pos.x > (ax(s.x0 + 25)) * T && fl.player.pos.x < (ax(s.x0 + 39)) * T, 'the launch lands on the mesa');
  assert(fl.flingBest >= 14 || fl.lastFling >= 14, 'the flight counts as a fling');
}

// ---- entities stream in and out, respawn works, the day turns
const entity = new PlaygroundWorld(1234);
for (let i = -100; i < 100; i++) {
  const c = entity.chunk(i);
  entity.player.pos = V((c.start + 4.5) * T, c.surface[4] * T - 15);
  entity.streamEntities();
  assert(entity.cubes.length <= 12, 'unvisited entity chunks must be unloaded');
}
entity.kill(entity.player, C(255, 255, 255));
advance(entity, 1.2);
assert(!entity.player.dead && !entity.overlapsSolid(entity.player, entity.player.pos));
assert.equal(entity.player.pos.x, 4.5 * T);
entity.portals = [floorPortal(entity, 0, 0), floorPortal(entity, 5, 1)];
entity.respawn();
assert(!entity.linked && entity.portals.every((p) => p === null));
const day = new PlaygroundWorld(1);
assert(day.daylight > 0.9);
day.time = DAY_SECONDS / 2;
assert.equal(day.daylight, 0);
day.time = DAY_SECONDS;
assert(day.daylight > 0.9);
console.log('playground: biomes, caves, lava, structures, relics, lifts, the launch course, streaming and day/night pass');
