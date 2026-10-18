# Portal 2D

A side-scrolling, 2D take on the portal puzzle genre, written in C# with [raylib](https://www.raylib.com/) (via Raylib-cs).
It has 12 test chambers, see-through portals, momentum flings, cubes, buttons, toxic goo, emancipation grids, lasers and glass.
All of the art and sound is generated in code, so the game ships no sprite or audio files.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Portal2D -c Release
```

To build a double-clickable macOS app (written to `dist/Portal 2D.app`):

```bash
./scripts/make-mac-app.sh
```

## Controls

| Input | Action |
|---|---|
| A / D | Move |
| W / Space | Jump (hold for a higher jump) |
| Left mouse | Fire the blue portal |
| Right mouse | Fire the orange portal |
| E | Pick up / drop a cube |
| R | Restart the chamber |
| H | Show / hide hints |
| Esc | Pause |
| F11 | Toggle fullscreen |

## How the portals work

**One transform drives everything.** A portal sits on the face of two white wall tiles and has a centre, an outward
normal and an in-plane "up" axis. The map from portal A to portal B sends `normal(A) → -normal(B)` and `up(A) → up(B)`.
Wall portals keep world-up, so you never come out upside down. Floor and ceiling portals keep world-right, so if you
fall into a floor portal while moving right, you keep moving right when you come out.
This same transform (`Core/Portal.cs`) is used for:

- **Teleporting:** when a body's centre crosses the portal plane, its position *and velocity* go through the map.
  That keeps momentum ("speedy thing goes in, speedy thing comes out"), so you can fling yourself.
- **Passing through walls:** the two tiles behind a linked portal stop being solid for a body that lines up with
  the opening, so you sink into the portal smoothly instead of snapping.
- **Lasers:** a beam that hits a portal face continues from the mapped point in the mapped direction.
- **Rendering:** see below.

**Seeing through portals.** For each portal X (paired with Y), the whole world is drawn a second time into an
off-screen texture with every vertex pushed through the Y → X transform. That puts the space in front of Y exactly
where it would appear behind X if X were a hole. The game then works out the visibility cone from your eyes through
X's opening and draws that cone, textured with the off-screen image and fading with distance. Bodies that are halfway
through a portal also get a clone drawn coming out of the other side. You can turn the effect off in Options.

## Project layout

```
src/Portal2D/
  Core/      math helpers, portal transform
  Game/      world simulation, physics, levels, audio synth, save data, app + screens
  Render/    procedural renderer (tiles, characters, portals, see-through cones, particles)
  UI/        immediate-mode UI kit, mechanic icons
  Testing/   headless bot + reference solutions for every chamber
```

Levels are built in code in `Game/Levels.cs` using a small rectangle-fill builder. The tile legend is at the top of that file.

## Tests

Every chamber has a scripted reference solution. The bot plays it through the real physics without opening a window:

```bash
dotnet run --project src/Portal2D -- --test        # all chambers
dotnet run --project src/Portal2D -- --test -v --level 9   # trace one chamber
dotnet run --project src/Portal2D -- --dump        # print level maps as ASCII
```

Progress and settings are saved to `~/.config/Portal2D/save.json` on macOS and Linux, and to `%APPDATA%\Portal2D` on Windows.

Font: Rajdhani (SIL Open Font License, see `assets/fonts/OFL.txt`).
