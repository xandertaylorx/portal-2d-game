# Changelog

All notable changes to Portal 2D are listed here. Versions follow [semantic versioning](https://semver.org).

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
