// Progress export/import: a file made from one save merges into another, keeping the better time and medal. Run: node tests/progress.mjs
import assert from 'node:assert/strict';
import { SaveData } from '../src/maps.js';

const names = ['A', 'B', 'C'];
const a = SaveData.load(names), b = SaveData.load(names);
a.times = { A: 10, B: 30 }; a.medals = { A: 3 }; a.ghosts = { A: '1,2,3' }; a.totalDeaths = 5; a.master = 0.3; a.fightBest = 2;
b.times = { A: 20, C: 40 }; b.medals = { A: 1, C: 2 }; b.totalDeaths = 9; b.fightBest = 1;
const file = JSON.parse(JSON.stringify(a.toFile()));
assert.equal(file.format, 'portal2d-progress');
const r = b.mergeFile(file);
assert.deepEqual(b.times, { A: 10, B: 30, C: 40 });          // faster A and new B come in, C stays
assert.deepEqual(b.medals, { A: 3, C: 2 });
assert.equal(b.ghosts.A, '1,2,3');                             // the ghost of the faster run comes with it
assert.equal(b.totalDeaths, 9);                                // counters keep the larger value
assert.equal(b.fightBest, 2);
assert.equal(b.master, 0.3);                                   // settings come from the file
assert.deepEqual(b.best, [10, 30, 40]);
assert.equal(r.chambers, 1);
assert.equal(b.mergeFile({ nope: true }), null);               // not a progress file
console.log('progress export/import ok');
