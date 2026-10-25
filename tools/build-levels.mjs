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

// ---- the 20 bonus chambers (101-120): every one is longer and denser than chamber 100, and they only get worse
const NAMES = ['Aftershock', 'Dead Reckoning', 'Hairpin', 'Glass Cannon', 'Tripwire', 'Overclock', 'Redshift', 'Undertow', 'Brownout', 'Cold Open',
  'Fault Line', 'Long Fuse', 'Blackout', 'Wrecking Ball', 'Deep End', 'Skyfall', 'Last Light', 'Crucible', 'Event Horizon', 'Terminal'];
const CIRCUITS = new Set(['button', 'laserdoor', 'timed']);
function hardStages(i, r) {
  const n = 8 + Math.round(i * 0.55);                       // 8 stages up to 19
  const core = ['pit', 'vault', 'button', 'turret', 'lift', 'gel', 'laserdoor', 'timed', 'fling', 'gauntlet', 'nest'];
  const ids = [];
  const must = i >= 2 ? ['laserdoor', 'timed', 'fling'] : ['laserdoor'];
  for (const m of must) ids.push(m);
  if (i >= 5) ids.push('gauntlet', 'nest');
  while (ids.length < n) ids.push(core[Math.floor(r() * core.length)]);
  // shuffle, then make sure no stage repeats back to back and there are at most six circuits
  for (let k = ids.length - 1; k > 0; k--) { const j = Math.floor(r() * (k + 1)); [ids[k], ids[j]] = [ids[j], ids[k]]; }
  let circuits = 0;
  return ids.filter((id, k) => {
    if (CIRCUITS.has(id) && ++circuits > 6) return false;
    return !(k > 0 && ids[k - 1] === id);
  });
}
const bonus = NAMES.map((name, i) => {
  let res = null, tries = 0;
  const r = rng(900 + i * 17);
  while (!res && tries++ < 60) {
    const ids = hardStages(i, r);
    try { res = solveChamber(name, ids, 1000 + i * 131 + tries * 53, { difficulty: 20 + i, noHints: true, gate: true }, 4); } catch (e) { /* try another arrangement */ }
  }
  if (!res) throw new Error(`could not build ${name}`);
  console.log(`bonus ${String(i + 1).padStart(2)} ${name.padEnd(15)} ${String(res.raw.family.split('+').length).padStart(2)} stages  ${String(res.frames).padStart(5)} frames  ${res.raw.map[0].length} wide`);
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
  const mid = [...easy, 'pit', 'vault', 'timed', 'fling'];
  const hard = [...mid, 'gauntlet', 'nest', 'laserdoor', 'laserdoor'];
  const bag = t < 3 ? easy : t < 7 ? mid : hard;
  const count = 2 + Math.floor(t / 2);       // 2..9 stages
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
