// Builds chambers out of "stages" (a pit to portal over, a turret to hide from, a lift to ride...). Every stage knows
// how to draw itself into the grid AND how to solve itself with the Bot, so a chamber is a list of stage names and
// comes out with a verified solution. See tools/build-levels.mjs for how these are turned into data files.
import { Bot } from './bot.mjs';

const T = 32, H = 14, FLOOR = 12;     // 14 rows: ceiling row 0, open rows 1..11, floor rows 12..13
const cx = (t) => t * T + T / 2;      // centre x of a tile column
const STAND_Y = FLOOR * T;            // y of the floor surface

export function rng(seed) {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6D2B79F5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const ri = (r, a, b) => a + Math.floor(r() * (b - a + 1));

class Grid {
  constructor(w) { this.w = w; this.g = Array.from({ length: H }, () => new Array(w).fill('X')); }
  set(x, y, c) { if (x >= 0 && y >= 0 && x < this.w && y < H) this.g[y][x] = c; }
  fill(x0, y0, x1, y1, c) { for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) this.set(x, y, c); }
  rows() { return this.g.map((r) => r.join('')); }
}

// Each stage: { width(r) -> columns it needs, draw(g, x0, r, ctx) , run(bot, x0, ctx) , hint, gun, mech }
export const STAGES = {
  // an empty hall: just hanging blocks, so the chamber has more shape
  hall: {
    mech: 'hall', gun: false, hint: '',
    plan: (r) => ({ w: ri(r, 6, 10), cols: Array.from({ length: ri(r, 1, 3) }, () => [ri(r, 1, 5), ri(r, 1, 4)]) }),
    width: (p) => p.w,
    draw(g, x0, p) { for (const [dx, h] of p.cols) g.fill(x0 + dx, 1, x0 + dx + 1, h, 'X'); },
    run(bot, x0, p) { bot.walkTo(cx(x0 + p.w - 1), 6); },
  },

  // a block or two to jump over
  hop: {
    mech: 'hop', gun: false, hint: '',
    plan: (r) => ({ blocks: r() < 0.5 ? [2] : [2, 5 + ri(r, 0, 1)], h: r() < 0.3 ? 2 : 1 }),
    width: () => 9,
    draw(g, x0, p) { for (const b of p.blocks) g.fill(x0 + b, FLOOR - p.h, x0 + b, FLOOR - 1, 'X'); },
    run(bot, x0, p) {
      for (const b of p.blocks) {
        bot.walkTo((x0 + b) * T - 30 - (p.h - 1) * 6, 5);
        bot.moveX = 1;
        bot.jump(p.h > 1 ? 28 : 22);
        bot.hold(1, (c) => c.p.pos.x > (x0 + b + 1) * T + 22 && c.p.onGround, 200, 'landing past the block');
      }
      bot.stop();
    },
  },

  // a goo pit too wide to jump: portal across the floor
  pit: {
    mech: 'pit', gun: true, hint: 'DEADLY GOO. PUT A PORTAL ON EACH SIDE OF IT.',
    plan: (r) => ({ g: ri(r, 6, 9) }),
    width: (p) => 8 + p.g,
    draw(g, x0, p) {
      g.fill(x0, FLOOR, x0 + 3, FLOOR, '#'); g.fill(x0 + 4, FLOOR, x0 + 3 + p.g, FLOOR + 1, '~'); g.fill(x0 + 4 + p.g, FLOOR, x0 + 7 + p.g, FLOOR, '#');
    },
    run(bot, x0, p) {
      bot.walkTo(cx(x0), 6);
      bot.shoot('blue', cx(x0 + 6 + p.g), STAND_Y);
      bot.shoot('orange', cx(x0 + 3) - 8, STAND_Y + 4);
      bot.hold(1, (c) => c.p.pos.x > (x0 + 6 + p.g) * T + 70 && c.p.onGround, 400, 'crossing the pit');
      bot.stop();
    },
  },

  // a wall too tall to climb: blue on the wall, orange on the ceiling beyond it
  vault: {
    mech: 'vault', gun: true, hint: 'TOO HIGH TO CLIMB. A CEILING IS A GOOD PLACE FOR A PORTAL.',
    plan: () => ({}),
    width: () => 12,
    draw(g, x0) { g.fill(x0 + 6, 6, x0 + 6, FLOOR - 1, '#'); g.fill(x0 + 7, 0, x0 + 10, 0, '#'); },
    run(bot, x0) {
      bot.walkTo(cx(x0 + 1), 6);
      bot.shoot('blue', (x0 + 6) * T + 8, 352);
      bot.shoot('orange', cx(x0 + 8), 28);
      bot.hold(1, (c) => c.p.pos.x > (x0 + 7) * T + 20 && c.p.onGround, 400, 'dropping out of the ceiling');
      bot.stop();
    },
  },

  // a cube on a button holds the door open
  button: {
    mech: 'button', gun: false, hint: 'A CUBE ON THE BUTTON HOLDS THE DOOR OPEN. PICK IT UP WITH E.',
    plan: (r, ctx) => ({ grp: ctx.group() }),
    width: () => 12,
    draw(g, x0, p) {
      g.set(x0 + 1, FLOOR - 1, 'C'); g.set(x0 + 5, FLOOR - 1, '123xyz'[p.grp]);
      g.fill(x0 + 8, 1, x0 + 8, FLOOR - 1, String.fromCharCode(97 + p.grp));
    },
    run(bot, x0, p) {
      bot.walkTo(cx(x0 + 1) - 34, 4);
      bot.use(cx(x0 + 1), STAND_Y - 14);
      bot.walkTo(cx(x0 + 4), 4);
      bot.aimAt(cx(x0 + 5), STAND_Y - 8);
      bot.wait(8);
      bot.use();
      bot.until((b) => !b.w.isSolid(x0 + 8, FLOOR - 1), 300, 'the door to open');
      const cubeX = bot.w.cubes.reduce((m, c) => (Math.abs(c.pos.x - cx(x0 + 5)) < Math.abs(m.pos.x - cx(x0 + 5)) ? c : m)).pos.x;   // the cube on the button is in the way: hop over it
      bot.walkTo(cubeX - 44, 4);
      bot.moveX = 1;
      bot.jump(24);
      bot.hold(1, (c) => c.p.pos.x > cubeX + 40 && c.p.onGround, 200, 'landing past the cube');
      bot.walkTo(cx(x0 + 10), 6);
    },
  },

  // ride a blue beam up to a high shelf
  lift: {
    mech: 'lift', gun: false, hint: 'BLUE BEAMS CARRY YOU UP.',
    plan: () => ({}),
    width: () => 11,
    draw(g, x0) { g.set(x0 + 3, FLOOR, 'B'); g.fill(x0 + 4, 2, x0 + 9, 3, 'X'); },
    run(bot, x0) {
      bot.walkTo(cx(x0 + 3), 3);
      bot.until((b) => b.p.pos.y < 54, 300, 'the ride to the top');
      bot.hold(1, (c) => c.p.pos.x > (x0 + 10) * T + 4 && c.p.onGround && c.p.pos.y > 340, 500, 'walking off the shelf');
      bot.stop();
    },
  },

  // bounce on blue gel up to a shelf
  gel: {
    mech: 'gel', gun: false, hint: 'BLUE GEL BOUNCES YOU HIGH.',
    plan: () => ({}),
    width: () => 13,
    draw(g, x0) { g.set(x0 + 2, FLOOR, 'J'); g.fill(x0 + 5, 6, x0 + 11, 7, 'X'); },
    run(bot, x0) {
      bot.walkTo(cx(x0), 5);
      bot.moveX = 1;
      bot.jump(14);
      bot.hold(1, (c) => c.p.pos.x > (x0 + 12) * T + 4 && c.p.onGround && c.p.pos.y > 340, 500, 'landing after the bounce');
      bot.stop();
    },
  },

  // a sentry in the middle of a corridor: carry a cube and keep it between you and the turret
  turret: {
    mech: 'turret', gun: false, hint: 'THE SENTRY SHOOTS WHAT IT SEES. HOLD A CUBE BETWEEN YOU.',
    plan: () => ({}),
    width: () => 18,
    draw(g, x0) {
      g.set(x0 + 1, FLOOR - 1, 'C');
      g.fill(x0 + 3, 1, x0 + 4, 8, 'X'); g.fill(x0 + 14, 1, x0 + 15, 8, 'X');
      g.set(x0 + 9, 1, 'S');
    },
    run(bot, x0) {
      bot.walkTo(cx(x0 + 1) - 34, 4);
      bot.use(cx(x0 + 1), STAND_Y - 14);
      const tx = cx(x0 + 9), ty = 48;
      bot.track(() => ({ x: tx, y: ty }));
      bot.walkTo(cx(x0 + 17), 5);
      bot.untrack();
      bot.aimAt(cx(x0 + 17) + 30, STAND_Y - 6);
      bot.wait(10);
      bot.use();
      bot.wait(10);
    },
  },

  // a goo pit with a sentry overhead: shield up, then portal across
  gauntlet: {
    mech: 'turret', gun: true, hint: '',
    plan: (r) => ({ g: ri(r, 6, 8) }),
    width: (p) => 8 + p.g + 6,
    draw(g, x0, p) {
      g.set(x0, FLOOR - 1, 'C');
      g.fill(x0 + 2, FLOOR, x0 + 5, FLOOR, '#'); g.fill(x0 + 6, FLOOR, x0 + 5 + p.g, FLOOR + 1, '~'); g.fill(x0 + 6 + p.g, FLOOR, x0 + 9 + p.g, FLOOR, '#');
      g.set(x0 + 5 + Math.floor(p.g / 2), 1, 'S');
      g.fill(x0 + 2, 1, x0 + 2, 6, 'X');
      g.fill(x0 + 11 + p.g, 1, x0 + 12 + p.g, 8, 'X');     // a lintel that hides you from the sentry once you are through
    },
    run(bot, x0, p) {
      bot.walkTo(cx(x0) - 34, 4);
      bot.use(cx(x0), STAND_Y - 14);
      const tgt = { x: cx(x0 + 5 + Math.floor(p.g / 2)), y: 48 };
      bot.track(() => tgt);
      bot.walkTo(cx(x0 + 3), 5);
      bot.shoot('blue', cx(x0 + 8 + p.g), STAND_Y);
      bot.shoot('orange', cx(x0 + 5) - 8, STAND_Y);
      bot.track(() => tgt);
      bot.hold(1, (c) => c.p.pos.x > (x0 + 8 + p.g) * T + 70 && c.p.onGround, 400, 'crossing under fire');
      bot.moveX = 0;
      bot.track(() => tgt);
      bot.walkTo(cx(x0 + 13 + p.g), 5);
      bot.untrack();
      bot.aimAt(bot.p.pos.x + 30, STAND_Y - 6);
      bot.wait(10);
      bot.use();
      bot.wait(8);
    },
  },

  // a laser blocked by a wall: put a portal where it hits, and one where you want it to go
  laserdoor: {
    mech: 'laser', gun: true, hint: 'A BEAM CAN GO THROUGH A PORTAL. THE DOOR OPENS WHEN THE TARGET IS HIT.',
    plan: (r, ctx) => ({ grp: ctx.group() }),
    width: () => 26,
    draw(g, x0, p) {
      g.set(x0, 3, '>');
      g.fill(x0 + 8, 1, x0 + 8, 6, '#'); g.fill(x0 + 13, 1, x0 + 13, 6, '#');
      g.fill(x0 + 22, 1, x0 + 22, FLOOR - 1, 'X');
      g.set(x0 + 22, 4, '456rst'[p.grp]);      // the beam comes out of the portal one row low, so the target sits at row 4
      g.fill(x0 + 22, 8, x0 + 22, FLOOR - 1, String.fromCharCode(97 + p.grp));
    },
    run(bot, x0, p) {
      bot.walkTo(cx(x0 + 4), 5);
      bot.shoot('blue', (x0 + 8) * T + 8, 112);
      bot.walkTo(cx(x0 + 16), 5);
      bot.shoot('orange', (x0 + 13) * T + 8, 112);
      bot.until((b) => !b.w.isSolid(x0 + 22, FLOOR - 1), 400, 'the laser to open the door');
      bot.walkTo(cx(x0 + 24), 6);
    },
  },

  // a timed button and a door far away: set up the portals first, then sprint
  timed: {
    mech: 'timed', gun: true, hint: 'THE DOOR ONLY STAYS OPEN FOR A FEW SECONDS.',
    plan: (r, ctx) => ({ grp: ctx.group() }),
    width: () => 23,
    draw(g, x0, p) {
      g.set(x0 + 3, FLOOR - 1, 'TUVWYZ'[p.grp]);
      g.fill(x0 + 9, 6, x0 + 9, FLOOR - 1, '#'); g.fill(x0 + 10, 0, x0 + 14, 0, '#');
      g.fill(x0 + 20, 1, x0 + 20, FLOOR - 1, String.fromCharCode(97 + p.grp));
    },
    run(bot, x0) {
      bot.walkTo(cx(x0), 5);
      bot.shoot('blue', (x0 + 9) * T + 8, 340);
      bot.shoot('orange', cx(x0 + 12), 28);
      bot.walkTo(cx(x0 + 3), 3);
      bot.hold(1, (c) => c.p.pos.x > (x0 + 22) * T, 500, 'the sprint to the door');
      bot.stop();
    },
  },

  // a launch plate throws you across a pit that is far too wide to jump
  fling: {
    mech: 'plate', gun: false, hint: 'A LAUNCH PLATE THROWS YOU UP AND FORWARD.',
    plan: (r) => ({ g: ri(r, 8, 10) }),
    width: (p) => 8 + p.g + 6,
    draw(g, x0, p) { g.set(x0 + 4, FLOOR, 'Q'); g.fill(x0 + 5, FLOOR, x0 + 4 + p.g, FLOOR + 1, '~'); },
    run(bot, x0, p) {
      bot.walkTo(cx(x0 + 1), 5);
      bot.hold(1, (c) => c.p.pos.x > (x0 + 5 + p.g) * T + 30 && c.p.onGround, 500, 'landing across the pit');
      bot.stop();
    },
  },

  // two sentries and a ride up: turret nest above a lift
  nest: {
    mech: 'turret', gun: false, hint: '',
    plan: () => ({}),
    width: () => 20,
    draw(g, x0) {
      g.set(x0 + 1, FLOOR - 1, 'C');
      g.fill(x0 + 3, 1, x0 + 4, 8, 'X'); g.fill(x0 + 16, 1, x0 + 17, 8, 'X');
      g.set(x0 + 8, 1, 'S'); g.set(x0 + 12, 1, 'S');
      g.set(x0 + 10, FLOOR, 'B'); g.fill(x0 + 11, 2, x0 + 15, 3, 'X');
    },
    run(bot, x0) {
      bot.walkTo(cx(x0 + 1) - 34, 4);
      bot.use(cx(x0 + 1), STAND_Y - 14);
      const t1 = { x: cx(x0 + 8), y: 48 }, t2 = { x: cx(x0 + 12), y: 48 };
      // keep the cube toward whichever sentry is nearer
      bot.track((b) => (Math.abs(b.p.pos.x - t1.x) < Math.abs(b.p.pos.x - t2.x) ? t1 : t2));
      bot.walkTo(cx(x0 + 18), 5);
      bot.untrack();
      bot.aimAt(cx(x0 + 18) + 30, STAND_Y - 6);
      bot.wait(10);
      bot.use();
      bot.wait(10);
    },
  },
};

/** Builds one chamber from a list of stage ids. Returns { raw, solution, frames } or throws if the bot cannot solve it. */
export function buildChamber(name, ids, seed, opts = {}) {
  const r = rng(seed);
  const ctx = { used: 0, group() { if (this.used >= 6) throw new Error('too many circuits'); return this.used++; } };
  const plans = ids.map((id) => STAGES[id].plan(r, ctx));
  const widths = ids.map((id, i) => STAGES[id].width(plans[i]));
  const startW = 6, endW = 7;
  const W = 1 + startW + widths.reduce((a, b) => a + b, 0) + endW + 1;
  const g = new Grid(W);
  g.fill(1, 1, W - 2, FLOOR - 1, ' ');
  // a few random ceiling stalactites and wall recesses so no two chambers look alike
  // (only over the start and end rooms, where they cannot get in a stage's way)
  for (let i = 0, n = ri(r, 1, 3); i < n; i++) { const x = r() < 0.5 ? ri(r, 5, startW) : W - endW + ri(r, 0, endW - 3), h = ri(r, 1, 2); g.fill(x, 1, x + ri(r, 0, 1), h, 'X'); }
  g.set(3, FLOOR - 1, 'P');
  let x = 1 + startW;
  const xs = [];
  ids.forEach((id, i) => { xs.push(x); STAGES[id].draw(g, x, plans[i]); x += widths[i]; });
  const exitX = x + 3;
  g.set(exitX, FLOOR - 1, 'E');
  const gun = ids.some((id) => STAGES[id].gun) ? 2 : (opts.gun ?? 0);
  const hints = [];
  for (const id of new Set(ids)) if (STAGES[id].hint && !opts.noHints) hints.push(STAGES[id].hint);
  const raw = {
    name, map: g.rows(), gun, blue: [1, 0], orange: [1, 0], hints: opts.hints || hints.slice(0, 2), fling: false, label: opts.label || '',
    theme: opts.theme ?? ri(r, 1, 6), seed, family: ids.join('+'), layoutId: `${name}:${seed}`, mechanics: [...new Set(ids.map((id) => STAGES[id].mech))],
    difficulty: opts.difficulty || 0, exitGroups: opts.gate ? ids.flatMap((id, i) => (id === 'button' ? [plans[i].grp] : [])) : [], tutorial: false,
  };
  const bot = new Bot(raw);
  ids.forEach((id, i) => STAGES[id].run(bot, xs[i], plans[i]));
  bot.finish(cx(exitX));
  return { raw, solution: bot.inputs(), frames: bot.frames };
}

/** Tries successive seeds until the bot solves the chamber. */
export function solveChamber(name, ids, seed, opts = {}, tries = 30) {
  let lastErr;
  for (let k = 0; k < tries; k++) {
    try { return { ...buildChamber(name, ids, seed + k * 7919, opts), tries: k + 1 }; } catch (e) { lastErr = e; }
  }
  throw new Error(`${name} [${ids.join(',')}]: ${lastErr.message}`);
}
