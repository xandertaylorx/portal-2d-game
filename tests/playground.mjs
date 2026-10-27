// Run: node tests/playground.mjs
import assert from 'node:assert/strict';
import { K, V, C } from '../src/core.js';
import { emptyInput } from '../src/world.js';
import { PlaygroundWorld, generateChunk, CHUNK_W, WORLD_H, DAY_SECONDS } from '../src/playground.js';
const T = K.Tile;
const advance = (w, seconds, input = emptyInput()) => {
  for (let n = 0; n < Math.round(seconds / K.SimDt); n++) { w.update(K.SimDt, input); w.events.length = 0; }
};

for (const index of [-10000, -2, -1, 0, 1, 2, 10000]) {
  const a = generateChunk(index, 1234), b = generateChunk(index, 1234);
  assert.deepEqual(a, b, 'terrain must regenerate identically');
  assert.equal(a.tiles.length, CHUNK_W);
  assert(a.tiles.every((col) => col.length === WORLD_H));
  assert.equal(a.tiles[30][a.base], ' ', 'drop shaft must be open');
  assert.equal(a.tiles[34][a.roomY + 14], 'B');
  assert.equal(a.tiles[41][a.roomY + 14], ' ', 'lower cavern entrance must be open');
  const next = generateChunk(index + 1, 1234);
  assert(Math.abs(a.surface[CHUNK_W - 1] - next.surface[0]) <= 1, 'terrain must meet smoothly at seams');
  for (let y = 73; y <= 82; y++) {
    if (a.tiles[CHUNK_W - 1][y] === ' ' && next.tiles[0][y] === ' ') break;
    assert(y < 82, 'lower caverns must join at seams');
  }
}
assert.notDeepEqual(generateChunk(0, 1).tiles, generateChunk(0, 2).tiles);
const w = new PlaygroundWorld(1234);
assert(!w.overlapsSolid(w.player, w.player.pos), 'spawn is clear');
advance(w, 2);
assert(w.player.onGround && !w.player.dead);
for (let i = -50; i < 50; i++) {
  const ch = w.chunk(i);
  assert.equal(w.at(i * CHUNK_W - 1, 90), w.chunk(i - 1).tiles[79][90]);
  assert.equal(w.at(ch.start + 8, ch.base - ch.houseH), '#');
}
assert(w.chunks.size <= 24, 'terrain cache must be bounded');
assert.deepEqual(w.chunk(0), generateChunk(0, 1234));

// Natural ground, including negative coordinates and faraway unloaded regions, accepts both portals.
function floorPortal(world, index, color) {
  const ch = world.chunk(index), x = ch.start + 36;
  assert.equal(world.at(x, ch.base), 'X');
  const p = world.buildPortal(color, V(x, ch.base), V(0, -1), V((x + 0.75) * T, ch.base * T));
  assert(p, `portal must fit natural ground in chunk ${index}`);
  p.open = 1;
  return p;
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
assert(w.chunks.size <= 24);
assert(w.isPortalable(exit.tileA.x, exit.tileA.y), 'eviction must not destroy remote portal terrain');

// The shared ray caster must work outside the finite chamber coordinate range.
const far = w.chunk(-1000), tx = far.start + 36;
const shot = w.previewShot(V((tx + 0.7) * T, far.base * T - 120), V(0, 1), 0);
assert(shot.hit.hit && shot.candidate);
let seam = false;
for (let index = -40; index < 40 && !seam; index++) {
  const a = w.chunk(index), b = w.chunk(index + 1), x = b.start;
  if (a.surface[79] !== b.surface[0]) continue;
  const p = w.buildPortal(0, V(x, b.surface[0]), V(0, -1), V((x + 0.1) * T, b.surface[0] * T));
  if (p && p.tileA.x === x - 1 && p.tileB.x === x) seam = true;
}
assert(seam, 'portals must span chunk seams');

const water = new PlaygroundWorld(1234);
let lake;
for (let i = 1; i < 20; i++) if (water.chunk(i).lake) { lake = water.chunk(i); break; }
assert(lake);
water.player.pos = V((lake.start + 58.5) * T, (lake.base + 4) * T);
advance(water, 2);
assert(!water.player.dead, 'blue water must not kill the player');
const bottom = water.player.pos.y;
advance(water, 1, { ...emptyInput(), jumpHeld: true });
assert(water.player.pos.y < bottom - T, 'holding jump must swim upward');

const lift = new PlaygroundWorld(1234), room = lift.chunk(0);
lift.player.pos = V(34.5 * T, (room.roomY + 13) * T);
advance(lift, 5);
assert(lift.player.pos.y < room.base * T, 'lift must carry the player back to the surface');
for (let i = -100; i < 100; i++) {
  const ch = lift.chunk(i);
  lift.player.pos = V((ch.start + 4.5) * T, ch.surface[4] * T - 15);
  lift.streamEntities();
  assert(lift.cubes.length <= 3, 'unvisited entity chunks must be unloaded');
}
lift.kill(lift.player, C(255, 255, 255));
advance(lift, 1.2);
assert(!lift.player.dead && !lift.overlapsSolid(lift.player, lift.player.pos));
assert.equal(lift.player.pos.x, 4.5 * T);
lift.portals = [floorPortal(lift, 0, 0), floorPortal(lift, 5, 1)];
lift.respawn();
assert(!lift.linked && lift.portals.every((p) => p === null));
const day = new PlaygroundWorld(1);
assert(day.daylight > 0.9);
day.time = DAY_SECONDS / 2;
assert.equal(day.daylight, 0);
day.time = DAY_SECONDS;
assert(day.daylight > 0.9);
console.log('playground: deterministic streaming, seams, natural portals, distant teleport, swimming, lifts, respawn, and day/night pass');
