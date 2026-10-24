// Progression, airlock safety, save migration and obvious puzzle bypasses.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { World, makeDef, stepFrame, emptyInput } from '../src/world.js';
import { SaveData, CustomMap } from '../src/maps.js';

const read = path => JSON.parse(readFileSync(new URL(path, import.meta.url)));
const { chambers } = read('../data/levels.json');
const { pools } = read('../data/random.json');
const classic = read('../tools/LevelForge/classic.json').chambers;
assert.equal(chambers.at(-1).name, 'Final Exam');
assert.equal(chambers.at(-1).label, '100');
assert.equal(chambers.filter(d => d.tutorial).length, 5, 'only introductory lessons reveal hints automatically');
assert.equal(new Set(chambers.map(d => d.recordKey)).size, 100);
chambers.forEach((d, i) => {
  assert.equal(d.label, String(i + 1).padStart(2, '0'));
  if (i) assert.ok(d.difficulty >= chambers[i - 1].difficulty);
});
for (const { level } of classic) {
  const current = chambers.find(d => d.name === level.name);
  assert.deepEqual(current.map, level.map, `preserve original geometry: ${level.name}`);
  assert.equal(current.recordKey, level.name, 'preserve original best-time keys after reordering');
}
for (const [tier, pool] of pools.entries()) {
  for (const d of pool) {
    assert.equal(d.mechanics.length, tier < 5 ? 2 : tier < 10 ? 3 : 4);
    assert.equal(new Set(d.mechanics).size, d.mechanics.length, 'no repeated puzzle inside a random test');
    if (tier >= 5) assert.ok(d.exitGroups.length >= 2, 'advanced tests require multiple persistent circuits');
  }
}

let locks = 0;
for (const raw of [...chambers, ...pools.flat()]) {
  const w = new World(makeDef(raw)), { x, y } = w.exit;
  assert.equal(raw.map[y - 1][x], ' ', `exit headroom: ${raw.name}`);
  assert.ok(w.isSolid(x, y + 1), `exit floor: ${raw.name}`);
  for (const group of raw.exitGroups) {
    w.groupActive.fill(true);
    w.groupActive[group] = false;
    w.player.pos = { x: x * 32 + 16, y: (y + 1) * 32 - 16 };
    w.player.vel = { x: 0, y: 0 };
    w.updatePlayer(w.player, 1 / 120, emptyInput());
    assert.equal(w.player.won, false, `${raw.name}: missing circuit ${group} must block the exit`);
    locks++;
  }
  w.groupActive.fill(true);
  w.player.pos = { x: x * 32 + 16, y: (y + 1) * 32 - 16 };
  w.player.vel = { x: 0, y: 0 };
  w.updatePlayer(w.player, 1 / 120, emptyInput());
  assert.equal(w.player.won, true, `powered exit must open: ${raw.name}`);
}
assert.equal(new World(CustomMap.blank('Compatibility').toDef()).exitReady, true, 'custom maps need no campaign circuit metadata');

let stored = JSON.stringify({ times: Object.fromEntries(classic.map(({ level }, i) => [level.name, 10 + i])) });
globalThis.localStorage = { getItem: () => stored, setItem: (_, value) => { stored = value; } };
const save = SaveData.load(chambers.map(d => d.recordKey));
chambers.forEach((d, i) => assert.equal(save.best[i], d.recordKey === d.name ? 10 + classic.findIndex(c => c.level.name === d.name) : 0));
save.store();
assert.deepEqual(SaveData.load(chambers.map(d => d.recordKey)).best, save.best);
delete globalThis.localStorage;

// A reference win alone would not detect an open corridor that skips all interactions.
let shortcuts = 0;
for (const raw of [...chambers.filter(d => d.recordKey !== d.name), ...pools.flat()]) {
  for (const direction of [-1, 1]) for (const hop of [false, true]) {
    const w = new World(makeDef(raw));
    for (let f = 0; f < 3600 && !w.player.dead && !w.player.won; f++) {
      stepFrame(w, { ...emptyInput(), moveX: direction, jumpPressed: hop && w.player.onGround, jumpHeld: hop });
    }
    assert.equal(w.player.won, false, `${raw.name}: running/hopping ${direction} bypasses the puzzle`);
    shortcuts++;
  }
}
console.log(`Progression, classic saves, ${locks} airlock checks, 640 exit clearances and ${shortcuts} shortcut attempts pass`);
