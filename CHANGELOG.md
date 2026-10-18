# Changelog

All notable changes to Portal 2D are listed here. Versions follow [semantic versioning](https://semver.org).

## [2.3.0] - 2026-10-18

### Added
- **29 new chambers - 50 in total**, getting steadily harder: two-room chambers first, then three, four and five
  rooms, ending in an eight-room **Final Exam** (chamber 50).
- New puzzle rooms: cube on a high shelf, cube across an emancipation grid, upward fling to a high ledge, a ceiling
  laser (beam fired upward), twin beams and a gel island. They also show up in Random Test.
- "Hard" variants of every room type: higher ledges, wider goo, extra beams, tighter windows.
- Chamber select has pages (arrow buttons or the left/right keys).

### Changed
- The original Final Exam is now **Midterm** (chamber 21). Your best time carries over.
- Random Test runs can now reach six rooms.
- Up to six door/button/laser circuits per chamber (was three).

### Fixed
- macOS: after an in-game update, Finder's "Get Info" now shows the new version number.
- A very fast fling could slide through a doorway into the next room. Fling rooms now end in a low wall.

## [2.2.2] - 2026-10-18

### Fixed
- macOS: updating did nothing when the game was opened straight from Downloads. macOS secretly ran it from a hidden
  read-only copy, so the updater changed that copy instead of the real app. The updater now finds the real app,
  updates it, clears the download quarantine flag so it isn't run from a hidden copy again, and re-signs it.
- If the game folder can't be written to, the update banner now says why (for example, "move Portal 2D to
  Applications") instead of silently restarting the old version.

## [2.2.1] - 2026-10-18

### Fixed
- macOS downloads no longer show "Portal 2D is damaged and can't be opened": the app bundle is now signed as a whole,
  and the updater re-signs it after installing an update.

## [2.2.0] - 2026-10-18

### Added
- Three new chambers before the Final Exam (21 in total):
  - **Skylight**: catch an upward beam in the ceiling and send it sideways into a receiver.
  - **Overpass**: a longer fling over goo, straight through an emancipation grid.
  - **Bounce House**: carry a cube through a ceiling drop onto gel and up to a button on the ledge.

### Changed
- Updates now come straight from the main repository's releases.
- Best times are stored by chamber name, so adding chambers no longer mixes up your records. Older saves are
  migrated automatically.
- A chamber unlocks as soon as you finish the one before it.

## [2.1.0] - 2026-10-18

### Added
- **Portal lenses**: linked portals now swell into an oval lens showing a live, fish-eye view out of the partner
  portal. It has parallax, ripples, a swirl and a dithered, sparkling rim, and characters passing through stay visible.
- **In-game updates**: the main menu tells you when a new version is out, downloads it, and restarts into it.
  "Check for updates" can be turned off in Options.
- README with screenshots and gameplay GIFs, a changelog, and a one-command release script.

### Changed
- Hint notes stay at the top of the screen, fade after about 10 seconds or as soon as you start moving, and don't
  come back when you restart. Press `H` to bring them back.
- License changed to PolyForm Strict 1.0.0: free to play, the code remains the author's.

### Removed
- The old dome-shaped see-through effect.

## [2.0.0] - 2026-10-18

### Added
- Complete pixel-art overhaul: 640×360 canvas, procedurally painted 16 px tiles and sprites, a terracotta critter
  hero, a hand-made bitmap font and pixel UI.
- Blue repulsion gel (bounce pads).
- Six new chambers: Spring Loaded, Two Weights, Gel Pit, Umbrella, Twin Beams, Grid Lock. That's 18 in total.
- **Random Test** mode: endless procedurally generated chambers, each verified solvable by a bot before you play it.
- CPU preview renderer and screenshot tooling.

### Fixed
- Players counted as standing on the ground for one frame after leaving a portal.
- Held cubes lagged behind the player too much to block lasers.

## [1.0.0] - 2026-10-18

### Added
- First release: 12 chambers, portals with momentum, cubes, buttons, doors, goo, emancipation grids, lasers, glass,
  see-through portal cones, synthesized audio, menus, options and save data.
