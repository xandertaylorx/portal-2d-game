// Cycle every layout, avoid recent opening puzzles, then minimize repeated mechanics across whole tests.
export function pickRandomTest(pool, used, recent, random = Math.random) {
  const key = (def) => def.layoutId || def.seed;
  let fresh = pool.filter((def) => !used.has(key(def)));
  if (!fresh.length) {
    for (const def of pool) used.delete(key(def));
    fresh = pool;
  }
  const varied = fresh.filter((def) => !recent.some((r) => r.family === def.family));
  const available = varied.length ? varied : fresh;
  const overlap = (def) => recent.reduce((score, r, index) => score
    + def.mechanics.filter((m) => r.mechanics.includes(m)).length * (index + 1), 0);
  const lowest = Math.min(...available.map(overlap));
  const candidates = available.filter((def) => overlap(def) === lowest);
  const def = candidates[Math.floor(random() * candidates.length)];
  used.add(key(def));
  recent.push({ family: def.family, mechanics: def.mechanics });
  if (recent.length > 3) recent.shift();
  return def;
}
