// Builds the data the in-game "watch solution" ghost and the par times use, from the C# reference solutions in
// test/traces.json (made by the exporter; see CLAUDE.md). Run: node tools/make-solutions.mjs
import { readFileSync, writeFileSync } from 'node:fs';

const here = new URL('../', import.meta.url);
const traces = JSON.parse(readFileSync(new URL('test/traces.json', here)));
const levels = JSON.parse(readFileSync(new URL('data/levels.json', here)));
const solutions = {}, pars = {};
for (const c of levels.chambers) {
  const t = traces.find((x) => !x.random && x.name === c.name);
  if (!t) throw new Error(`no reference solution for ${c.name}`);
  solutions[c.name] = t.trace.inputs;
  pars[c.name] = Math.round(t.trace.frames / 60 * 1.5 * 10) / 10;   // gold par: 1.5x the reference run
}
writeFileSync(new URL('data/solutions.json', here), JSON.stringify(solutions));
writeFileSync(new URL('data/pars.json', here), JSON.stringify(pars));
console.log(`wrote ${Object.keys(solutions).length} solutions and pars`);
