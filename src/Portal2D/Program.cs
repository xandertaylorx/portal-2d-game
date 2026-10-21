using Portal2D.Game;
using Portal2D.Testing;

namespace Portal2D;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--test")) return TestRunner.Run(args.Contains("-v"), args.SkipWhile(a => a != "--level").Skip(1).Select(int.Parse).FirstOrDefault() - 1);
        if (args.Contains("--dump"))
        {
            for (int i = 0; i < Levels.All.Length; i++)
            {
                Console.WriteLine($"== {i + 1:00} {Levels.All[i].Name}");
                foreach (var row in Levels.All[i].Map) Console.WriteLine(row);
            }
            return 0;
        }
        if (args.Contains("--proctrace"))
        {
            // --proctrace Room [hard] [seed]: one room on its own, with a frame-by-frame trace of the bot
            int ti = Array.IndexOf(args, "--proctrace");
            var room = Enum.Parse<Procgen.Room>(args[ti + 1]);
            bool hardRoom = args.Contains("hard");
            int traceSeed = args.Skip(ti + 2).Select(a => int.TryParse(a, out var n) ? n : -1).FirstOrDefault(n => n > 0);
            var r = Procgen.Compose([new Procgen.Spec(room, hardRoom)], traceSeed > 0 ? traceSeed : 1, "trace", "", null);
            foreach (var row in r.Def.Map) Console.WriteLine(row);
            var w = new World(r.Def); var bot = new Testing.Bot(w);
            using var e = r.Solution(bot).GetEnumerator();
            for (int f = 0; f < 60 * 40; f++)
            {
                Testing.TestRunner.StepFrame(w, e.MoveNext() ? e.Current : bot.In());
                var pl = w.Player!;
                if (f % (args.Contains("fine") ? 3 : 15) == 0) Console.WriteLine($"t={f / 60f:0.00} p=({pl.Pos.X / 32:0.0},{pl.Pos.Y / 32:0.0}) v=({pl.Vel.X:0},{pl.Vel.Y:0}) g={pl.OnGround}");
                if (pl.Dead || pl.Won) { Console.WriteLine(pl.Dead ? "DEAD" : "WON"); break; }
            }
            return 0;
        }
        if (args.Contains("--scores"))
        {
            _ = Levels.All;
            int n = 22;
            foreach (var (name, rooms, score) in Levels.ComposedInfo) Console.WriteLine($"{n++,3} {name,-16} rooms={rooms} score={score}");
            return 0;
        }
        int ft = Array.IndexOf(args, "--featured");
        if (ft >= 0) return Featured.Build(args[ft + 1]);
        if (args.Contains("--procdiag")) { Console.Write(Procgen.Diagnose(20)); return 0; }
        int pg = Array.IndexOf(args, "--procgen");
        if (pg >= 0)
        {
            int count = int.Parse(args[pg + 1]);
            int ok = 0, totalAttempts = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int seed = 1; seed <= count; seed++)
            {
                var r = Procgen.Generate(seed, seed % 15);
                bool good = Procgen.Verify(r);
                if (good) ok++;
                totalAttempts += r.Attempts;
                if (args.Contains("-v") || !good) Console.WriteLine($"seed {seed}: {(good ? "ok" : "FAILED")} attempts={r.Attempts} width={r.Def.Map[0].Length}");
            }
            Console.WriteLine($"{ok}/{count} generated levels verified, avg attempts {totalAttempts / (float)count:0.0}, {sw.ElapsedMilliseconds / (float)count:0} ms each");
            return ok == count ? 0 : 1;
        }
        int pv = Array.IndexOf(args, "--preview");
        if (pv >= 0)
        {
            var def = args[pv + 1] == "menu" ? Levels.Menu
                : args[pv + 1].StartsWith("r") ? Procgen.Generate(int.Parse(args[pv + 1][1..]), 6).Def
                : Levels.All[int.Parse(args[pv + 1]) - 1];
            Render.Preview.Level(new World(def), 0.5f).Export(args[pv + 2], 2);
            return 0;
        }
        int bn = Array.IndexOf(args, "--banner");
        if (bn >= 0) { Render.Preview.Banner().Export(args[bn + 1], 2); return 0; }
        int sp = Array.IndexOf(args, "--sprites");
        if (sp >= 0) { Render.Preview.SpriteSheet().Export(args[sp + 1], 4); return 0; }
        int rt = Array.IndexOf(args, "--resolve-app");
        if (rt >= 0) { Console.WriteLine(Updater.OriginalBundlePath(args[rt + 1]) ?? "(unresolved)"); return 0; }
        int su = Array.IndexOf(args, "--selftest-update");
        if (su >= 0)
        {
            var u = new Updater();
            u.StageLocal(args[su + 1]);
            Console.WriteLine(u.LaunchSwapAndQuit() ? "swap launched" : "swap failed: " + u.Error);
            return 0;
        }
        new App(args).Run();
        return 0;
    }
}
