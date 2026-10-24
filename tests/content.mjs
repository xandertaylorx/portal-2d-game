// Content contract and selection behavior, independent of rendering and localStorage.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { pickRandomTest } from '../src/random.js';
import { makeDef } from '../src/world.js';

const read = (path) => JSON.parse(readFileSync(new URL(path, import.meta.url)));
const { chambers } = read('../data/levels.json');
const { pools } = read('../data/random.json');
assert.equal(chambers.length, 100);
assert.equal(new Set(chambers.map(d => d.name)).size, 100, 'best-time save keys must be unique');
assert.equal(new Set(chambers.map(d => JSON.stringify(d.map))).size, 100);
assert.equal(pools.length, 15);
const layouts = new Set();
for (const [tier, raw] of pools.entries()) {
  assert.equal(raw.length, 36);
  assert.ok(new Set(raw.map(d => d.family)).size >= 13, `tier ${tier} needs spatial and mechanic variety`);
  for (const d of raw) {
    assert.ok(d.layoutId && !layouts.has(d.layoutId), 'random tiers must not recycle identical layouts');
    layouts.add(d.layoutId);
  }
  const pool = raw.map(makeDef), used = new Set(), recent = [];
  let state = tier + 1;
  const rng = () => ((state = (Math.imul(state, 1664525) + 1013904223) >>> 0) / 2 ** 32);
  for (let cycle = 0; cycle < 3; cycle++) {
    const cycleLayouts = new Set();
    for (let i = 0; i < pool.length; i++) {
      const fresh = pool.filter(d => !used.has(d.layoutId));
      const alternatives = fresh.filter(d => !recent.includes(d.family));
      const before = [...recent];
      const d = pickRandomTest(pool, used, recent, rng);
      assert.ok(!cycleLayouts.has(d.layoutId), 'no map repeats before a tier is exhausted');
      if (alternatives.length) assert.ok(!before.includes(d.family), 'prefer a family absent from the last three tests');
      cycleLayouts.add(d.layoutId);
    }
  }
}
for (const d of [...chambers, ...pools.flat()]) {
  assert.ok(d.map.length >= 10 && d.map.every(row => row.length === d.map[0].length), `rectangular map: ${d.name}`);
  assert.equal(d.map.join('').split('P').length - 1, 1, `one spawn: ${d.name}`);
  assert.equal(d.map.join('').split('E').length - 1, 1, `one exit: ${d.name}`);
}
console.log('100 unique chambers, 540 unique random layouts, and three full selection cycles per tier pass');
