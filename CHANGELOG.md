# Changelog

All notable changes to Portal 2D are listed here. Versions follow [semantic versioning](https://semver.org).

## [2.20.0] - 2026-10-28

### Changed
- **The Playground is a real world now.** It is generated from noise instead of repeating one template: wetlands, meadows, forests, deserts and
  snowy mountains blend into each other, with lakes, caves, lava at the bottom and a day/night and weather cycle. Rivers of caves open into
  sinkholes you can drop into and ride a lift back out of.
- **Portal toys to play with.** Gel bounce hills, orange-gel runways over pits, lift towers, lab ruins with a gel pad under a high ledge, and
  **launch towers**: ride the lift to the top, put a portal on the tower wall and one on the ground, step off, and the fall flings you across a ravine.
  Glowing **relics** are hidden on all of them and in the crystal caves; a pointer shows the nearest one, your count is saved, and long portal flights are
  measured as a fling record.
- **A new main menu.** The live game footage in the background is gone. It is a clean, static blueprint backdrop with a pair of big portals and a cube
  passing through, one clear list of modes, and the same look on the other menu screens.

### Fixed
- The new menu and the playground's audio no longer break (a mistyped call and a missing panel function from the unfinished work are fixed).

## [2.19.0] - 2026-10-28

### Changed
- **Fight mode is now a boss fight.** The evil-twin duel is gone. You face **COLOSSUS**, a giant mech in a pit between two ledges, and your portal gun
  is the only thing that can hurt it. It lobs mortar shells at where you stand and shows a red marker where each will land. When it fires, the hatch on
  top of its head vents open for a few seconds: put a blue portal on the floor at the marker and an orange one on the glowing ceiling panel above the
  mech, and its own shell drops straight down its own hatch. Cubes block shells.
- Three phases: shockwaves you have to jump, then a portal purge that fizzles your portals and twin shells. You have six hearts, get one back at each
  phase, and restart from the start of the phase you died in. Best time and victories are saved.
- The Main Menu button is now BOSS FIGHT.

### Fixed
- The refreshed main menu no longer crashes on drawing its cards, and the in-game audio no longer breaks from a mistyped method name.

## [2.18.0] - 2026-10-25

### Changed
- **Chambers 101-120 are far harder.** They are now long, dense gauntlets (8 to 17 linked challenges, wider than the Final Exam) and the exit
  is locked behind the weight circuits. New challenges include laser-and-door puzzles where you must put a portal where the beam hits,
  timed doors you have to sprint to with portals set up in advance, launch-plate flings over huge pits, and sentry-guarded
  pits. Random tests use the same new challenges and get longer, up to nine linked challenges.
- **Fight mode reworked.** A smaller arena so you always see everything, floating health and energy bars over each fighter, damage numbers,
  a 3-2-1 countdown, bigger bolts and hit flashes, a controls reminder, and a calmer start (the evil twin is slower to open fire in
  early matches). The twin now climbs between platforms to chase you, hops pits and avoids lifts.

### Fixed
- The best-run ghost no longer glides through walls when your run used a portal: it now snaps across.

## [2.17.0] - 2026-10-25

### Added
- **Export and import your progress.** Options now has EXPORT PROGRESS and IMPORT PROGRESS. The file holds your best times, medals,
  best-run ghosts, records, settings, character and your own maps. Importing merges it in: faster times and better medals win,
  and maps you didn't already have are added, so it is safe to import on a second device.

### Changed
- The version number now only shows on the main menu.

## [2.16.0] - 2026-10-25

### Added
- **Fight mode.** Duel your evil twin in randomly generated arenas, first to three rounds. Use your portals against it and the
  arena: fire energy bolts (Q) that fly through portals and bounce off mirror cubes, stomp on its head, fling cubes at it, and
  knock it into goo or into a sentry's sights. Grab health, rapid-fire and shield pickups. Each round can change the rules (low gravity,
  ricochet bolts, sudden death), and the twin gets smarter every match you win, even opening portals to ambush you.
- **20 new chambers (101-120)** built around sentry turrets, lift beams and blue gel, ramping up to a chamber that uses everything.
- **Far more varied random tests.** Hundreds of new tests chain two to six challenges (pits, vaults, button doors, lifts, gel, sentries
  and more) and are mixed into every difficulty. Each one has a built-in solution, so Watch Solution works in random tests too.
- Press **F8** in a chamber to record your own run as a solution.

### Fixed
- Watch Solution's camera now follows the ghost instead of staying on you, and the ghost's portals and cubes are visible.

## [2.15.0] - 2026-10-25

### Added
- **Watch Solution.** The pause menu can replay a chamber's reference solution as a ghost that walks the route for you.
  Runs that use it don't earn a medal.
- **Par times and medals.** Every chamber has a par time. Beat it for gold, come within half again for silver, or
  finish for bronze. Medals show on the chamber select screen and on the results screen.
- **Best-run ghost.** Race a translucent copy of your fastest run. Press G to hide or show it.
- **Sentry turrets**, **lift beams** and **push beams** in the map builder (under Lasers and Hazards). Turrets lock on, charge
  and fire when they can see you, so break their line of sight with a wall or a cube. Beams carry you and cubes up or down.
- The map builder's tool palette scrolls with the mouse wheel.

### Changed
- **Light shafts and dust.** Lamps now cast volumetric shafts that stop where they hit the floor, with dust drifting through.
- Glass panes glint, and lights leave glossy reflections on the floor beneath them.
- The camera leads toward where you are heading and looks down when you fall fast.
- Heavy landings and very fast portal exits shake the screen and freeze the action for a split second.
- **Reactive audio.** A hum rises as you near laser beams and portals, wind builds with your speed, and the whole mix
  muffles and clears after a portal trip, with the whoosh pitched by your exit speed.

## [2.14.0] - 2026-10-25

### Changed
- **Portal animations.** A new portal opens as a thin slit that stretches to full length and then swells to full
  thickness, with a flash of light and an expanding ring of energy.
- Passing through a portal now pulses both ends: the lens swirls faster and brightens, the rim flashes white, rings
  ripple out, and the portals light up the room. You come out the other side with speed streaks, and you squash and
  stretch along the direction you fly.
- Replaced or cleared portals collapse and fizzle away instead of vanishing instantly.

## [2.13.1] - 2026-10-25

### Changed
- Softer light everywhere. Shadows now fade out with soft penumbrae instead of cutting off, and every glow
  (portals, lamps, lasers, the exit, doors) falls off smoothly with no visible rim.
- Chambers are a little brighter so the lit areas read better while still feeling moody.

## [2.13.0] - 2026-10-25

### Changed
- **Moody, dynamic lighting.** Chambers are now dark, and only real lights brighten them. Lamps, portals, lasers, laser
  impacts, receivers, lit buttons, door lights, fizzlers, goo, the exit and charged cubes all cast light that walls,
  cubes and you block, so you throw shadows and light pools spill around corners. Shadows are soft-edged, and a
  faint glow keeps you visible in dark corners. Portals wash the floor and walls blue or orange.
- Laser beams are brighter and more intense, with a wide hot bloom and a larger white-hot flare where they hit.
- The holographic field on carried cubes is subtler.

### Fixed
- Removed the glowing blobs that travelled along laser beams.

## [2.12.0] - 2026-10-25

### Changed
- **Real lighting.** Portals, lamps, laser beams, laser impacts, receivers, buttons, emitters and the exit now cast
  light that is blocked by walls, so glows spill across floors and leave true shadows behind corners.
- Lasers have a soft bloom, energy pulses racing along the beam and a white-hot impact flare.
- Held cubes (and cubes resting on buttons) are wrapped in a holographic field instead of a blue box: a blurred halo,
  scanlines, a sweeping light band, brief glitch ghosting and corner brackets.
- Fizzlers glow into the room around them.

## [2.11.0] - 2026-10-24

### Changed
- Rebuilt the 47 expansion chambers. New puzzles include retrieving two cubes from separate areas,
  changing a beam's purpose after collecting cargo, aligning two mirrors, and carrying cargo through a bounce route.
  Longer chambers combine these with earlier mechanics across galleries and alternating floors.
- Reordered all 100 chambers into lessons, circuit puzzles and longer challenges. **Final Exam is now chamber 100.**
- Multi-stage airlocks require completed weight circuits to stay powered. A circuit counter and locked-door
  indicators show what the exit still needs.
- Rebuilt all 540 Random Tests into two-, three- and four-stage challenges. Higher streaks require multiple
  persistent circuits, and selection avoids repeating mechanics from recent tests.
- Hints appear automatically only in the first five lessons; press H when you want help later.

### Fixed
- Exit doors fit their openings, with readable signs at the normal pixel scale.
- Removed the fractional chamber zoom that distorted text and sprites. Moving the pointer toward the screen edge
  lets you inspect tall rooms and distant portal surfaces.
- Corrected blocked floor transitions, unsafe entries, missing exit floors and reflected laser sightlines in the
  rebuilt layouts. Every exported map has a winning reference replay in the browser physics.
- Original chamber best times survive the reorder; rebuilt chambers start with fresh records.

## [2.10.0] - 2026-10-24

### Added
- **100 test chambers.** 47 new chambers take you through tall atriums, switchbacks, flooded islands,
  freight lifts, beam windows, bridges, timed climbs, bounce shafts and cargo gantries. Expect routes that
  double back, cubes that need a return trip, and exits above or behind your starting point.
- **540 Random Test layouts**, with twelve new spatial puzzle families alongside handcrafted challenges
  and short combinations of the full set of room types. Higher streaks mix tougher mechanics without
  turning every test into a longer corridor.

### Changed
- New portal puzzles keep their floor, ceiling and distant portal panels in view so you can aim the whole route.
- Random Test avoids recently played puzzle families when it can, and plays every layout in a tier before
  repeating one. The original chambers keep their names, progress and best times.

## [2.9.1] - 2026-10-23

### Changed
- **A calmer main menu.** The three main actions (Continue, Random Test, Map Builder) stand out, and Chambers, Options
  and Controls sit in one quiet row beneath them. The background is dimmer behind the menu, the tagline no longer
  sits on the laser, and the button captions are shorter.

## [2.9.0] - 2026-10-23

### Added
- **Character customiser.** OPTIONS now has CUSTOMISE CHARACTER: 8 colours, 11 hats (cap, top hat, crown, party hat,
  wizard hat, cowboy hat, beanie, propeller cap, chef hat, halo), face accessories (shades, glasses, moustache, blush)
  and outfits (scarf, bow tie, tie, stripes, cape), plus a RANDOM button.
- **Fullscreen button** in the corner of the page, so you don't have to dig through menus. F11 still works.
- **HUD fades out of the way.** The chamber tag, timer, hints and intro card drop to 20% opacity while your cursor is
  over them, so they never hide the action.
- The site icon is now the little character.

### Changed
- The web game is now the main version. The C# desktop version has moved to `OLD-DESKTOP/` and is frozen.
- In a window the game is drawn at a crisp whole-number scale again; fullscreen fills the screen.

## [2.8.1] - 2026-10-23

### Fixed
- The web version now fills your whole browser window instead of sitting in a small box.

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
