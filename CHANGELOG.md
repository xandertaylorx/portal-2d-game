# Changelog

All notable changes to Portal 2D are listed here. Versions follow [semantic versioning](https://semver.org).

## [2.8.0] - 2026-10-23

### Added
- **Portal 2D now runs in your browser.** Play the full game at
  [xandertaylorx.github.io/portal-2d-game](https://xandertaylorx.github.io/portal-2d-game/): all 53 chambers, Random
  Test, the map builder, featured maps, the portal lenses and the sound effects. Nothing to download. Your progress
  and maps are saved in the browser.
- **Share maps as links.** In the web version, SHARE copies a link that opens straight into the game and adds the map
  to your list. Map codes are the same as the desktop game's, so maps move freely between the two.
- **Back up your maps.** The web map builder can download all your maps as one file.

### Changed
- Random Test chambers in the web version come from a large pool of pre-verified chambers per difficulty.

## [2.7.0] - 2026-10-21

### Added
- **Featured maps.** Press **FEATURED MAPS** on the Map Builder screen to browse hand-picked chambers hosted on the
  game's GitHub page. Add one to My Maps and play it. Six maps to start, more to come.
- **One-button portals.** A new option in OPTIONS makes left click fire blue, then orange, then blue again, so you
  can play with a single button. The original left = blue, right = orange is still the default.
- **Three new chambers.** *Stepping Stones* (a new handcrafted chamber), plus *Stairway* and *Crossfire*.
  Chambers now run to 53, with Final Exam still at the end.

### Changed
- You must beat your own map before you can share it, so everything shared is known to be finishable.
- The main menu buttons are wider and text that is too long for a button is shortened instead of spilling out.

## [2.6.1] - 2026-10-21

### Fixed
- Jumping (and picking up, and firing portals) no longer gets randomly ignored. A quick key press could
  fall between game updates and vanish, so you'd run and jump without jumping.
- Your character no longer keeps drifting forward in mid-air after you let go of the move keys. Speed from
  gels and launch plates still carries you as before.
- Open doors no longer cover you: the dark door frame is now drawn behind the player and cubes.

## [2.6.0] - 2026-10-21

### Added
- **Map builder.** A new **MAP BUILDER** button on the main menu lets you design your own chambers. Pick a tool,
  click to place it, right click to erase. You get walls, glass, goo, emancipation grids, gels, launch plates,
  cubes, mirror cubes, buttons, timed buttons, doors, lasers and laser targets, plus a draw / box / fill brush.
- Doors, buttons and laser targets are wired together by **colour and number**, with coloured badges and link
  lines on the map so you can see what opens what. The editor warns you about doors nobody can open.
- **Test play** a map from the editor and beat it to mark it as beaten. Undo and redo, resize the map, pick a
  wall style and choose the portal gun (none, blue only, or both) from MAP OPTIONS.
- Maps **save on your device automatically**. **My Maps** shows a thumbnail, your best time and PLAY, EDIT, SHARE
  and DELETE for each one.
- **Share maps with friends**: SHARE copies a short code, and PASTE MAP CODE adds a friend's map. OPEN MAPS FOLDER
  shows the map files.

## [2.5.0] - 2026-10-20

### Added
- **19 brand-new handcrafted chambers (22-40)**, each with its own layout and its own trick: shooting portals
  through a window, using a cube as a step stool, throwing a cube with a launch plate, a sealed-cell jailbreak,
  a level with no portal gun, a fall-speed loop, timed-button runs, rooftop platforming and more.
- **Seven new room types in Random Test** (window shots, stool, catapult, moat, grid crossing, penthouse,
  spotlight). It now draws from 28 room types.

### Changed
- Chambers 22-40 are no longer built from repeated room templates. Chambers 41-50 are longer runs that mix the new
  rooms in, ending in a nine-room Final Exam.
- Best times for chambers 22-50 start fresh, because the chambers have new names.

## [2.4.0] - 2026-10-19

### Added
- **Five new mechanics**: orange **launch plates** that throw you up and sideways, orange **speed gel** that lets you
  run fast enough to jump huge gaps, **timed buttons** (yellow) that hold a door open for only a few seconds,
  **mirror cubes** that bend laser beams, and chained **gel pads** to bounce up like stairs.
- Nine new puzzle rooms built on them (plate hop, carrying a cube on a plate or gel pad, sprint gap, bank shot,
  clockwork door, grid-wipe crossing, gel stairs). They also appear in Random Test.
- Each chamber family now has its own wall colour.

### Changed
- **Chambers 22-50 are rebuilt** and ordered by real difficulty. New ideas are introduced every few chambers
  instead of the same rooms in new orders.
- **Random Test is far more varied**: it draws from 21 room types, never repeats a room within a chamber, and the
  harder rooms only unlock as your streak grows. Runs can reach seven rooms.

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
