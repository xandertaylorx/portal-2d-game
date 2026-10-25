// Replays the C# reference solutions (exported by `tools/LevelForge`) through the JS world and checks every
// chamber is still beaten, in about the same number of frames. Run: node tests/replay.mjs
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import { World, makeDef, stepFrame } from '../src/world.js';
import { replay } from '../tools/bot.mjs';

const here = new URL('.', import.meta.url);
const levels = JSON.parse(readFileSync(new URL('../data/levels.json', here)));
const random = JSON.parse(readFileSync(new URL('../data/random.json', here)));
const traces = JSON.parse(readFileSync(new URL('../test/traces.json', here)));

function* inputs(s) {
  for (const part of s.split(';')) {
    if (!part) continue;
    const [tok, n] = part.split('x');
    const [m, f, ax, ay] = tok.split(',');
    const fl = +f;
    for (let i = 0; i < +n; i++) yield {
      moveX: +m, jumpPressed: !!(fl & 1), jumpHeld: !!(fl & 2), usePressed: !!(fl & 4), fireBlue: !!(fl & 8),
      fireOrange: !!(fl & 16), fireCycle: !!(fl & 32), aimWorld: { x: +ax, y: +ay },
    };
  }
}

// A short or stale traces file must never silently certify a larger content export.
const campaignTraces = traces.filter(t => !t.random);
assert.deepEqual(campaignTraces.map(t => t.name).sort(), levels.chambers.map(d => d.name).sort());
const randomTraces = traces.filter(t => t.random).map(t => t.random.join(':'));
const expectedRandom = random.pools.flatMap((pool, d) => pool.map((_, i) => `${d}:${i}`));
assert.deepEqual(randomTraces.sort(), expectedRandom.sort());
assert.ok(traces.every(t => t.trace.won), 'exporter must prove every reference solution wins');

let bad = 0;
for (const t of traces) {
  const raw = t.random ? random.pools[t.random[0]][t.random[1]] : levels.chambers.find((c) => c.name === t.name);
  const w = new World(makeDef(raw));
  let frames = 0, last = null;
  for (const inp of inputs(t.trace.inputs)) {
    last = inp;
    stepFrame(w, inp);
    frames++;
    if (w.player.won || w.player.dead) break;
  }
  // Float32 C# vs double JS can put an exit overlap exactly on a tile edge.
  // Allow the existing two-frame drift tolerance at the end, continuing movement without new actions.
  for (let i = 0; i < 2 && !w.player.won && !w.player.dead && last; i++) {
    stepFrame(w, { ...last, jumpPressed: false, usePressed: false, fireBlue: false, fireOrange: false, fireCycle: false });
    frames++;
  }
  const ok = w.player.won;
  const drift = Math.abs(frames - t.trace.frames);
  if (!ok || drift > 2) bad++;
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${t.name.padEnd(22)} frames ${frames}/${t.trace.frames}${w.player.dead ? ' (died)' : ''}`);
}
console.log(bad ? `${bad} chamber(s) differ from the C# game` : `all ${traces.length} replays match the C# game`);

// Web-built chambers and random tests carry their own solution: every one must exist and win (see CLAUDE.md).
let missing = 0, total = 0;
const checkOwn = (label, defs) => {
  for (const d of defs) {
    total++;
    if (!d.solution) { missing++; console.log(`NO SOLUTION ${label} ${d.name}`); continue; }
    const r = replay(d, d.solution);
    if (!r.won) { missing++; console.log(`FAIL ${label} ${d.name} (${r.dead ? 'died' : 'did not reach the exit'})`); }
  }
};
checkOwn('bonus', JSON.parse(readFileSync(new URL('../data/bonus.json', here))).chambers);
for (const [i, pool] of JSON.parse(readFileSync(new URL('../data/random2.json', here))).pools.entries()) checkOwn(`random tier ${i + 1}`, pool);
console.log(missing ? `${missing} web-built level(s) have no working solution` : `all ${total} web-built levels have a working solution`);
if (missing) bad++;
process.exit(bad ? 1 : 0);
