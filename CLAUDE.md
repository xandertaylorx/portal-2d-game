# Portal 2D: notes for Claude

Pixel-art portal puzzler in C# (.NET 10) + Raylib-cs. Source: `src/Portal2D/`. See README.md for the architecture.

## "commit push" means commit, push AND release

Whenever the user says **"commit push"** (or "commit and push", "push it", or similar), do all of this, not just the
commit:

1. Run the checks. All of them must pass before releasing:
   - `dotnet run --project src/Portal2D -- --test` (every chamber must be solvable)
   - `dotnet run --project src/Portal2D -- --procgen 30` (generated chambers must verify)
2. Pick the next version from the latest `## [x.y.z]` in `CHANGELOG.md`:
   - **patch** (x.y.Z+1): fixes and small tweaks
   - **minor** (x.Y+1.0): new chambers, mechanics or features
   - **major**: only if the user asks
3. Add a `## [new version] - YYYY-MM-DD` section at the top of `CHANGELOG.md` with Added / Changed / Fixed bullets
   written for players. Update the version badge in `README.md`.
4. Commit everything with a descriptive message and push.
5. Run `./scripts/release.sh <new version>`. It re-runs the tests, stamps `<Version>` in the csproj, builds macOS
   (arm64 + x64), Windows and Linux packages, tags `v<version>`, pushes, and creates the GitHub release on
   `xandertaylorx/portal-2d-game`. Installed games then offer the update from the main menu.
6. Report the release URL.

If the tests fail, don't release: fix the problem or tell the user.

## How releases and updates work

- Releases live on this repo's GitHub Releases. `Game/Updater.cs` (`ReleaseRepo`) checks
  `releases/latest` and downloads the `Portal2D-<rid>.zip` asset for the player's platform
  (`osx-arm64`, `osx-x64`, `win-x64`, `linux-x64`). Those zips must contain the flat publish output. Don't rename them.
- `Portal2D-macOS-<arch>-app.zip` are the double-clickable apps for first-time downloads.
- Dev builds (`dotnet run`, anything under `bin/`) never self-update.
- v2.1.0 builds check the archived `xandertaylorx/portal-2d-releases` repo, which holds a final v2.2.0 bridge release
  so those players can update. Don't delete that repo while old builds may still be around.

## Working on levels

- Handcrafted chambers 1-21: `Game/Levels.cs`. Each needs a reference solution in `Testing/Solutions.cs` (same index).
- Chambers 22-50: `Levels.AddComposed`, each a fixed list of `Procgen` rooms plus a seed. Their solutions are
  generated with the rooms. If one fails `--test`, change its seed or rooms. Rooms that need a floor-level entrance
  (Fling, Laser, UpFling, TwinBeam) can't follow rooms that exit high (HighLedge, Gel, UpFling).
- `--test` must pass for all chambers, and `--procdiag` checks every room type. Best times are saved by chamber **name**, so renaming a chamber resets its
  best time.
- Random Test rooms: `Game/Procgen.cs`. Each room archetype must emit its own bot solution.
- Look at art without a window: `--preview <n|rSEED> out.png`, `--sprites out.png`, `--banner out.png`.
  Real screenshots: `scripts/shots.sh <dir> [names]` (needs a display; run it outside the sandbox).
