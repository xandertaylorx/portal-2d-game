using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Portal2D.Core;
using Portal2D.Game;
using Portal2D.Testing;

static class Program
{
    static bool verbose;
    record Trace(bool won, int frames, string inputs);
    record Sample(LevelDef Def, Trace Trace, string Family, int Seed = 0, int[]? ExitGroups = null,
        int Difficulty = 0, string? RecordKey = null, string[]? Mechanics = null);

    // The web airlock is enabled at each simulation tick, using the same circuit state as the JS world.
    // No desktop physics or desktop campaign is modified.
    static Trace Record(LevelDef def, Func<Bot, IEnumerable<InputState>> solution, int[]? goals = null)
    {
        var w = new World(def); var bot = new Bot(w);
        using var e = solution(bot).GetEnumerator();
        var sb = new StringBuilder(); string last = ""; int run = 0, frames = 0;
        void Flush() { if (run > 0) sb.Append(last).Append('x').Append(run).Append(';'); }
        while (frames < 10800) {
            InputState i;
            try { i = e.MoveNext() ? e.Current : bot.In(); }
            catch (InvalidOperationException ex) { if (verbose) Console.WriteLine(ex.Message); break; }
            int flags = (i.JumpPressed ? 1 : 0) | (i.JumpHeld ? 2 : 0) | (i.UsePressed ? 4 : 0)
                | (i.FireBlue ? 8 : 0) | (i.FireOrange ? 16 : 0) | (i.FireCycle ? 32 : 0);
            string token = FormattableString.Invariant($"{i.MoveX},{flags},{i.AimWorld.X:R},{i.AimWorld.Y:R}");
            if (token == last) run++; else { Flush(); last = token; run = 1; }
            w.HasExit = (goals ?? []).All(g => w.GroupActive[g]); w.Update(K.SimDt, i);
            i.JumpPressed = i.UsePressed = i.FireBlue = i.FireOrange = i.FireCycle = false;
            w.HasExit = (goals ?? []).All(g => w.GroupActive[g]); w.Update(K.SimDt, i);
            w.Events.Clear(); frames++;
            if (w.Player!.Won || w.Player.Dead) break;
        }
        Flush();
        if (!w.Player!.Won && verbose)
            Console.WriteLine($"FAIL {def.Name}: at {w.Player.Pos}, dead={w.Player.Dead}, teleports={w.Player.Teleports}, circuits={string.Join(',', w.GroupActive)}");
        return new(w.Player.Won, frames, sb.ToString());
    }

    static IEnumerable<InputState> Inputs(string trace)
    {
        foreach (string part in trace.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            var run = part.Split('x'); var tok = run[0].Split(','); int flags = int.Parse(tok[1]);
            for (int n = 0; n < int.Parse(run[1]); n++) yield return new InputState {
                MoveX = int.Parse(tok[0]), JumpPressed = (flags & 1) != 0, JumpHeld = (flags & 2) != 0,
                UsePressed = (flags & 4) != 0, FireBlue = (flags & 8) != 0, FireOrange = (flags & 16) != 0,
                FireCycle = (flags & 32) != 0, AimWorld = new(float.Parse(tok[2]), float.Parse(tok[3]))
            };
        }
    }

    static string LayoutId(LevelDef d) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', d.Map) + $"|{d.Gun}|{d.BlueDir.X},{d.BlueDir.Y}|{d.OrangeDir.X},{d.OrangeDir.Y}")))[..16];

    static object Data(Sample s) => new {
        name = s.Def.Name, map = s.Def.Map, gun = (int)s.Def.Gun,
        blue = new[] { s.Def.BlueDir.X, s.Def.BlueDir.Y }, orange = new[] { s.Def.OrangeDir.X, s.Def.OrangeDir.Y },
        hints = s.Def.Hints, fling = s.Def.Fling, label = s.Def.Label, theme = s.Def.Theme,
        seed = s.Seed, family = s.Family, layoutId = LayoutId(s.Def), exitGroups = s.ExitGroups ?? [],
        difficulty = s.Difficulty, recordKey = s.RecordKey ?? s.Def.Name, tutorial = s.Difficulty == 0,
        mechanics = s.Mechanics ?? [s.Family]
    };

    static LevelDef ReadDef(JsonElement j) => new() {
        Name = j.GetProperty("name").GetString()!, Map = j.GetProperty("map").Deserialize<string[]>()!,
        Gun = (GunMode)j.GetProperty("gun").GetInt32(),
        BlueDir = new(j.GetProperty("blue")[0].GetInt32(), j.GetProperty("blue")[1].GetInt32()),
        OrangeDir = new(j.GetProperty("orange")[0].GetInt32(), j.GetProperty("orange")[1].GetInt32()),
        Hints = j.GetProperty("hints").Deserialize<string[]>()!, Fling = j.GetProperty("fling").GetBoolean(),
        Theme = j.GetProperty("theme").GetInt32(), Label = j.GetProperty("label").GetString()!
    };

    static readonly string[] FoundationOrder = [
        "Awakening", "Blue Sky", "Two Ends", "Weighted", "Stool Pigeon", "Peephole", "Toxic", "Emancipation",
        "Stepping Stones", "Through the Glass", "Catapult", "Pinball", "Launch Pad", "Spring Loaded", "Speedy Thing",
        "Gel Pit", "Trampoline Park", "Two Weights", "Umbrella", "Moat", "Up and Over", "Relay", "Twin Beams", "Skylight",
        "Closing Time", "Rush Hour", "Hold the Door", "Grid Lock", "Ascension", "Overpass", "Bounce House", "Disarmed",
        "Jailbreak", "Spotlight", "Double Decker", "Safe Cracker", "Bank Shot", "Flyover", "Dry Dock", "Light Well",
        "About Turn", "Fizzler Row", "Ascent", "Rebound", "Beam Split", "Freight Lift", "Terminal Velocity", "Rooftops", "Midterm"
    ];

    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        verbose = args.Any(a => a.StartsWith("--"));
        var sources = Campaign.Sources();
        if (args.Contains("--circuits") || args.Contains("--sources")) {
            var selected = args.Contains("--circuits") ? Circuits.Names.Select(n => sources[n]) : sources.Values;
            int failures = 0;
            foreach (var d in selected) {
                var t = Record(d, d.Solution!); if (!t.won) failures++;
                Console.WriteLine($"{(t.won ? "ok" : "FAIL")} {d.Name} ({t.frames} frames)");
            }
            return failures == 0 ? 0 : 1;
        }
        var additions = new List<Sample>();
        foreach (string name in Circuits.Names) {
            var def = sources[name]; var trace = Record(def, def.Solution!);
            if (!trace.won) throw new Exception($"Unsolved circuit: {name}");
            additions.Add(new(def, trace, name, RecordKey: name + "#circuit-v2", Mechanics: [name]));
        }
        foreach (var plan in Campaign.Blueprints) {
            var r = Spatial.Build(plan.Name, plan.Rooms.Select(n => sources[n]).ToArray(),
                1 + additions.Count % 6, plan.Horizontal);
            var trace = Record(r.Def, r.Def.Solution!, r.Goals);
            Console.WriteLine($"{(trace.won ? "ok" : "FAIL")} {plan.Name} ({trace.frames} frames, {r.Goals.Length} persistent circuits)");
            if (!trace.won) throw new Exception($"Unsolved blueprint: {plan.Name}");
            additions.Add(new(r.Def, trace, plan.Rooms[0], ExitGroups: r.Goals,
                RecordKey: plan.Name + "#circuit-v2", Mechanics: plan.Rooms));
        }
        if (args.Contains("--check") || args.Contains("--blueprints")) return 0;

        // Preserve the exact original maps and their recorded solutions. This also avoids the frozen
        // generator's process-randomized HashCode.Combine changing existing chambers on every export.
        using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "classic.json")));
        var menu = ReadDef(original.RootElement.GetProperty("menu"));
        var classic = new List<Sample>();
        foreach (var item in original.RootElement.GetProperty("chambers").EnumerateArray()) {
            var def = ReadDef(item.GetProperty("level"));
            var recorded = item.GetProperty("trace").GetProperty("inputs").GetString()!;
            var trace = Record(def, _ => Inputs(recorded));
            if (!trace.won) throw new Exception($"Classic reference failed: {def.Name}");
            classic.Add(new(def, trace, def.Name));
        }
        var byName = classic.Concat(additions).ToDictionary(s => s.Def.Name);
        var order = FoundationOrder.ToList();
        var composed = classic.Skip(41).Where(s => s.Def.Name != "Final Exam").Select(s => s.Def.Name).ToArray();
        order.AddRange(Campaign.Blueprints.Take(12).Select(p => p.Name));
        order.AddRange(composed.Take(3));
        order.AddRange(Campaign.Blueprints.Skip(12).Take(12).Select(p => p.Name));
        order.AddRange(composed.Skip(3).Take(3));
        order.AddRange(Campaign.Blueprints.Skip(24).Select(p => p.Name));
        order.AddRange(composed.Skip(6));
        order.Add("Final Exam");
        if (order.Count != 100 || order.Distinct().Count() != 100 || byName.Count != 100) throw new Exception("Campaign order must contain every chamber exactly once");
        var campaign = order.Select((n, i) => byName[n] with {
            Difficulty = i < 5 ? 0 : i < 24 ? 1 : i < 37 ? 2 : i < 49 ? 3 : i < 64 ? 4 : i < 79 ? 5 : i < 99 ? 6 : 7
        }).ToList();
        for (int i = 0; i < campaign.Count; i++) campaign[i].Def.Label = (i + 1).ToString("D2");

        var pools = new List<List<Sample>>(); var seen = new HashSet<string>();
        string[] early = ["Peephole", "Catapult", "Moat", "Hold the Door", "Double Decker", "Safe Cracker", "Spotlight", "Rush Hour",
            "Launch Pad", "Stool Pigeon", "Stepping Stones", "Light Well", "Flyover", "Closing Time"];
        string[] advanced = ["Light Well", "Freight Lift", "Dry Dock", "Beam Split", "Flyover", "Rebound", "Double Decker",
            "Fizzler Row", "Bank Shot", "Ascent", "Moat", "Hold the Door", "Safe Cracker", "Terminal Velocity", "Rooftops", "Closing Time"];
        for (int difficulty = 0; difficulty < 15; difficulty++) {
            var pool = new List<Sample>();
            var choices = difficulty < 5 ? early : advanced;
            int count = difficulty < 5 ? 2 : difficulty < 10 ? 3 : 4;
            for (int slot = 0; slot < 36; slot++) {
                Sample? sample = null;
                for (int attempt = 0; attempt < 400 && sample == null; attempt++) {
                    int seed = 100000 + difficulty * 100000 + slot * 400 + attempt;
                    var rng = new Random(seed);
                    var names = new[] { choices[(slot + difficulty * 3) % choices.Length] }
                        .Concat(choices.OrderBy(_ => rng.Next()).Where(n => n != choices[(slot + difficulty * 3) % choices.Length]).Take(count - 1)).ToArray();
                    bool horizontal = rng.Next(3) == 0 && !names.Contains("Ascent") && !names.Contains("Terminal Velocity")
                        && !names.Contains("Rooftops");
                    string signature = string.Join('>', names) + (horizontal ? ":gallery" : ":storeys");
                    if (seen.Contains(signature)) continue;
                    try {
                        var r = Spatial.Build($"Random Test {seed}", names.Select(n => sources[n]).ToArray(), 1 + difficulty % 6, horizontal);
                        if (difficulty >= 5 && r.Goals.Length < 2) continue;
                        var trace = Record(r.Def, r.Def.Solution!, r.Goals);
                        if (!trace.won) continue;
                        string layout = LayoutId(r.Def);
                        if (seen.Contains(layout)) continue;
                        seen.Add(signature); seen.Add(layout);
                        r.Def.Label = "??";
                        sample = new(r.Def, trace, names[0], seed, r.Goals, difficulty + 1, Mechanics: names);
                    } catch (InvalidOperationException) { /* Invalid access route or circuit budget: reject the plan. */ }
                }
                if (sample == null) throw new Exception($"No verified plan for tier {difficulty}, slot {slot}");
                pool.Add(sample);
            }
            pools.Add(pool);
            Console.WriteLine($"Random tier {difficulty + 1}: {pool.Count} verified {count}-stage challenges");
        }

        string dir = args.FirstOrDefault() ?? "data";
        Directory.CreateDirectory(dir);
        // Nothing is written until the complete campaign and every random candidate have won.
        File.WriteAllText(Path.Combine(dir, "levels.json"), JsonSerializer.Serialize(new {
            menu = Data(new(menu, new(true, 0, ""), "menu")), chambers = campaign.Select(Data)
        }));
        File.WriteAllText(Path.Combine(dir, "random.json"), JsonSerializer.Serialize(new { pools = pools.Select(p => p.Select(Data)) }));
        var traces = campaign.Select(s => (object)new { name = s.Def.Name, trace = s.Trace }).ToList();
        for (int d = 0; d < pools.Count; d++) for (int i = 0; i < pools[d].Count; i++)
            traces.Add(new { name = pools[d][i].Def.Name, random = new[] { d, i }, trace = pools[d][i].Trace });
        string testDir = Path.GetFullPath(Path.Combine(dir, "..", "test")); Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "traces.json"), JsonSerializer.Serialize(traces));
        Console.WriteLine($"Exported {campaign.Count} chambers, {pools.Sum(p => p.Count)} random challenges, {traces.Count} winning replays.");
        return 0;
    }
}
