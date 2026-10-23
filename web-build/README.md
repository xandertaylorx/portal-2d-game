# Portal 2D, web build

The browser version of Portal 2D: plain JavaScript modules on a 640x360 canvas, no bundler and no dependencies.
See the main [README](../README.md#the-web-build) for how it relates to the C# game.

```
index.html      page shell
src/            game code (world.js = physics, render.js = scene + portal lenses, app.js = screens, editor.js = map builder)
data/           chambers + Random Test pool, exported from the C# game (dotnet run ... --export-web web-build/data)
tests/          node tests/replay.mjs replays the C# reference solutions through the JS physics
```
