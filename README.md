<p align="center">
  <img src="docs/media/banner.png" alt="Portal 2D" width="100%">
</p>

<p align="center">
  <b>A pixel-art, side-scrolling portal puzzler that runs in your browser, written from scratch.</b><br>
  100 test chambers with varied puzzle routes, an endless procedurally generated mode,
  and portals you can actually see through.
</p>

<p align="center">
  <img alt="version" src="https://img.shields.io/badge/version-2.20.0-ff8a1f?style=flat-square">
  <img alt="JavaScript" src="https://img.shields.io/badge/plain-JavaScript-3fa7ff?style=flat-square">
  <img alt="platforms" src="https://img.shields.io/badge/play-in%20your%20browser-5ee08a?style=flat-square">
  <img alt="license" src="https://img.shields.io/badge/license-PolyForm%20Strict-d97757?style=flat-square">
</p>

<p align="center"><b><a href="https://portal.crooli.com/">▶ Play it in your browser</a></b> - no download, nothing to install.</p>

---

<table>
  <tr>
    <td width="50%"><img src="docs/media/fling.gif" alt="Momentum fling"><br><sub><b>Speedy thing goes in, speedy thing comes out.</b> Momentum is carried through the portals.</sub></td>
    <td width="50%"><img src="docs/media/lens.gif" alt="See-through portal lenses"><br><sub><b>Portal lenses.</b> Each linked portal shows a live, parallaxed view out of its partner.</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/media/laser.gif" alt="Laser redirect"><br><sub><b>Lasers travel through portals</b>, so you can redirect them into receivers.</sub></td>
    <td width="50%"><img src="docs/media/random.gif" alt="Random Test mode"><br><sub><b>Random Test:</b> endless generated chambers. The game proves every one is solvable before you play it.</sub></td>
  </tr>
</table>

## Features

- **100 chambers** built around momentum flings, weighted cubes and buttons, toxic goo,
  emancipation grids, lasers and receivers, glass, repulsion gel, launch plates, speed gel, timed
  buttons and mirror cubes. Eight new circuit puzzles require cargo retrieval, beam reuse, two-mirror alignment
  and timed portal routes. Another 39 authored combinations build into longer galleries and alternating floors,
  with weight circuits that must stay powered to unlock the final airlock. Lessons come first, hints become
  optional after the opening five, and **Final Exam is chamber 100**. Original chamber records survive the reorder.
- **Map builder**: design your own chambers with a point-and-click editor. Paint walls, goo, gel, lasers, cubes,
  buttons and doors, wire doors to buttons by colour, undo and redo, then test play it. Maps save on your device
  automatically, and you can share one with a friend as a single line of text.
- **Random Test mode**: 540 verified challenges across 15 streak tiers. Two-stage tests grow into three- and
  four-stage routes; advanced tests require multiple completed circuits. The picker avoids recent opening
  puzzles and minimizes repeated mechanics across entire tests, while exhausting each tier before repeating a map.
  The game tracks your best streak.
- **Portal lenses**: a fragment shader turns each linked portal into a fish-eye window onto the other side, with
  parallax, ripples and a dithered pixel rim.
- **Everything is generated in code**: every sprite, tile, the bitmap font and every sound effect. The game ships no
  art or audio files.
- **Character customiser**: pick a colour, a hat, a face accessory and an outfit in OPTIONS. Your look follows you
  through every chamber, and through the portals.
- **No install**: it runs in any modern browser. A fullscreen button sits in the corner, and the HUD fades out of the
  way when your cursor goes near it.
- Coyote time, jump buffering, an aim preview that shows where a portal will land, best times, and a pause menu
  with options for volume, screen shake, portal lenses and fullscreen.

<p align="center">
  <img src="docs/media/menu.png" width="49%" alt="Main menu">
  <img src="docs/media/select.png" width="49%" alt="Chamber select">
  <img src="docs/media/l13.png" width="49%" alt="Two Weights">
  <img src="docs/media/complete.png" width="49%" alt="Chamber complete">
</p>

## Play

**[xandertaylorx.github.io/portal-2d-game](https://xandertaylorx.github.io/portal-2d-game/)**: the whole game,
including the map builder, Random Test and featured maps. It needs a keyboard and mouse, so open it on a computer.
Progress, your maps and your character are saved in the browser. Maps are shared as links that open straight into the
game.

> The original C# desktop version is frozen in [`OLD-DESKTOP/`](OLD-DESKTOP/). Its builds are still on the
> [Releases page](https://github.com/xandertaylorx/portal-2d-game/releases) but get no further updates. Map codes are
> the same in both, so a map made in either plays in the other.

### Controls

| Input | Action |
|---|---|
| `A` / `D` | Move |
| `W` / `Space` | Jump (hold for higher) |
| Left mouse | Fire the blue portal |
| Right mouse | Fire the orange portal |
| `E` | Pick up / drop a cube |
| `R` | Restart the chamber |
| `H` | Show / hide hints |
| `Esc` | Pause |
| `F11` | Fullscreen |

## How it works

### One transform for everything
A portal sits on the face of two white tiles and has a centre, an outward normal and an in-plane "up" axis.
Going from portal A to portal B sends `normal(A) → -normal(B)` and `up(A) → up(B)`. Wall portals keep world-up, so
you never come out upside down. Floor and ceiling portals keep world-right, so the direction you were moving is kept.
That single affine map (`Portal` in `src/core.js`) drives:

- **Teleporting:** position *and velocity* go through the map, which is where flings come from.
- **Sinking in:** the tiles behind a linked portal stop being solid for anything lined up with the opening.
- **Lasers:** a beam that hits a portal face continues out of the other one.
- **Rendering:** the world is drawn a second time through the map into an off-screen texture, so the space in front
  of one portal lines up behind the other.

### Portal lenses
When both portals exist, each one swells into an oval lens. A per-pixel pass samples the off-screen view behind the
portal, so it shows what is in front of the partner portal. It squeezes that view into the lens with a fish-eye
curve, shifts it with parallax based on where you stand, and adds ripples, a swirl and a dithered, sparkling rim.
Characters passing through are redrawn on the room side, so they never vanish behind the lens.

### Pixel-art pipeline
The game renders to a 640×360 canvas (16 px tiles), scaled up with nearest-neighbour filtering by a whole number.
Tiles and sprites are painted pixel by pixel into CPU buffers (`Pix` in `src/gfx.js`). Each level's back wall and solid
tiles are baked into two textures, with auto-tiled outlines, rounded corners, panel seams, ambient occlusion and a
painted chamber number.

### Every level is proven solvable
The C# bots in `OLD-DESKTOP/src/Portal2D/Testing/` drive the real physics with scripted inputs. Every handcrafted chamber has a reference solution,
and `--test` plays all of them headlessly. `tests/replay.mjs` replays those solutions through the JavaScript physics.
The web authoring tool in `tools/LevelForge/` references that frozen C# simulation and adds the new
puzzles without changing the desktop game. It records a winning solution for **all 100 campaign chambers and all
540 random tests**; the JavaScript replay test checks every one before shipping. Unsolved candidates are rejected
rather than exported.

<p align="center"><img src="docs/media/sprites.png" width="70%" alt="Sprites"></p>

## Running and changing it

No build step and no dependencies: it is plain JavaScript modules on a 640×360 canvas.

```bash
python3 -m http.server 8000          # then open http://localhost:8000
dotnet run --project tools/LevelForge -c Release -- data  # export levels + all reference traces
node tests/content.mjs               # check map diversity and random selection cycles
node tests/challenges.mjs            # progression, saves, exit locks and shortcut attempts
node tests/replay.mjs                # replay all 640 reference solutions through the JS physics
node tools/make-favicon.mjs          # regenerate favicon.png from the hero sprite
```

Open `http://localhost:8000/tests/browser-smoke.html` for campaign, random and custom-map rendering checks
and an exit-sign contact sheet.

`?level=N` and `?screen=select|options|controls|character|mymaps|editor` deep-link to a chamber or screen.
A push to `main` that touches the game files is deployed to GitHub Pages by `.github/workflows/pages.yml`.

```
index.html   page shell, fullscreen button
src/         game code: world.js (physics), render.js (scene + portal lenses), app.js (screens), editor.js (map builder),
             art.js (sprites, tiles, character customiser), audio.js (synthesised sound), maps.js (saves, share codes)
data/        the 100 chambers + 540 verified Random Test layouts (JSON)
tests/       physics replays, content/selection, challenge safety, and browser rendering checks
tools/LevelForge/ C# web level authoring + export, using the frozen reference physics
OLD-DESKTOP/ the original C# version and reference simulation (see its README)
```

## License

Copyright © 2026 xandertaylorx. Released under the **[PolyForm Strict License 1.0.0](LICENSE.md)**.

You may download, play and study the game for free, for any **non-commercial** purpose. You may **not** sell it,
use it commercially, modify it, or redistribute the code or builds without written permission. All rights not
granted are reserved.

*Portal 2D is an independent fan project. It is not affiliated with or endorsed by Valve Corporation. "Portal" is a
trademark of Valve Corporation.*
