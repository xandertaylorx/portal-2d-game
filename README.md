# Portal 2D

A pixel-art, side-scrolling take on the portal puzzle genre, written in C# with [raylib](https://www.raylib.com/) (via Raylib-cs).
You play a small terracotta critter with a portal gun.

- **18 handcrafted test chambers**: momentum flings, cubes, buttons, toxic goo, emancipation grids, lasers, glass and blue repulsion gel.
- **Random Test mode**: endless, procedurally generated chambers. Every generated level is played by a bot before you get it, so it is always solvable.
- **Portal windows**: a shader cuts a swirling, dithered hole behind each portal so you can see the other side.
- All art, the font and the sound are generated in code, so the game ships no asset files.

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

**Portal windows.** For each portal X (paired with Y), the whole world is drawn a second time into an off-screen
texture with every vertex pushed through the Y → X transform. That puts the space in front of Y exactly where it
would appear behind X if X were a hole. A fragment shader then cuts a half-ellipse "hole" into the wall behind X and
samples that texture through it. Near the rim it adds a swirling refraction, a dithered glowing edge and scanlines.
The hole opens wider as you walk closer. Bodies that are halfway through a portal also get a clone drawn coming out
of the other side. You can turn the effect off in Options.

## Pixel art pipeline

The game renders to a 640×360 canvas (16 px tiles) and scales it up with nearest-neighbour filtering, by a whole
number where the window allows. Every sprite (the hero, cube, portals, exit) and every tile is painted pixel by
pixel into a CPU buffer (`Render/Pix.cs`). Each level's back wall and solid tiles are baked into two textures, with
auto-tiled edges, rounded corners, panel seams and drop shadows. The UI uses a hand-made 5×7 bitmap font.
You can preview the art without opening a window:

```bash
dotnet run --project src/Portal2D -- --preview 9 out.png    # level 9 as a PNG (r123 = random seed 123)
dotnet run --project src/Portal2D -- --sprites out.png      # sprite sheet
./scripts/shots.sh shots                                    # real in-game screenshots (needs a display)
```

## Random Test mode

`Game/Procgen.cs` builds a row of rooms. Each room is a randomised puzzle archetype: goo crossing, high ledge,
cube carried to a button, laser redirect, fling, laser "umbrella", or gel bounce. Each archetype also writes out
the inputs that solve it. The finished level is then played headlessly with those inputs, using the real physics,
and only kept if the bot reaches the exit; otherwise the next sub-seed is tried. Each level after the first adds
more rooms, up to five.

```bash
dotnet run --project src/Portal2D -- --procgen 50          # generate and verify 50 seeds
```

## Project layout

```
src/Portal2D/
  Core/      math helpers, portal transform
  Game/      world simulation, physics, levels, audio synth, save data, app + screens
  Render/    pixel buffers, sprite + tile generators, bitmap font, renderer, portal-window shader
  UI/        immediate-mode pixel UI kit, mechanic icons
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

