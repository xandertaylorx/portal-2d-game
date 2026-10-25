// Generates the web-only chambers and random tests, each with a bot-made solution that is verified by replaying it:
//   data/bonus.json    20 hand-ordered chambers (appended after the 100 exported ones)
//   data/random2.json  15 difficulty tiers of random tests built from stages
// Run: node tools/build-levels.mjs      (see CLAUDE.md: every chamber needs a verified solution)
import { writeFileSync } from 'node:fs';
import { solveChamber, rng } from './stages.mjs';
import { replay } from './bot.mjs';

const out = (name) => new URL(`../data/${name}`, import.meta.url);
const par = (frames) => Math.round(frames / 60 * 1.5 * 10) / 10;
const finish = (res) => {
  const rp = replay(res.raw, res.solution);
  if (!rp.won) throw new Error(`${res.raw.name}: solution does not replay`);
  return { ...res.raw, solution: res.solution, par: par(res.frames) };
};

// ---- the 20 bonus chambers, ramping from one new idea at a time to everything together
const BONUS = [
  ['Elevator Pitch', ['lift'], { hints: ['BLUE BEAMS CARRY YOU AND CUBES UP.', 'WALK INTO THE BEAM, THEN STEP OFF AT THE TOP.'], theme: 2 }],
  ['Eye Contact', ['turret'], { hints: ['A SENTRY SHOOTS ANYTHING IT SEES.', 'PICK UP A CUBE WITH E AND HOLD IT BETWEEN YOU AND THE SENTRY.'], theme: 4 }],
  ['Trampoline Park', ['hop', 'gel'], { theme: 1 }],
  ['Cargo Run', ['button', 'lift']],
  ['Cover Story', ['turret', 'hop']],
  ['Pit Stop', ['pit']],
  ['Over the Wall', ['vault', 'hall']],
  ['Door Dash', ['button', 'vault']],
  ['Lookout Tower', ['lift', 'turret']],
  ['Moat', ['pit', 'turret']],
  ['Crossfire', ['nest']],
  ['Skyline', ['lift', 'gel', 'pit']],
  ['Open Fire', ['gauntlet']],
  ['Shield Wall', ['button', 'turret', 'hop']],
  ['High Roller', ['gel', 'vault', 'lift']],
  ['Watchtower', ['nest', 'pit']],
  ['Long Way Down', ['gel', 'button', 'turret', 'vault']],
  ['Obstacle Course', ['hop', 'pit', 'vault', 'gauntlet']],
  ['Hot Zone', ['gauntlet', 'lift', 'nest']],
  ['Final Inspection', ['lift', 'nest', 'gel', 'vault', 'button', 'gauntlet']],
];
const bonus = BONUS.map(([name, ids, o = {}], i) => {
  const res = solveChamber(name, ids, 1000 + i * 131, { difficulty: i + 1, ...o });
  console.log(`bonus ${String(i + 1).padStart(2)} ${name.padEnd(18)} ${ids.join('+').padEnd(40)} ${res.frames} frames`);
  return finish(res);
});
writeFileSync(out('bonus.json'), JSON.stringify({ chambers: bonus }));

// ---- random tests: tier t chains more stages, and the harder ones draw on every stage type
const ADJ = ['Dusty', 'Rusted', 'Silent', 'Cold', 'Hollow', 'Sunken', 'Bright', 'Narrow', 'Looming', 'Wired', 'Gilded', 'Frozen', 'Humming', 'Crooked', 'Distant', 'Final'];
const NOUN = ['Annex', 'Gallery', 'Cellar', 'Hangar', 'Atrium', 'Dock', 'Walkway', 'Vault', 'Foundry', 'Reservoir', 'Lab', 'Stack', 'Bay', 'Spine', 'Cistern', 'Deck'];
const TIERS = 15, PER = 20;
const pools = [];
for (let t = 0; t < TIERS; t++) {
  const r = rng(77 + t * 31);
  const easy = ['hop', 'button', 'lift', 'gel', 'turret', 'hall'];
  const mid = [...easy, 'pit', 'vault'];
  const hard = [...mid, 'gauntlet', 'nest'];
  const bag = t < 3 ? easy : t < 7 ? mid : hard;
  const count = 2 + Math.floor(t / 3);       // 2..6 stages
  const pool = [], seen = new Set();
  for (let k = 0; pool.length < PER && k < PER * 8; k++) {
    const ids = [];
    while (ids.length < count) {
      const pick = bag[Math.floor(r() * bag.length)];
      if (pick === 'hall' && ids.includes('hall')) continue;
      if (ids.length && ids[ids.length - 1] === pick) continue;     // never the same stage twice in a row
      ids.push(pick);
    }
    const sig = ids.join('+');
    if (seen.has(sig)) continue;
    seen.add(sig);
    const name = `${ADJ[Math.floor(r() * ADJ.length)]} ${NOUN[Math.floor(r() * NOUN.length)]}`;
    try {
      const res = solveChamber(name, ids, 5000 + t * 977 + k * 61, { difficulty: t + 1, noHints: t > 1 });
      pool.push(finish(res));
    } catch (e) { /* this combination does not solve; the loop just tries another */ }
  }
  if (pool.length < PER) throw new Error(`tier ${t}: only ${pool.length} levels`);
  console.log(`random tier ${String(t + 1).padStart(2)}: ${pool.length} levels, ${count} stages each, e.g. ${pool[0].family}`);
  pools.push(pool);
}
writeFileSync(out('random2.json'), JSON.stringify({ pools }));
console.log('done');
