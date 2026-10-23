using System.Text;
using System.Text.Json;
using Portal2D.Game;

namespace Portal2D.Testing;

/// <summary>
/// Dumps every chamber (and a pool of verified Random Test chambers) to JSON for the browser version at the repo root.
/// The web game never has to re-implement the level generators or the bots: it plays the exact verified maps.
///   dotnet run --project OLD-DESKTOP/src/Portal2D -c Release -- --export-web data
/// </summary>
public static class WebExport
{
    const int PerDifficulty = 16, Difficulties = 15;

    static object Def(LevelDef d, int seed = 0) => new
    {
        name = d.Name,
        map = d.Map,
        gun = (int)d.Gun,
        blue = new[] { d.BlueDir.X, d.BlueDir.Y },
        orange = new[] { d.OrangeDir.X, d.OrangeDir.Y },
        hints = d.Hints,
        fling = d.Fling,
        label = d.Label,
        theme = d.Theme,
        seed
    };

    /// <summary>Plays a reference solution headlessly and records the exact inputs, so the JS port can replay and compare.</summary>
    static object Trace(LevelDef def, Func<Bot, IEnumerable<InputState>> solution)
    {
        var w = new World(def);
        var bot = new Bot(w);
        using var e = solution(bot).GetEnumerator();
        var sb = new StringBuilder();
        string last = "";
        int run = 0, frames = 0;
        void Flush() { if (run > 0) sb.Append(last).Append('x').Append(run).Append(';'); }
        bool won = false;
        for (float t = 0; t < 90; t += 1 / 60f)
        {
            var i = e.MoveNext() ? e.Current : bot.In();
            int flags = (i.JumpPressed ? 1 : 0) | (i.JumpHeld ? 2 : 0) | (i.UsePressed ? 4 : 0) | (i.FireBlue ? 8 : 0) | (i.FireOrange ? 16 : 0) | (i.FireCycle ? 32 : 0);
            string tok = FormattableString.Invariant($"{i.MoveX},{flags},{i.AimWorld.X:R},{i.AimWorld.Y:R}");
            if (tok == last) run++; else { Flush(); last = tok; run = 1; }
            TestRunner.StepFrame(w, i);
            frames++;
            if (w.Player!.Won) { won = true; break; }
            if (w.Player.Dead) break;
        }
        Flush();
        return new { won, frames, inputs = sb.ToString() };
    }

    public static int Build(string dir)
    {
        Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = false };

        var chambers = Levels.All.Select(l => Def(l)).ToList();
        File.WriteAllText(Path.Combine(dir, "levels.json"),
            JsonSerializer.Serialize(new { menu = Def(Levels.Menu), chambers }, opts));
        Console.WriteLine($"levels.json: {chambers.Count} chambers");

        // reference-solution traces: the JS port replays these to prove it behaves like the C# game (kept out of the site)
        var traces = new List<object>();
        for (int i = 0; i < Levels.All.Length; i++)
            traces.Add(new { name = Levels.All[i].Name, trace = Trace(Levels.All[i], bot => Solutions.For(i, bot)) });

        var pools = new List<List<object>>();
        int made = 0;
        for (int d = 0; d < Difficulties; d++)
        {
            var pool = new List<object>();
            for (int i = 0; i < PerDifficulty; i++)
            {
                int seed = 1000 + d * 100 + i;
                var r = Procgen.Generate(seed, d);
                if (!Procgen.Verify(r)) { Console.WriteLine($"seed {seed} (difficulty {d}) did not verify, skipped"); continue; }
                pool.Add(Def(r.Def, seed));
                if (i == 0) traces.Add(new { name = r.Def.Name, random = new[] { d, 0 }, trace = Trace(r.Def, r.Solution) });
                made++;
            }
            pools.Add(pool);
        }
        File.WriteAllText(Path.Combine(dir, "random.json"), JsonSerializer.Serialize(new { pools }, opts));
        var testDir = Path.Combine(dir, "..", "test");
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "traces.json"), JsonSerializer.Serialize(traces, opts));
        Console.WriteLine($"random.json: {made} verified chambers across {Difficulties} difficulties");
        return 0;
    }
}
