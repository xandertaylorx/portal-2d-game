# OLD-DESKTOP

The original C# + Raylib desktop version of Portal 2D, frozen at **v2.8.1**. The maintained game is the web version at
the repo root (play it at https://xandertaylorx.github.io/portal-2d-game/).

- Old desktop builds are still on the [Releases page](https://github.com/xandertaylorx/portal-2d-game/releases) and
  keep working. There will be no further desktop releases.
- The chambers were authored here. `src/Portal2D/Game/Levels.cs`, `Chambers.cs` and `Procgen.cs` produce the levels, and the
  bots in `Testing/` prove each one is solvable.
- Export the levels for the web game (writes `data/levels.json`, `data/random.json` and the replay traces in `test/`):

  ```bash
  dotnet run --project OLD-DESKTOP/src/Portal2D -c Release -- --export-web data
  node tests/replay.mjs
  ```
- Run the desktop game: `dotnet run --project OLD-DESKTOP/src/Portal2D -c Release` (needs the .NET 10 SDK).
- The scripts in `scripts/` are retired and no longer match the repo layout.
