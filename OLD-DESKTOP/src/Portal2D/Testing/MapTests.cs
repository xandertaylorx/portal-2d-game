using Portal2D.Core;
using Portal2D.Game;

namespace Portal2D.Testing;

/// <summary>Headless checks for the map builder: editing, saving, sharing and actually playing a custom map.</summary>
public static class MapTests
{
    public static int Run()
    {
        var bad = new List<string>();
        void Check(bool ok, string what) { if (!ok) bad.Add(what); }

        // editing operations
        var map = CustomMap.Blank("TEST");
        var ed = new MapEditor(map);
        bad.AddRange(ed.RunSelfTest());

        // sanitising: whatever a file or a pasted code contains must load safely
        var evil = new CustomMap
        {
            Name = "<<evil>>  name that is far too long to ever fit",
            Map = ["XXXXXXXXXXXXXXXX", "XPuoPEE?..zXXXXX", "X", "XXXXXXXXXXXXXXXX", "", "X", "X", "X", "X", "X", "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"],
            Gun = 99, Theme = -4, Best = float.NaN
        };
        Check(evil.Sanitize(), "a messy map is repaired rather than rejected");
        Check(evil.Map.All(r => r.Length == evil.Map[0].Length) && evil.W <= CustomMap.MaxW, "repaired map is rectangular and within limits");
        Check(evil.Map.Sum(r => r.Count(c => c == 'P')) == 1 && evil.Map.Sum(r => r.Count(c => c == 'E')) == 1, "repaired map has at most one start and exit");
        Check(!evil.Map.Any(r => r.Contains('u') || r.Contains('o')), "fixed-portal markers are stripped");
        Check(evil.Gun == 2 && evil.Theme == 0 && evil.Best == 0 && evil.Name.Length <= CustomMap.MaxName, "settings are clamped");
        Check(new World(evil.ToDef()).W == evil.W, "repaired map builds a world");
        Check(!new CustomMap { Map = ["XX", "XX"] }.Sanitize(), "a tiny map is rejected");

        // share codes
        var shared = CustomMap.FromShareCode(map.ToShareCode());
        Check(shared != null && shared.Map.SequenceEqual(map.Map) && shared.Name == map.Name && shared.Id != map.Id, "share code round-trips as a new map");
        Check(CustomMap.FromShareCode("hello") == null && CustomMap.FromShareCode("P2D1:!!!") == null && CustomMap.FromShareCode(null) == null, "bad share codes are refused");

        // files
        var tmp = Path.Combine(Path.GetTempPath(), "portal2d-maptest-" + Guid.NewGuid().ToString("N")[..6]);
        CustomMaps.DirOverride = tmp;
        try
        {
            var a = CustomMap.Blank("FIRST"); var b = CustomMap.Blank("SECOND");
            Check(CustomMaps.Save(a) && CustomMaps.Save(b), "maps save to disk");
            File.WriteAllText(Path.Combine(tmp, "junk.json"), "{ not json");
            var all = CustomMaps.LoadAll();
            Check(all.Count == 2 && all.Any(m => m.Name == "FIRST"), "saved maps load back, junk files are skipped");
            var oldPath = a.FilePath;
            a.Name = "RENAMED";
            CustomMaps.Save(a);
            Check(a.FilePath != oldPath && !File.Exists(oldPath!) && CustomMaps.LoadAll().Count == 2, "renaming moves the file instead of duplicating it");
            var copy = CustomMaps.Duplicate(a);
            Check(CustomMaps.LoadAll().Count == 3 && copy.Id != a.Id, "duplicate makes a new map");
            CustomMaps.Delete(copy);
            Check(CustomMaps.LoadAll().Count == 2, "delete removes the file");
        }
        finally
        {
            CustomMaps.DirOverride = null;
            try { Directory.Delete(tmp, true); } catch { }
        }

        // a custom map really plays: the blank room is walkable from start to exit
        var w = new World(CustomMap.Blank("WALK").ToDef());
        var bot = new Bot(w);
        for (int f = 0; f < 60 * 20 && !w.Player!.Won; f++) TestRunner.StepFrame(w, bot.In(mx: 1));
        Check(w.Player!.Won, "the player can walk from start to exit in a blank map");

        // one-button portals: clicks alternate blue and orange
        {
            var cm = CustomMap.Blank("CYCLE");
            var rows = (string[])cm.Map.Clone();
            rows[16] = "X" + new string('#', rows[16].Length - 2) + "X";
            cm.Map = rows;
            var cw = new World(cm.ToDef());
            var cb = new Bot(cw);
            cb.Aim = new System.Numerics.Vector2(cw.Player!.Pos.X, 16 * K.Tile + 1);
            var shot = cb.In(); shot.FireCycle = true;
            for (int i = 0; i < 2; i++)
            {
                cb.Aim = new System.Numerics.Vector2(cw.Player!.Pos.X + i * 200, 16 * K.Tile + 1);
                shot.AimWorld = cb.Aim;
                TestRunner.StepFrame(cw, shot);
                for (int f = 0; f < 30; f++) TestRunner.StepFrame(cw, cb.In());
                if (i == 0) Check(cw.Portals[0] != null && cw.Portals[1] == null, "the first one-button shot is blue");
            }
            Check(cw.Portals[0] != null && cw.Portals[1] != null, "the second one-button shot is orange");
        }

        // featured list: share codes become maps, junk entries are skipped
        {
            var good = CustomMap.Blank("FEAT").ToShareCode();
            var list = FeaturedMaps.Parse("{\"maps\":[{\"name\":\"hello\",\"author\":\"me\",\"code\":\"" + good + "\"},{\"name\":\"bad\",\"code\":\"nope\"}]}");
            Check(list.Count == 1 && list[0].Name == "HELLO" && list[0].Map.Name == "HELLO", "featured list keeps good maps and skips junk");
        }

        foreach (var s in bad) Console.WriteLine("  MAP BUILDER FAIL: " + s);
        Console.WriteLine(bad.Count == 0 ? "Map builder: OK" : $"Map builder: {bad.Count} FAILURES");
        return bad.Count == 0 ? 0 : 1;
    }
}
