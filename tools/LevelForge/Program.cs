using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Numerics;
using Portal2D.Core;
using Portal2D.Game;
using Portal2D.Testing;

static class Program
{
    static bool Verbose;
    record Trace(bool won, int frames, string inputs);
    record Sample(LevelDef Def, Trace Trace, string Family, int Seed = 0);
    static Trace Record(LevelDef def, Func<Bot, IEnumerable<InputState>> solution)
    {
        var w = new World(def); var bot = new Bot(w);
        using var e = solution(bot).GetEnumerator();
        var sb = new StringBuilder(); string last = ""; int run = 0, frames = 0;
        void Flush() { if (run > 0) sb.Append(last).Append('x').Append(run).Append(';'); }
        while (frames < 10800) {
            var i = e.MoveNext() ? e.Current : bot.In();
            int flags = (i.JumpPressed ? 1 : 0) | (i.JumpHeld ? 2 : 0) | (i.UsePressed ? 4 : 0)
                | (i.FireBlue ? 8 : 0) | (i.FireOrange ? 16 : 0) | (i.FireCycle ? 32 : 0);
            string token = FormattableString.Invariant($"{i.MoveX},{flags},{i.AimWorld.X:R},{i.AimWorld.Y:R}");
            if (token == last) run++; else { Flush(); last = token; run = 1; }
            TestRunner.StepFrame(w, i); frames++;
            if (w.Player!.Won || w.Player.Dead) break;
        }
        Flush();
        if (!w.Player!.Won && Verbose) Console.WriteLine($"FAIL {def.Name}: at {w.Player.Pos} dead={w.Player.Dead} held={w.Player.Holding != null} tele={w.Player.Teleports} portals={string.Join("/", w.Portals.Select(p => p?.Center.ToString() ?? "none"))} cubes={string.Join("/", w.Cubes.Select(c => c.Pos.ToString()))} groups={string.Join(",", w.GroupActive)}");
        return new(w.Player.Won, frames, sb.ToString());
    }

    static object Data(Sample s) => new {
        name = s.Def.Name, map = s.Def.Map, gun = (int)s.Def.Gun,
        blue = new[] { s.Def.BlueDir.X, s.Def.BlueDir.Y }, orange = new[] { s.Def.OrangeDir.X, s.Def.OrangeDir.Y },
        hints = s.Def.Hints, fling = s.Def.Fling, label = s.Def.Label, theme = s.Def.Theme,
        seed = s.Seed, family = s.Family, layoutId = LayoutId(s.Def)
    };

    static string LayoutId(LevelDef d) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', d.Map) + $"|{d.Gun}|{d.BlueDir}|{d.OrangeDir}")))[..16];

    static Sample Mirror(Sample source)
    {
        char Flip(char c) => c switch { '>' => '<', '<' => '>', 'Q' => 'q', 'q' => 'Q', 'M' => 'N', 'N' => 'M', _ => c };
        var d = source.Def;
        var mirrored = new LevelDef { Name = d.Name, Map = d.Map.Select(row => new string(row.Reverse().Select(Flip).ToArray())).ToArray(),
            Gun = d.Gun, BlueDir = new(-d.BlueDir.X, d.BlueDir.Y), OrangeDir = new(-d.OrangeDir.X, d.OrangeDir.Y),
            Hints = d.Hints, Fling = d.Fling, Theme = d.Theme, Label = d.Label };
        IEnumerable<InputState> Inputs(Bot _) {
            foreach (string part in source.Trace.inputs.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                var run = part.Split('x'); var tok = run[0].Split(',');
                int flags = int.Parse(tok[1]);
                for (int n = 0; n < int.Parse(run[1]); n++) yield return new InputState {
                    MoveX = -int.Parse(tok[0]), JumpPressed = (flags & 1) != 0, JumpHeld = (flags & 2) != 0,
                    UsePressed = (flags & 4) != 0, FireBlue = (flags & 8) != 0, FireOrange = (flags & 16) != 0, FireCycle = (flags & 32) != 0,
                    AimWorld = new(d.Map[0].Length * 32 - float.Parse(tok[2]), float.Parse(tok[3]))
                };
            }
        }
        return new(mirrored, Record(mirrored, Inputs), source.Family, source.Seed);
    }

    static int Main(string[] args)
    {
        Verbose = args.Contains("--check");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var additions = new List<Sample>();
        // Interleave routes: no back-to-back puzzles from the same recipe. The last gallery is the finale.
        for (int v = 0; v < 4; v++) for (int f = 0; f < Puzzles.Families.Length; f++) {
            if (v == 3 && f == 11) continue;
            var def = Puzzles.Build(f, v);
            var trace = Record(def, def.Solution!);
            if (!trace.won) continue;
            var sample = new Sample(def, trace, Puzzles.Families[f]);
            if (v % 2 == 1) { var mirror = Mirror(sample); if (mirror.Trace.won) sample = mirror; }
            if (!sample.Trace.won) continue;
            additions.Add(sample);
            Console.WriteLine($"ok {def.Name} ({trace.frames} frames)");
        }
        if (additions.Count != 47) { Console.WriteLine($"Only {additions.Count}/47 new puzzles verified; export stopped."); return 1; }
        if (args.Contains("--check")) return 0;
        string dir = args.FirstOrDefault() ?? "data";
        var campaign = new List<Sample>();
        for (int i = 0; i < Levels.All.Length; i++) {
            int index = i; var def = Levels.All[i];
            var trace = Record(def, bot => Solutions.For(index, bot));
            if (!trace.won) throw new Exception($"Original solution failed: {def.Name}");
            campaign.Add(new(def, trace, "classic"));
        }
        campaign.AddRange(additions);
        if (campaign.Count != 100) throw new Exception($"Expected 100 chambers, got {campaign.Count}");
        for (int i = 0; i < campaign.Count; i++) campaign[i].Def.Label = (i + 1).ToString("D2");
        var pools = new List<List<Sample>>();
        var seen = new HashSet<string>(); int seed = 10000;
        for (int difficulty = 0; difficulty < 15; difficulty++) {
            var pool = new List<Sample>();
            for (int i = 0; i < 36; i++) {
                Sample? sample = null;
                for (int attempt = 0; attempt < 160 && sample == null; attempt++) {
                    int id = seed++;
                    if (i < 24) {
                        int family = (i + difficulty * 5) % 12;
                        int variant = 4 + difficulty * 2 + i / 12 + attempt;
                        var d = Puzzles.Build(family, variant, $"Random Test {id}");
                        var t = Record(d, d.Solution!);
                        if (t.won) sample = new(d, t, Puzzles.Families[family], id);
                    } else if (i < 26) {
                        // Whole handcrafted chambers add mirrors, cube shielding, speed jumps and flings.
                        int index = 21 + (difficulty * 7 + i + attempt) % 20;
                        var source = campaign[index];
                        var d = new LevelDef { Name = $"Random Test {id}", Map = source.Def.Map,
                            Gun = source.Def.Gun, BlueDir = source.Def.BlueDir, OrangeDir = source.Def.OrangeDir,
                            Hints = source.Def.Hints, Fling = source.Def.Fling, Theme = 1 + difficulty % 6 };
                        sample = new(d, source.Trace, "classic-" + source.Def.Name, id);
                    } else {
                        // Mix the full mechanic set without making every high streak a seven-room corridor.
                        var rng = new Random(id);
                        int count = 1 + difficulty / 5;
                        var rooms = Enum.GetValues<Procgen.Room>()
                            .Where(r => Procgen.Weight(new(r)) <= 2 + difficulty / 3)
                            .OrderBy(_ => rng.Next()).Take(count).Select(r => new Procgen.Spec(r, difficulty > 5 && rng.Next(2) == 0)).ToArray();
                        try {
                            var r = Procgen.Compose(rooms, id, $"Random Test {id}", "??", null);
                            var t = Record(r.Def, r.Solution);
                            if (t.won) sample = new(r.Def, t, "sequence", id);
                        } catch (InvalidOperationException) { /* Incompatible room entrances: try the next plan. */ }
                    }
                    if (sample != null && (i + difficulty + attempt + attempt / 20) % 2 == 1) {
                        var mirror = Mirror(sample); if (mirror.Trace.won) sample = mirror;
                    }
                    if (sample != null && (!sample.Trace.won || !seen.Add(LayoutId(sample.Def)))) sample = null;
                }
                if (sample == null) throw new Exception($"Could not fill random tier {difficulty}, slot {i}");
                sample.Def.Label = "??";
                pool.Add(sample);
            }
            pools.Add(pool);
            Console.WriteLine($"Random tier {difficulty}: {pool.Count} verified tests");
        }
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "levels.json"), JsonSerializer.Serialize(new {
            menu = Data(new(Levels.Menu, new(true, 0, ""), "menu")), chambers = campaign.Select(Data)
        }));
        File.WriteAllText(Path.Combine(dir, "random.json"), JsonSerializer.Serialize(new { pools = pools.Select(p => p.Select(Data)) }));
        var traces = campaign.Select(s => (object)new { name = s.Def.Name, trace = s.Trace }).ToList();
        for (int d = 0; d < pools.Count; d++) for (int i = 0; i < pools[d].Count; i++)
            traces.Add(new { name = pools[d][i].Def.Name, random = new[] { d, i }, trace = pools[d][i].Trace });
        string testDir = Path.GetFullPath(Path.Combine(dir, "..", "test")); Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "traces.json"), JsonSerializer.Serialize(traces));
        Console.WriteLine($"Exported {campaign.Count} chambers, {pools.Sum(p => p.Count)} random tests, {traces.Count} reference replays.");
        return 0;
    }
}
