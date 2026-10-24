# Web chamber authoring

This is the maintained C# level exporter for the browser game. It references the frozen desktop assembly for
`LevelBuilder`, physics and bots; it does not modify the desktop game. Run with the .NET 10 SDK:

```sh
dotnet run --project tools/LevelForge -c Release -- data
node tests/content.mjs
node tests/challenges.mjs
node tests/replay.mjs
```

- `classic.json` freezes the original 53 maps and winning input traces. The legacy procedural generator uses
  process-randomized hashing, so rebuilding it would silently alter existing maps and invalidate records.
- `Circuits.cs` authors eight standalone puzzles and their solutions: Light Well, About Turn, Dry Dock,
  Freight Lift, Beam Split, Flyover, Closing Time and Rebound.
- `Campaign.cs` specifies 39 multi-stage plans. `Spatial.cs` connects their rooms as horizontal galleries or
  alternating floors, preserves necessary portal surfaces, and assigns separate circuits. Weight circuits and
  stable mirror circuits remain required by the final airlock; portal-dependent receivers and timers stay local.
- `Program.cs` orders all 100 chambers, ending with Final Exam. Original records stay keyed by their names;
  replacement chambers have new `recordKey` values. The first five lessons reveal hints automatically.

The spatial solution adapter reads the actual simulation through each room's local coordinate system, including
cube positions, door states and teleports. It transforms inputs, not outcomes. Mirrored portal shots account for
floor/ceiling pair selection. A stage must reach its exit and the complete reference solution must win with every
required airlock circuit active. Failed plans are rejected, never exported as fallbacks.

Random Test contains 36 distinct layouts per tier across 15 tiers. Tiers 1–5 combine two puzzles, tiers 6–10 combine
three, and tiers 11–15 combine four. No test repeats a source puzzle internally. Advanced tests require at least
two persistent circuits. Seeds, accepted plans and layout hashes are deterministic; duplicate layouts are rejected.

Useful diagnostics:

```sh
dotnet run --project tools/LevelForge -c Release -- --circuits   # eight new standalone solutions
dotnet run --project tools/LevelForge -c Release -- --sources    # all composable source puzzles
dotnet run --project tools/LevelForge -c Release -- --blueprints # all 47 additions without writing data
```

The exporter writes `data/levels.json`, `data/random.json`, and gitignored `test/traces.json` only after all 640
solutions win. The JS replay test covers every exported map, including the airlock requirement. Challenge tests
also check exit clearance, individual circuit locks, obvious run/jump shortcuts, progression and saved records.
Serve the repository and open `tests/browser-smoke.html` for actual canvas rendering checks.

The legacy desktop `--export-web` command still exports only frozen content. Use this tool for the browser game.
