# Web chamber authoring

This is the maintained C# level exporter for the browser game. It references the frozen desktop assembly for
`LevelBuilder`, physics, bots and the original 53 chambers; it does not modify or add features to the desktop game.
Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project tools/LevelForge -c Release -- data
node tests/content.mjs
node tests/replay.mjs
```

`Puzzles.cs` authors twelve spatial puzzle recipes with four campaign layouts apiece (the final recipe has three).
They are interleaved to make 47 additional chambers. Parameters change dimensions, shelf heights, sightlines,
travel distances and direction. The first 53 chamber names are preserved because saves use names as keys.
`--check` checks only the additions, including any reflected solutions, without writing data.

`Program.cs` records the actual inputs needed to win each chamber. Random Test gets 36 distinct layouts per tier:
24 spatial puzzles, two complete handcrafted challenges, and ten short sequences drawn from the 28 reference room
types. Each tier includes all twelve new families. Spatial recipes vary dimensions beyond their campaign versions;
reflections reverse map tiles, directional devices, fixed portal normals and recorded inputs. Reflections are
simulated again and discarded if they fail. Invalid sightlines, incompatible room entrances, losing solutions and
duplicate random layouts are rejected. Harder sequence mechanics enter as the streak increases, with at most three
rooms per sequence. Generation fails if a slot cannot be filled, rather than exporting an unsolved fallback.

The output is `data/levels.json`, `data/random.json`, and gitignored `test/traces.json`. Replays cover every exported
campaign chamber and every random layout. The legacy `--export-web` command in OLD-DESKTOP still exports only the
frozen original content; use this tool when updating the maintained browser game.
