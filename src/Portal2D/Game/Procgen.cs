using System.Numerics;
using Portal2D.Core;
using Portal2D.Testing;
using static Portal2D.Testing.Bot;

namespace Portal2D.Game;

/// <summary>
/// Room-based chamber builder, used for two things:
///  * the later handcrafted chambers, which are authored as a fixed sequence of rooms (<see cref="Compose"/>), and
///  * Random Test, which picks the sequence at random (<see cref="Generate"/>).
///
/// A chamber is a row of rooms separated by one-tile walls. Each room is a puzzle archetype with a few
/// randomised parameters, and every archetype also emits the inputs that solve it, so the whole chamber can be
/// played headlessly by <see cref="Bot"/> using the real physics. Random chambers are only handed to the player
/// after the bot finishes them; authored chambers are checked by <c>--test</c>.
/// </summary>
public static class Procgen
{
    const int H = 22, Floor = 19;
    const int T = K.Tile;
    const PortalColor Blue = PortalColor.Blue, Orange = PortalColor.Orange;
    static readonly Vector2 Fwd = new(120, 0), Back = new(-120, 0);

    public sealed class Result
    {
        public required LevelDef Def;
        public required Func<Bot, IEnumerable<InputState>> Solution;
        public int Seed;
        public int Attempts;
    }

    delegate IEnumerable<InputState> Step(Bot b);

    public enum Room
    {
        CeilCross, HighLedge, CubeCross, Fling, Laser, Umbrella, Gel,
        ShelfCube, FizzCube, UpFling, Skylight, TwinBeam, GelIsland,
        PlateHop, PlateCarry, Sprint, BankShot, Clockwork, GridGoo, GelStairs, GelCarry,
        Peephole, Stool, Catapult, Moat, Fizzler, Penthouse, Spotlight
    }

    public readonly record struct Spec(Room Type, bool Hard = false);

    /// <summary>Debug: force every room to this archetype.</summary>
    public static Room? Force;

    public static string Diagnose(int seeds)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var room in Enum.GetValues<Room>())
            foreach (bool hard in new[] { false, true })
            {
                int ok = 0;
                for (int sd = 1; sd <= seeds; sd++)
                    if (Verify(Compose([new(Room.CeilCross), new(room, hard)], sd, "diag", "", null))) ok++;
                sb.AppendLine($"{room,-10} {(hard ? "hard" : "    ")} {ok}/{seeds}");
            }
        return sb.ToString();
    }

    sealed class Ctx
    {
        public required LevelBuilder B;
        public required Random Rng;
        public readonly List<Step> Steps = new();
        public readonly List<string> Hints = new();
        public int Group;
        public bool Fling;
    }

    // ------------------------------------------------------------------ random tests

    /// <summary>Generate a random chamber for a seed, retrying sub-seeds until the bot can solve it.</summary>
    public static Result Generate(int seed, int difficulty)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var rng = new Random(HashCode.Combine(seed, attempt, 0x5eed));
            var plan = RandomPlan(rng, difficulty);
            var r = Compose(plan, HashCode.Combine(seed, attempt), $"Random Test {seed}", "??", null);
            r.Seed = seed;
            if (Verify(r)) { r.Attempts = attempt + 1; return r; }
        }
        return Compose([new(Room.CeilCross)], seed, $"Random Test {seed}", "??", null);
    }

    /// <summary>How demanding a room is (1 = trivial, 4 = needs several ideas at once). Hard variants add one.</summary>
    public static int Weight(Spec s) => BaseWeight(s.Type) + (s.Hard ? 1 : 0);

    static int BaseWeight(Room r) => r switch
    {
        Room.CeilCross or Room.HighLedge => 1,
        Room.GelIsland or Room.PlateHop or Room.Sprint or Room.ShelfCube or Room.CubeCross or Room.FizzCube
            or Room.Umbrella or Room.Laser => 2,
        Room.Skylight or Room.Gel or Room.Fling or Room.TwinBeam or Room.PlateCarry or Room.Clockwork
            or Room.GridGoo or Room.GelStairs or Room.GelCarry or Room.Peephole or Room.Catapult or Room.Moat
            or Room.Fizzler or Room.Spotlight => 3,
        Room.Stool => 2,
        _ => 4 // UpFling, BankShot, Penthouse
    };

    static List<Spec> RandomPlan(Random rng, int difficulty)
    {
        int rooms = Math.Clamp(2 + difficulty / 2, 2, 7);
        int cap = 2 + difficulty / 2;                       // heaviest room type on offer
        float hardChance = difficulty < 3 ? 0 : MathF.Min(0.65f, (difficulty - 2) * 0.1f);
        var all = Enum.GetValues<Room>();
        var plan = new List<Spec>();
        bool high = false;
        for (int i = 0; i < rooms; i++)
        {
            Room pick;
            if (Force is Room forced) pick = forced;
            else
            {
                // never repeat a room within a chamber (there are plenty to choose from), keep the weight in range
                var pool = all.Where(r => plan.All(p => p.Type != r) && BaseWeight(r) <= cap
                    && !(NeedsFloorEntry(r) && high)
                    && !(Gated(r) && plan.Count(p => Gated(p.Type)) >= World.Groups)).ToList();
                if (pool.Count == 0) pool = all.Where(r => !(NeedsFloorEntry(r) && high) && plan.LastOrDefault().Type != r).ToList();
                pick = pool[rng.Next(pool.Count)];
            }
            plan.Add(new Spec(pick, rng.NextSingle() < hardChance));
            high = ExitsHigh(pick);
        }
        return plan;
    }

    public static bool Verify(Result r)
    {
        var w = new World(r.Def);
        var bot = new Bot(w);
        using var e = r.Solution(bot).GetEnumerator();
        for (float t = 0; t < 180; t += 1 / 60f)
        {
            var input = e.MoveNext() ? e.Current : bot.In();
            TestRunner.StepFrame(w, input);
            if (w.Player!.Won) return true;
            if (w.Player.Dead) return false;
        }
        return false;
    }

    // ------------------------------------------------------------------ composition

    static bool Gated(Room r) => r is Room.CubeCross or Room.Laser or Room.Umbrella or Room.ShelfCube or Room.FizzCube or Room.Skylight or Room.TwinBeam
        or Room.PlateCarry or Room.BankShot or Room.Clockwork or Room.GelCarry or Room.Catapult or Room.Moat
        or Room.Fizzler or Room.Penthouse or Room.Spotlight;
    static bool NeedsFloorEntry(Room r) => r is Room.Fling or Room.Laser or Room.UpFling or Room.TwinBeam or Room.Spotlight;
    static bool ExitsHigh(Room r) => r is Room.HighLedge or Room.Gel or Room.UpFling or Room.PlateHop or Room.GelStairs or Room.Stool;

    static int RoomWidth(Spec s) => s.Type switch
    {
        Room.CeilCross => s.Hard ? 26 : 22,
        Room.HighLedge => 18,
        Room.CubeCross => s.Hard ? 28 : 24,
        Room.Fling => s.Hard ? 28 : 26,
        Room.Laser => 22,
        Room.Umbrella => 20,
        Room.Gel => 20,
        Room.ShelfCube => 22,
        Room.FizzCube => 22,
        Room.UpFling => 28,
        Room.Skylight => 22,
        Room.TwinBeam => 22,
        Room.GelIsland => 24,
        Room.PlateHop => 22,
        Room.PlateCarry => 26,
        Room.Sprint => 26,
        Room.BankShot => 28,
        Room.Clockwork => 28,
        Room.GridGoo => 34,
        Room.GelStairs => 24,
        Room.GelCarry => 24,
        Room.Peephole => 26,
        Room.Stool => 20,
        Room.Catapult => 24,
        Room.Moat => 35,
        Room.Fizzler => 37,
        Room.Penthouse => 30,
        Room.Spotlight => 24,
        _ => 20
    };

    /// <summary>Build a chamber from an explicit room sequence. Parameters inside rooms come from <paramref name="seed"/>.</summary>
    public static Result Compose(IReadOnlyList<Spec> plan, int seed, string name, string label, string[]? hints)
    {
        var rng = new Random(HashCode.Combine(seed, 0xc0de));
        int width = 1 + 5 + 1 + plan.Sum(p => RoomWidth(p) + 1) + 6 + 1;
        var b = new LevelBuilder(width, H);
        var ctx = new Ctx { B = b, Rng = rng };

        b.Fill(1, 1, 5, Floor, ' ');
        b.Set(2, Floor, 'P');
        int x0 = 7;
        int entryRow = Floor;
        Separator(b, 6, entryRow, null);

        foreach (var spec in plan)
        {
            if (NeedsFloorEntry(spec.Type) && entryRow != Floor)
                throw new InvalidOperationException($"{name}: room {spec.Type} needs a floor-level entrance");
            int w = RoomWidth(spec);
            b.Fill(x0, 1, x0 + w - 1, Floor, ' ');
            int groupBefore = ctx.Group;
            int exitRow = BuildRoom(ctx, spec, x0, w, entryRow);
            int? doorGroup = ctx.Group > groupBefore ? ctx.Group - 1 : null;
            if (ctx.Group > World.Groups) throw new InvalidOperationException($"{name}: more than {World.Groups} gated rooms");
            Separator(b, x0 + w, exitRow, doorGroup);
            x0 += w + 1;
            entryRow = exitRow;
        }

        b.Fill(x0, 1, x0 + 5, Floor, ' ');
        b.Set(x0 + 3, Floor, 'E');
        int exitX = x0 + 3;
        ctx.Steps.Add(bot => bot.WalkTo(X(exitX), hopCubes: true, timeout: 20));

        var def = new LevelDef
        {
            Name = name,
            Theme = 1 + Math.Abs(seed) % (Render.LevelArt.ThemeCount - 1),
            Map = b.Build(),
            Hints = hints ?? ctx.Hints.Distinct().Take(2).ToArray(),
            Fling = ctx.Fling,
            Label = label
        };
        var steps = ctx.Steps.ToList();
        return new Result { Def = def, Seed = seed, Solution = bot => steps.SelectMany(s => s(bot)) };
    }

    /// <summary>Wall between rooms with a 2-high opening at the exit height (or a door at floor level).</summary>
    static void Separator(LevelBuilder b, int x, int exitRow, int? doorGroup)
    {
        // the column is still solid from the initial fill (and may hold receivers/emitters) - only cut the opening
        if (doorGroup is int g) b.Fill(x, Floor - 3, x, Floor, World.DoorChar(g));
        else b.Fill(x, exitRow - 1, x, exitRow, ' ');
    }

    /// <summary>First row from <paramref name="preferred"/> (wrapping in [lo, hi]) with <paramref name="count"/> plain wall cells.</summary>
    static int FreeRows(LevelBuilder b, int x, int preferred, int lo, int hi, int count)
    {
        for (int k = 0; k <= hi - lo; k++)
        {
            int y = lo + (preferred - lo + k) % (hi - lo + 1);
            bool ok = true;
            for (int i = 0; i < count; i++) if (b.Get(x, y + i) != 'X') ok = false;
            if (ok) return y;
        }
        throw new InvalidOperationException("no free wall rows for emitters");
    }

    static IEnumerable<InputState> Through(Bot bot, float x, Func<bool> tele, float? landX = null)
    {
        foreach (var i in bot.WalkTo(x, stopIf: tele)) yield return i;
        foreach (var i in bot.Until(tele)) yield return i;
        if (landX is float lx)
            foreach (var i in bot.Steer(lx, () => bot.Player.OnGround, 4)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.OnGround, 4)) yield return i;
    }

    // ------------------------------------------------------------------ rooms

    /// <summary>Builds one room and appends its solution. Returns the row the player leaves at.</summary>
    static int BuildRoom(Ctx c, Spec spec, int x0, int w, int entryRow)
    {
        var b = c.B;
        var rng = c.Rng;
        bool hard = spec.Hard;
        int n = 0;
        Func<bool> MarkTele(Bot bot) { n = bot.Player.Teleports; return () => bot.Player.Teleports > n; }
        c.Steps.Add(bot => bot.WalkTo(X(x0 + 1), hopCubes: true, timeout: 15));

        switch (spec.Type)
        {
            case Room.CeilCross:
            {
                int gooEnd = x0 + w - 7;
                b.Fill(x0 + 2, 20, x0 + 5, 20, '#');
                b.Fill(x0 + 7, 20, gooEnd, 21, '~');
                b.Fill(x0 + w - 6, 0, x0 + w - 2, 0, '#');
                if (hard || rng.Next(2) == 0) b.Fill(x0 + 10, 1, x0 + 11, 5, 'X');
                int ceil = x0 + w - 5;
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 3, 20)), bot.Fire(Orange, Ceil(ceil, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 4) * T, tele); });
                c.Hints.Add("Goo below. Find a way over it.");
                return Floor;
            }
            case Room.HighLedge:
            {
                int top = hard ? rng.Next(6, 9) : rng.Next(8, 13);
                b.Fill(x0 + 9, top, x0 + w - 1, Floor, 'X');
                b.Fill(x0 + 2, 20, x0 + 6, 20, '#');
                // a taller block hides more of the ceiling, so the hard variant's portal spot sits closer
                int ceilAt = hard ? x0 + 10 : x0 + 12;
                b.Fill(ceilAt - 1, 0, x0 + w - 3, 0, '#');
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 3, 20)), bot.Fire(Orange, Ceil(ceilAt, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 4) * T, tele); });
                c.Hints.Add("Up is just another direction.");
                return top - 1;
            }
            case Room.CubeCross:
            {
                int g = c.Group++;
                b.Set(x0 + 3, Floor, 'C');
                b.Fill(x0 + 5, 20, x0 + 7, 20, '#');
                b.Fill(x0 + 9, 20, x0 + w - 8, 21, '~');
                b.Fill(x0 + w - 7, 0, x0 + w - 4, 0, '#');
                b.Set(x0 + w - 5, Floor, World.ButtonChar(g));
                int bx = x0 + w - 5;
                c.Steps.Add(bot => Seq(bot.Use(), bot.Fire(Blue, Bot.Floor(x0 + 5, 20)), bot.Fire(Orange, Ceil(x0 + w - 6, 0)),
                    bot.AimAt(new Vector2((x0 + 9) * T, 700))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 6) * T, tele); });
                c.Steps.Add(bot => Seq(bot.WalkTo((bx + 2) * T), bot.Drop(new Vector2((bx - 2) * T, 700)), bot.Wait(0.5f)));
                c.Hints.Add("Carry the cube with you through the portal.");
                return Floor;
            }
            case Room.Fling:
            {
                c.Fling = true;
                // launch face on the shared wall - leave anything the previous room put there (receivers) alone
                for (int y = 7; y <= 12; y++) if (b.Get(x0 - 1, y) == 'X') b.Set(x0 - 1, y, '#');
                b.Fill(x0 + 5, 5, x0 + 9, 5, 'X');
                b.Fill(x0 + 5, 0, x0 + 9, 0, '#');
                b.Fill(x0 + 1, 20, x0 + 3, 20, '#');
                b.Fill(x0 + 11, 20, x0 + (hard ? 20 : 18), 21, '~');
                b.Fill(x0 + w - 2, Floor - 1, x0 + w - 2, Floor, 'X'); // a curb so a fast landing can't slide into the next room
                // prefer high launch rows: low ones aren't visible from the shelf's edge
                int launchRow = new[] { 7 + rng.Next(0, 2), 7, 8, 9 }
                    .First(y => b.Get(x0 - 1, y) == '#' && b.Get(x0 - 1, y + 1) == '#');
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0)), bot.Fire(Blue, Bot.Floor(x0 + 1, 20)), bot.Fire(Orange, Ceil(x0 + 5, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 2) * T, tele, (x0 + 7) * T); });
                c.Steps.Add(bot => Seq(bot.WalkTo((x0 + 5) * T + 14), bot.Fire(Orange, WallR(x0 - 1, launchRow))));
                c.Steps.Add(bot =>
                {
                    var tele = MarkTele(bot);
                    return Seq(bot.Move(-1, () => !bot.Player.OnGround), bot.Steer((x0 + 2) * T, tele),
                        bot.Until(() => bot.Player.OnGround, 5));
                });
                c.Hints.Add("Speedy thing goes in, speedy thing comes out.");
                return Floor;
            }
            case Room.Laser:
            {
                int g = c.Group++;
                int e = FreeRows(b, x0 - 1, rng.Next(6, 10), 6, 9, 1);
                int r = hard ? rng.Next(2, 4) : rng.Next(2, 6);
                b.Set(x0 - 1, e, '>');
                b.Fill(x0 + 9, 2, x0 + 10, 14, 'X');
                b.Fill(x0 + 9, e - 2, x0 + 9, e + 3, '#');
                b.Fill(x0 + 10, 1, x0 + 10, 7, '#');
                if (hard || rng.Next(2) == 0) b.Fill(x0 + 16, 1, x0 + 16, 14, '=');
                b.Set(x0 + w, r, World.ReceiverChar(g));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 2)), bot.Fire(Blue, WallL(x0 + 9, e)), bot.WalkTo(X(x0 + 14)),
                    bot.Fire(Orange, WallR(x0 + 10, r)), bot.Wait(0.6f)));
                c.Hints.Add("Redirect the beam into the receiver.");
                return Floor;
            }
            case Room.Umbrella:
            {
                int g = c.Group++;
                b.Set(x0 + 3, Floor, 'C');
                b.Set(x0 + 8, 0, 'v');
                if (hard || rng.Next(2) == 0) b.Set(x0 + 13, 0, 'v');
                b.Set(x0 + 17, Floor, World.ButtonChar(g));
                c.Steps.Add(bot => Seq(bot.Use(), bot.WalkTo((x0 + 16) * T + 8, aimRel: new Vector2(0, -400)),
                    bot.Drop(new Vector2((x0 + 20) * T, 700)), bot.Wait(0.5f)));
                c.Hints.Add("Cubes block lasers. Hold one over your head.");
                return Floor;
            }
            case Room.Gel:
            {
                int top = hard ? rng.Next(5, 7) : rng.Next(6, 9);
                b.Fill(x0 + 12, top + 1, x0 + w - 1, Floor, 'X');
                b.Fill(x0 + 8, 20, x0 + 10, 20, 'J');
                b.Fill(x0 + 7, 0, x0 + 11, 0, '#');
                b.Fill(x0 + 2, 20, x0 + 4, 20, '#');
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 2, 20)), bot.Fire(Orange, Ceil(x0 + 8, 0))));
                c.Steps.Add(bot =>
                {
                    var tele = MarkTele(bot);
                    var p = bot.Player;
                    return Seq(bot.WalkTo((x0 + 3) * T, stopIf: tele), bot.Until(tele),
                        bot.Steer((x0 + 9) * T + 16, () => p.Vel.Y < -300),
                        bot.Until(() => p.Pos.Y < (top + 3) * T, 4), bot.Until(() => p.OnGround, 4, mx: 1));
                });
                c.Hints.Add("Land hard on gel to bounce high.");
                return top;
            }
            case Room.ShelfCube:
            {
                // cube up on a shelf: reach it through the ceiling, bring it down to the button
                int g = c.Group++;
                int shelf = hard ? 7 : 9;
                b.Fill(x0 + 1, shelf, x0 + 6, shelf, 'X');
                b.Set(x0 + 3, shelf - 1, 'C');
                b.Fill(x0 + 1, 0, x0 + 6, 0, '#');
                b.Fill(x0 + 9, 20, x0 + 13, 20, '#');
                b.Set(x0 + 17, Floor, World.ButtonChar(g));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 15)), bot.Fire(Blue, Bot.Floor(x0 + 10, 20)), bot.Fire(Orange, Ceil(x0 + 3, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 11) * T, tele, (x0 + 4) * T); });
                c.Steps.Add(bot => Seq(bot.Use(), bot.Fire(Blue, Ceil(x0 + 1, 0)), bot.AimAt(new Vector2((x0 + 20) * T, 700)),
                    bot.WalkTo((x0 + 16) * T + 8), bot.Drop(new Vector2((x0 + 20) * T, 700)), bot.Wait(0.5f)));
                c.Hints.Add("The cube is out of reach. Come at it from above.");
                return Floor;
            }
            case Room.FizzCube:
            {
                // the cube can't cross the grid, so send it over through the portals
                int g = c.Group++;
                b.Set(x0 + 3, Floor, 'C');
                b.Fill(x0 + 5, 20, x0 + 8, 20, '#');
                b.Fill(x0 + 11, 1, x0 + 11, Floor, '|');
                b.Fill(x0 + 14, 0, x0 + 18, 0, '#');
                b.Set(x0 + 17, Floor, World.ButtonChar(g));
                c.Steps.Add(bot => Seq(bot.Use(), bot.Fire(Blue, Bot.Floor(x0 + 5, 20)), bot.Fire(Orange, Ceil(x0 + 15, 0)),
                    bot.AimAt(new Vector2((x0 + 9) * T, 700))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 6) * T, tele); });
                c.Steps.Add(bot => Seq(bot.WalkTo((x0 + 19) * T), bot.Drop(new Vector2((x0 + 15) * T, 700)), bot.Wait(0.5f)));
                c.Hints.Add("Grids vaporise cubes. Portals don't.");
                return Floor;
            }
            case Room.UpFling:
            {
                // shelf -> fall into a floor portal -> launch up out of another floor portal onto a high ledge
                c.Fling = true;
                int top = hard ? 8 : 9;
                b.Fill(x0 + 5, 5, x0 + 9, 5, 'X');
                b.Fill(x0 + 5, 0, x0 + 9, 0, '#');
                b.Fill(x0 + 1, 20, x0 + 3, 20, '#');
                b.Fill(x0 + 11, 20, x0 + 16, 21, '~');
                b.Fill(x0 + 17, 20, x0 + 21, 20, '#');
                b.Fill(x0 + 23, top, x0 + w - 1, Floor, 'X');
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0)), bot.Fire(Blue, Bot.Floor(x0 + 1, 20)), bot.Fire(Orange, Ceil(x0 + 5, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 2) * T, tele, (x0 + 7) * T); });
                c.Steps.Add(bot => Seq(bot.WalkTo((x0 + 10) * T - 6), bot.Fire(Orange, Bot.Floor(x0 + 19, 20))));
                c.Steps.Add(bot =>
                {
                    var tele = MarkTele(bot);
                    var p = bot.Player;
                    return Seq(bot.WalkTo((x0 + 5) * T + 14), bot.Move(-1, () => !p.OnGround), bot.Steer((x0 + 2) * T, tele),
                        bot.Until(() => p.Pos.Y < (top - 1) * T, 3), bot.Until(() => p.OnGround, 4, mx: 1));
                });
                c.Hints.Add("Fall far into a floor portal and you'll fly out of the other one just as high.");
                return top - 1;
            }
            case Room.Skylight:
            {
                // a beam pointing up from a wall shelf: catch it in the ceiling and aim it at the receiver
                int g = c.Group++;
                int r = hard ? rng.Next(8, 10) : rng.Next(9, 13);
                b.Fill(x0 + 3, 12, x0 + 5, 12, 'X');
                b.Set(x0 + 4, 12, '^');
                b.Fill(x0 + 2, 0, x0 + 7, 0, '#');
                b.Fill(x0 + 12, 6, x0 + 13, 14, 'X');
                b.Fill(x0 + 13, 7, x0 + 13, 13, '#');
                b.Set(x0 + w, r, World.ReceiverChar(g));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 8)), bot.Fire(Blue, Ceil(x0 + 4, 0)), bot.WalkTo(X(x0 + 16)),
                    bot.Fire(Orange, WallR(x0 + 13, r - 1)), bot.Wait(0.6f)));
                c.Hints.Add("Catch the beam in the ceiling and send it sideways.");
                return Floor;
            }
            case Room.TwinBeam:
            {
                // two beams through one portal pair, both receivers must light
                int g = c.Group++;
                int e = FreeRows(b, x0 - 1, rng.Next(7, 10), 7, 9, 2);
                int r = rng.Next(2, 6);
                b.Set(x0 - 1, e, '>').Set(x0 - 1, e + 1, '>');
                b.Fill(x0 + 9, 2, x0 + 10, 14, 'X');
                b.Fill(x0 + 9, e - 2, x0 + 9, e + 3, '#');
                b.Fill(x0 + 10, 1, x0 + 10, 7, '#');
                if (hard) b.Fill(x0 + 16, 1, x0 + 16, 14, '=');
                b.Set(x0 + w, r, World.ReceiverChar(g)).Set(x0 + w, r + 1, World.ReceiverChar(g));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 2)), bot.Fire(Blue, WallL(x0 + 9, e)), bot.WalkTo(X(x0 + 14)),
                    bot.Fire(Orange, WallR(x0 + 10, r)), bot.Wait(0.6f)));
                c.Hints.Add("Two beams, one portal pair. Line them both up.");
                return Floor;
            }
            case Room.PlateHop:
            {
                // the orange plate throws you up and to the right: over a goo pit onto a high ledge
                int rise = hard ? rng.Next(7, 9) : rng.Next(4, 7);
                int top = 20 - rise;
                int lx = x0 + 10;
                b.Set(x0 + 3, 20, 'Q');
                b.Fill(x0 + 5, 20, lx - 1, 21, '~');
                b.Fill(lx, top, x0 + w - 1, Floor, 'X');
                c.Steps.Add(bot => Seq(bot.Move(1, () => !bot.Player.OnGround), bot.Move(1, () => bot.Player.OnGround, 4)));
                c.Hints.Add("Orange plates throw you. Step on one.");
                return top - 1;
            }
            case Room.PlateCarry:
            {
                // bring the cube up to a button on a high shelf by riding the plate with it
                int g = c.Group++;
                int rise = hard ? 8 : rng.Next(6, 8);
                int s = 20 - rise;
                int bx = x0 + 15;
                b.Set(x0 + 2, Floor, 'C');
                b.Set(x0 + 5, 20, 'Q');
                b.Fill(x0 + 12, s, x0 + 18, s + 1, 'X');
                b.Set(bx, s - 1, World.ButtonChar(g));
                b.Fill(x0 + 8, 20, x0 + 20, 21, '~');
                var fwd = new Vector2(120, 0);
                c.Steps.Add(bot => Seq(bot.Use(), bot.Move(1, () => !bot.Player.OnGround, aimRel: fwd),
                    bot.Move(1, () => bot.Player.OnGround, 4, aimRel: fwd)));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(bx) - 36, aimRel: fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(120, -2))), bot.Wait(0.5f)));
                c.Hints.Add("The plate throws you but not the cube you carry.");
                return Floor;
            }
            case Room.Sprint:
            {
                // orange gel makes you fast: run up, then jump a gap no normal jump can clear
                int gap = hard ? 10 : rng.Next(8, 10);
                int land = x0 + 8 + gap;
                b.Fill(x0 + 1, 20, x0 + 7, 20, 'G');
                b.Fill(x0 + 8, 20, land - 1, 21, '~');
                c.Steps.Add(bot => Seq(bot.Move(1, () => bot.Player.Pos.X >= (x0 + 8) * T + 6, 4),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > land * T + 8)));
                c.Hints.Add("Orange gel makes you fast. Run, then jump.");
                return Floor;
            }
            case Room.BankShot:
            {
                // a falling beam, a mirror cube and a platform in the air: bank the beam into the receiver
                int g = c.Group++;
                int rise = hard ? 7 : 6;
                int s = 20 - rise;
                int px = x0 + 10, pw = 7;
                int bm = x0 + 14;
                b.Set(x0 + 2, Floor, 'N');
                b.Set(x0 + 3, 20, 'Q');
                b.Fill(px, s, px + pw - 1, s + 1, 'X');
                b.Set(bm, 0, 'v');
                b.Set(x0 + w, s - 1, World.ReceiverChar(g));
                var fwd = new Vector2(120, 0);
                c.Steps.Add(bot => Seq(bot.Use(), bot.Move(1, () => !bot.Player.OnGround, aimRel: fwd),
                    bot.Move(1, () => bot.Player.OnGround, 4, aimRel: fwd)));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(bm) - 36, aimRel: fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(120, -2))), bot.Wait(0.6f)));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(px - 2), timeout: 4), bot.Until(() => bot.Player.OnGround, 3), bot.WalkTo(X(x0 + w - 3), timeout: 10)));
                c.Hints.Add("Cubes with a mirror bend beams. Get it into the light.");
                return Floor;
            }
            case Room.Clockwork:
            {
                // a timed button: the door only stays open for a few seconds, so sprint for it
                int g = c.Group++;
                int gap = hard ? 10 : rng.Next(8, 10);
                b.Set(x0 + 1, Floor, World.TimedButtonChar(g));
                b.Fill(x0 + 3, 20, x0 + 9, 20, 'G');
                b.Fill(x0 + 10, 20, x0 + 9 + gap, 21, '~');
                int land = x0 + 10 + gap;
                c.Steps.Add(bot => Seq(bot.Move(1, () => bot.Player.Pos.X >= (x0 + 10) * T + 6, 4),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > land * T + 8)));
                c.Hints.Add("The door only stays open for a few seconds.");
                return Floor;
            }
            case Room.GridGoo:
            {
                // a grid wipes your portals half way: use them to cross the first pit, then place new ones for the second
                int gx = x0 + (hard ? 21 : 19);
                int pitB = gx + 4;
                b.Fill(x0 + 5, 20, x0 + 9, 21, '~');
                b.Fill(x0 + 2, 20, x0 + 3, 20, '#');
                b.Fill(x0 + 10, 0, x0 + 12, 0, '#');
                b.Fill(gx, 1, gx, Floor, '|');
                b.Fill(gx + 2, 20, gx + 3, 20, '#');
                b.Fill(pitB, 20, pitB + 5, 21, '~');
                b.Fill(pitB + 6, 0, pitB + 8, 0, '#');
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 2, 20)), bot.Fire(Orange, Ceil(x0 + 10, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 3) * T, tele, (x0 + 13) * T); });
                c.Steps.Add(bot => bot.WalkTo(X(gx + 1)));
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(gx + 2, 20)), bot.Fire(Orange, Ceil(pitB + 6, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (gx + 3) * T, tele, (pitB + 7) * T); });
                c.Hints.Add("Grids erase your portals. Plan for it.");
                return Floor;
            }
            case Room.GelStairs:
            {
                // two gel pads, one above the other: bounce up the stairs
                int r1 = hard ? 7 : rng.Next(5, 7);
                int r2 = hard ? 7 : rng.Next(5, 7);
                int s1 = 20 - r1, s2 = s1 - r2;
                b.Fill(x0 + 3, 20, x0 + 5, 20, 'J');
                b.Fill(x0 + 8, s1, x0 + 13, s1 + 1, 'X');
                b.Fill(x0 + 9, s1, x0 + 11, s1, 'J');
                b.Fill(x0 + 15, s2, x0 + w - 1, Floor, 'X');
                c.Steps.Add(bot => Seq(bot.Move(1, () => !bot.Player.OnGround), bot.Move(1, () => bot.Player.OnGround && bot.Player.Pos.Y < (s1 + 1) * T, 4)));
                c.Hints.Add("Bounce, then bounce again.");
                return s2 - 1;
            }
            case Room.GelCarry:
            {
                // carry the cube while a gel pad throws you up to a button on a shelf
                int g = c.Group++;
                int rise = hard ? 7 : rng.Next(5, 7);
                int s = 20 - rise;
                int bx = x0 + 12;
                b.Set(x0 + 2, Floor, 'C');
                b.Fill(x0 + 4, 20, x0 + 6, 20, 'J');
                b.Fill(x0 + 9, s, x0 + 16, s + 1, 'X');
                b.Set(bx, s - 1, World.ButtonChar(g));
                b.Fill(x0 + 8, 20, x0 + 17, 21, '~');
                var fwd = new Vector2(120, 0);
                c.Steps.Add(bot => Seq(bot.Use(), bot.Move(1, () => !bot.Player.OnGround, aimRel: fwd),
                    bot.Move(1, () => bot.Player.OnGround && bot.Player.Pos.Y < (s + 1) * T, 4, aimRel: fwd)));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(bx) - 36, aimRel: fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(120, -2))), bot.Wait(0.5f)));
                c.Hints.Add("Cubes don't bounce while you carry them. You still do.");
                return Floor;
            }
            case Room.Peephole:
            {
                // the far side of the wall is only visible through the window
                int wx = x0 + 14;
                b.Fill(wx, 1, wx + 2, Floor, 'X').Fill(wx, 3, wx + 2, 8, ' ');
                b.Fill(x0 + 1, 20, x0 + 2, 20, '#');
                b.Fill(x0 + 19, 0, x0 + w - 1, 0, '#');
                if (hard) b.Fill(x0 + 23, 17, x0 + 23, Floor, 'X');
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 4)), bot.Fire(Blue, Bot.Floor(x0 + 1, 20)), bot.Fire(Orange, Ceil(x0 + 20, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 2) * T, tele); });
                c.Hints.Add("Portal shots fly through the window. You can't.");
                return Floor;
            }
            case Room.Stool:
            {
                // the ledge is one jump too high: stand on the cube
                int lx = x0 + 12;
                b.Fill(lx, 16, x0 + w - 1, Floor, 'X');
                b.Set(x0 + 3, Floor, 'C');
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 3) - 26), bot.Use(), bot.WalkTo(lx * T - 74, aimRel: Fwd), bot.Use(), bot.Wait(0.6f),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.Y < 610),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > (lx + 1) * T)));
                c.Hints.Add("A cube makes a decent step.");
                return 15;
            }
            case Room.Catapult:
            {
                // the button is out of reach: throw a cube onto it with a launch plate
                int g = c.Group++;
                int a = x0 + 9;
                b.Set(a, 20, 'Q');
                b.Fill(a + 7, 12, x0 + w - 1, 13, 'X');
                b.Set(a + 9, 11, World.ButtonChar(g));
                b.Set(x0 + 3, Floor, 'C');
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 3) - 26), bot.Use(), bot.WalkTo(a * T - 22, aimRel: Fwd), bot.Use(), bot.Wait(1.5f),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > (a + 2) * T)));
                c.Hints.Add("Launch plates throw cubes, too.");
                return Floor;
            }
            case Room.Moat:
            {
                // the cube is on an island: fetch it, bring it back
                int g = c.Group++;
                b.Fill(x0 + 9, 20, x0 + 29, 21, '~').Fill(x0 + 17, 20, x0 + 21, 20, '#').Fill(x0 + 3, 20, x0 + 4, 20, '#');
                b.Fill(x0 + 15, 0, x0 + 20, 0, '#').Fill(x0 + 29, 0, x0 + 34, 0, '#');
                b.Set(x0 + 32, Floor, World.ButtonChar(g)).Set(x0 + 19, Floor, 'C');
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 3, 20)), bot.Fire(Orange, Ceil(x0 + 17, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 4) * T, tele); });
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 19) - 40), bot.Use(), bot.WalkTo(X(x0 + 20), aimRel: Fwd),
                    bot.Fire(Blue, Bot.Floor(x0 + 19, 20)), bot.Fire(Orange, Ceil(x0 + 30, 0))));
                c.Steps.Add(bot =>
                {
                    var tele = MarkTele(bot);
                    return Seq(bot.WalkTo((x0 + 20) * T - 4, stopIf: tele, aimRel: Fwd), bot.Until(tele), bot.Until(() => bot.Player.OnGround, 4),
                        bot.WalkTo(X(x0 + 34)), bot.Drop(new Vector2((x0 + 30) * T, 700)), bot.Wait(0.5f));
                });
                c.Hints.Add("Fetch the cube from the island. Bring it back.");
                return Floor;
            }
            case Room.Fizzler:
            {
                // the cube goes over the grid and the goo by portal
                int g = c.Group++;
                b.Fill(x0, 20, x0 + 1, 20, '#').Fill(x0 + 6, 20, x0 + 10, 21, '~').Fill(x0 + 12, 0, x0 + 14, 0, '#');
                b.Fill(x0 + 14, 20, x0 + 15, 20, '#');
                b.Fill(x0 + 17, 1, x0 + 17, Floor, '|');
                b.Fill(x0 + 19, 20, x0 + 29, 21, '~').Fill(x0 + 31, 0, x0 + 34, 0, '#');
                b.Set(x0 + 34, Floor, World.ButtonChar(g)).Set(x0 + 4, Floor, 'C');
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 3)), bot.Use(), bot.Fire(Blue, Bot.Floor(x0, 20)), bot.Fire(Orange, Ceil(x0 + 12, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 1) * T, tele, (x0 + 13) * T + 16); });
                c.Steps.Add(bot => Seq(bot.WalkTo((x0 + 13) * T, aimRel: Fwd),
                    bot.Fire(Blue, Bot.Floor(x0 + 14, 20)), bot.Fire(Orange, Ceil(x0 + 31, 0))));
                c.Steps.Add(bot =>
                {
                    var tele = MarkTele(bot);
                    return Seq(Through(bot, (x0 + 15) * T, tele, (x0 + 33) * T),
                        bot.WalkTo(X(x0 + 36), aimRel: Fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(-100, 30))), bot.Wait(0.6f));
                });
                c.Hints.Add("The grid eats cubes. You can shoot through it.");
                return Floor;
            }
            case Room.Penthouse:
            {
                // go up through the hole by portal, send the cube down it, put it on the button
                int g = c.Group++;
                b.Fill(x0, 13, x0 + w - 1, 14, 'X').Fill(x0 + 7, 13, x0 + 10, 14, ' ');
                b.Fill(x0 + 2, 20, x0 + 3, 20, '#').Fill(x0 + 15, 0, x0 + 16, 0, '#');
                b.Set(x0 + 23, 12, 'C').Set(x0 + 27, Floor, World.ButtonChar(g));
                c.Steps.Add(bot => Seq(bot.WalkTo((x0 + 7) * T + 14), bot.Fire(Orange, Ceil(x0 + 15, 0)), bot.Fire(Blue, Bot.Floor(x0 + 2, 20))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 3) * T, tele); });
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 23) - 40), bot.Use(), bot.WalkTo((x0 + 11) * T + 20, aimRel: Back), bot.Use(), bot.Wait(0.8f),
                    bot.WalkTo(X(x0 + 9)), bot.Until(() => bot.Player.OnGround && bot.Player.Pos.Y > 15 * T, 3),
                    bot.WalkTo(X(x0 + 10)), bot.Use(), bot.WalkTo(X(x0 + 25), aimRel: Fwd),
                    bot.Drop(new Vector2((x0 + 28) * T, 700)), bot.Wait(0.6f)));
                c.Hints.Add("The ceiling upstairs is out of reach, not out of sight.");
                return Floor;
            }
            case Room.Spotlight:
            {
                // the beam is on this side of the wall, the receiver on the other: put the portals through the window
                int g = c.Group++;
                int wx = x0 + 12;
                int e = FreeRows(b, x0 - 1, 12, 10, 13, 1);
                b.Set(x0 - 1, e, '>');
                b.Fill(wx, 1, wx + 2, Floor, 'X').Fill(wx, 3, wx + 2, 8, ' ').Fill(wx, e - 1, wx, e, '#');
                b.Fill(wx, Floor - 3, wx + 2, Floor, World.DoorChar(g));
                b.Fill(x0 + 15, 0, x0 + w - 1, 0, '#');
                b.Set(x0 + 17, 6, World.ReceiverChar(g));
                c.Steps.Add(bot => Seq(bot.Fire(Orange, Ceil(x0 + 17, 0)), bot.Fire(Blue, WallL(wx, e - 1)), bot.Wait(1.2f)));
                c.Hints.Add("Aim through the window. The beam goes where the portals send it.");
                return Floor;
            }
            case Room.GelIsland:
            {
                // jump onto a gel island in the goo and let it throw you across
                b.Fill(x0 + 8, 20, x0 + 11, 21, '~');
                b.Fill(x0 + 12, 20, x0 + 14, 20, 'J');
                b.Fill(x0 + 15, 20, x0 + 19, 21, '~');
                c.Steps.Add(bot => Seq(bot.WalkTo((x0 + 8) * T - 18),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > (x0 + 20) * T + 4)));
                c.Hints.Add("Keep moving while you bounce.");
                return Floor;
            }
        }
        return Floor;
    }
}
