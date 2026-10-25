// Fight mode smoke test: arenas generate to size with a start tile, and a simulated match runs to the end without errors.
// Run: node tests/fight.mjs
import { Fight, generateArena } from '../src/fight.js';
import { emptyInput } from '../src/world.js';
// arenas generate sanely
for (let s = 1; s <= 200; s++) { const a = generateArena(s * 7919); if (a.def.map.length !== 22 || a.def.map.some(r => r.length !== 40)) throw new Error('bad arena size'); if (!a.def.map.join('').includes('P')) throw new Error('no P'); }
console.log('200 arenas ok');
const save = { store() {}, fightBest: 0, fightWins: 0 };
let events = 0;
const f = new Fight(save, { event: () => events++, shake() {} });
let rounds = 0, outcomes = { win: 0, lose: 0 };
f.level = 3;
const t0 = Date.now();
for (let frame = 0; frame < 60 * 120 && f.phase !== 'matchEnd'; frame++) {
  const inp = { ...emptyInput(), aimWorld: { x: f.evil.body.pos.x, y: f.evil.body.pos.y }, moveX: Math.sin(frame / 50) > 0 ? 1 : -1, jumpPressed: frame % 90 === 0, jumpHeld: frame % 90 < 20, fireBolt: frame % 20 < 3 };
  f.update(1 / 60, inp);
}
console.log('phase', f.phase, 'wins', f.wins, 'losses', f.losses, 'round', f.roundNo, 'events', events, 'ms', Date.now() - t0);
console.log('you hp', f.you.hp, 'evil hp', f.evil.hp, 'bolts', f.bolts.length);
if (f.phase !== 'matchEnd') throw new Error('the simulated match never finished');
