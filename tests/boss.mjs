// Boss fight test: a scripted player that only uses portals (blue on the shell's landing spot, orange on the ceiling above
// the mech) must be able to beat COLOSSUS without dying. If this fails the fight is not beatable. Run: node tests/boss.mjs
import assert from 'node:assert/strict';
import { Boss } from '../src/boss.js';
import { emptyInput } from '../src/world.js';

const LEDGE_Y = 384;
function play(seed) {
  const save = { store() {}, bossBest: 0, bossWins: 0 };
  const boss = new Boss(save, { event() {}, shake() {} });
  const handled = new Set();
  let shootWait = 0, frames = 0;
  while (boss.phase !== 'won' && frames < 60 * 400) {
    const p = boss.player, w = boss.world;
    const inp = { ...emptyInput(), aimWorld: { x: p.pos.x + 100, y: p.pos.y - 50 } };
    shootWait -= 1 / 60;
    if (boss.phase === 'fight' && !p.dead) {
      const left = p.pos.x < 512;
      const home = left ? 260 : 760;
      let want = home;
      const live = boss.shells.filter((s) => !s.reflected);
      for (const s of live) if (Math.abs(p.pos.x - s.land) < 130) want = p.pos.x + (p.pos.x < s.land ? -140 : 140);   // keep clear of landing spots
      const blue = w.portals[0];                       // do not stand on your own portal: it drops you into the pit
      if (blue && Math.abs(want - blue.center.x) < 100) want = blue.center.x + (left ? (blue.center.x > 260 ? -110 : 110) : (blue.center.x > 760 ? -110 : 110));
      want = Math.max(left ? 170 : 700, Math.min(left ? 340 : 860, want));
      inp.moveX = Math.abs(want - p.pos.x) > 14 ? Math.sign(want - p.pos.x) : 0;
      for (const wv of boss.waves) if (wv.wait <= 0 && Math.abs(wv.x - p.pos.x) < 70 && Math.sign(p.pos.x - wv.x) === wv.dir && p.onGround) { inp.jumpPressed = true; inp.jumpHeld = true; }   // hop shockwaves
      const orange = w.portals[1];
      if (!orange && shootWait <= 0 && boss.purgeWarn <= 0) { inp.aimWorld = { x: 512, y: 20 }; inp.fireOrange = true; shootWait = 0.5; }   // the exit portal on the ceiling above the mech
      else if (shootWait <= 0) {
        const s = live.find((q) => !handled.has(q));
        if (s && s.age > 0.15) { inp.aimWorld = { x: s.land, y: LEDGE_Y + 2 }; inp.fireBlue = true; handled.add(s); shootWait = 0.4; }     // blue where the shell lands
      }
    }
    boss.update(1 / 60, inp);
    frames++;
  }
  return { won: boss.phase === 'won', time: boss.time, hearts: boss.hearts, attempts: boss.attempts, hits: boss.totalHits };
}
let wins = 0; const out = [];
for (let s = 1; s <= 6; s++) { const r = play(s * 977); out.push(`${r.won ? 'won ' : 'LOST'} ${r.time.toFixed(0)}s  hearts left ${r.hearts}  attempts ${r.attempts}  boss hits ${r.hits}`); if (r.won) wins++; }
console.log(out.join('\n'));
assert.equal(wins, 6, 'the scripted player must beat the boss every time');
console.log('boss is beatable with portals alone');
