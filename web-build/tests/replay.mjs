// Replays the C# reference solutions (exported by `Portal2D --export-web`) through the JS world and checks every
// chamber is still beaten, in about the same number of frames. Run: node web-build/tests/replay.mjs
import { readFileSync } from 'node:fs';
import { World, makeDef, stepFrame } from '../src/world.js';

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
  // after the trace ends the bot idles; give it a moment
  const ok = w.player.won;
  const drift = Math.abs(frames - t.trace.frames);
  if (!ok || drift > 2) bad++;
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${t.name.padEnd(22)} frames ${frames}/${t.trace.frames}${w.player.dead ? ' (died)' : ''}`);
}
console.log(bad ? `${bad} chamber(s) differ from the C# game` : `all ${traces.length} replays match the C# game`);
process.exit(bad ? 1 : 0);
