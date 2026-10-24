// Prefer a new layout and a different puzzle family. Once a tier is exhausted, start a fresh cycle.
export function pickRandomTest(pool, used, recent, random = Math.random) {
  const key = (def) => def.layoutId || def.seed;
  let fresh = pool.filter((def) => !used.has(key(def)));
  if (!fresh.length) {
    for (const def of pool) used.delete(key(def));
    fresh = pool;
  }
  const varied = fresh.filter((def) => !recent.includes(def.family));
  const candidates = varied.length ? varied : fresh;
  const def = candidates[Math.floor(random() * candidates.length)];
  used.add(key(def));
  recent.push(def.family);
  if (recent.length > 3) recent.shift();
  return def;
}
