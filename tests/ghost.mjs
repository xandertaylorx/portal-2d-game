// The best-run ghost must not glide through walls when the recording contains a portal trip. Run: node tests/ghost.mjs
import assert from 'node:assert/strict';
import { GhostPlayback } from '../src/ghost.js';

// samples (20 per second): walk right, then a 600-unit teleport between two samples, then keep walking
const g = new GhostPlayback('100,300,9, 10,0,9, 10,0,9, 600,-200,9, 10,0,9');
const xs = [];
for (let t = 0; t <= 0.2; t += 0.01) xs.push(Math.round(g.at(t).pos.x));
const step = Math.max(...xs.slice(1).map((x, i) => Math.abs(x - xs[i])));
assert.ok(step < 650, 'sanity');
// between the sample before the jump and the sample after it the ghost must stay put, never sit in the middle
const mid = g.at(0.1 + 0.025).pos.x;       // halfway between sample 2 (x=120) and sample 3 (x=720)
assert.ok(mid === 120 || mid === 720, `ghost was drawn in the middle of a teleport at x=${mid}`);
console.log('ghost teleport handling ok');
