using System.Text.Json;
using Portal2D.Game;

namespace Portal2D.Testing;

/// <summary>
/// Builds <c>featured/maps.json</c>: hand-picked room combinations from the generator, each exported as a map-builder
/// share code and re-verified through the custom-map path (so only maps the bot can really beat get featured).
/// </summary>
public static class Featured
{
    static readonly (string name, string note, int seed, Procgen.Spec[] rooms)[] Picks =
    [
        ("Step Up", "A GENTLE WARM-UP", 101, [new(Procgen.Room.Stool), new(Procgen.Room.Peephole), new(Procgen.Room.PlateHop)]),
        ("Special Post", "FLING AND RUN", 102, [new(Procgen.Room.Catapult), new(Procgen.Room.Sprint), new(Procgen.Room.Spotlight)]),
        ("Wading Pool", "MIND THE GOO", 103, [new(Procgen.Room.Moat), new(Procgen.Room.GelIsland)]),
        ("Gel Garden", "BOUNCE AND CARRY", 104, [new(Procgen.Room.GelStairs, true), new(Procgen.Room.GelCarry), new(Procgen.Room.Gel)]),
        ("Clock Tower", "TIMING IS EVERYTHING", 105, [new(Procgen.Room.Clockwork), new(Procgen.Room.BankShot)]),
        ("Long Way Round", "THREE ROOMS, THREE IDEAS", 106, [new(Procgen.Room.Spotlight, true), new(Procgen.Room.PlateCarry), new(Procgen.Room.Umbrella)]),
    ];

    public static int Build(string path)
    {
        var rows = new List<object>();
        foreach (var (name, note, seed0, rooms) in Picks)
        for (int seed = seed0; seed < seed0 + 25; seed++)
        {
            var r = Procgen.Compose(rooms, seed, name, "", null);
            var cm = new CustomMap { Name = name.ToUpperInvariant(), Map = r.Def.Map, Gun = (int)r.Def.Gun, Theme = r.Def.Theme };
            if (!cm.Sanitize()) { Console.WriteLine($"{name}: not a valid map"); continue; }
            var back = CustomMap.FromShareCode(cm.ToShareCode());
            if (back == null) { Console.WriteLine($"{name}: share code failed"); continue; }
            var def = back.ToDef();
            var w = new World(def);
            var bot = new Bot(w);
            using var e = r.Solution(bot).GetEnumerator();
            bool won = false;
            for (float t = 0; t < 240 && !won && !w.Player!.Dead; t += 1 / 60f)
            {
                TestRunner.StepFrame(w, e.MoveNext() ? e.Current : bot.In());
                won = w.Player!.Won;
            }
            Console.WriteLine($"{name}: {(won ? "ok" : "NOT BEATABLE, skipped")} ({back.W}x{back.H})");
            if (won) { rows.Add(new { name = cm.Name, author = "PORTAL 2D", note, code = cm.ToShareCode() }); break; }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { maps = rows }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
