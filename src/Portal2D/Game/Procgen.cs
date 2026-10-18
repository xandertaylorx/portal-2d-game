using System.Numerics;
using Portal2D.Core;
using Portal2D.Testing;
using static Portal2D.Testing.Bot;

namespace Portal2D.Game;

/// <summary>
/// Procedural "Random Test" chambers.
///
/// A level is a row of rooms separated by one-tile walls. Each room is a randomised puzzle archetype
/// (goo crossing, high ledge, cube + button, laser redirect, fling, laser umbrella, gel bounce...).
/// Every archetype also emits the inputs that solve it, so after generation the whole level is played
/// headlessly by <see cref="Bot"/> using the real physics. Only levels the bot finishes are handed to
/// the player - if a roll fails, the next sub-seed is tried.
/// </summary>
public static class Procgen
{
    const int H = 22, Floor = 19;
    const int T = K.Tile;
    const PortalColor Blue = PortalColor.Blue, Orange = PortalColor.Orange;

    public sealed class Result
    {
        public required LevelDef Def;
        public required Func<Bot, IEnumerable<InputState>> Solution;
        public int Seed;
        public int Attempts;
    }

    delegate IEnumerable<InputState> Step(Bot b);

    public enum Room { CeilCross, HighLedge, CubeCross, Fling, Laser, Umbrella, Gel }

    /// <summary>Debug: force every room to this archetype.</summary>
    public static Room? Force;

    public static string Diagnose(int seeds)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var room in Enum.GetValues<Room>())
        {
            Force = room;
            int ok = 0;
            for (int sd = 1; sd <= seeds; sd++) if (Verify(Build(sd, 2, 0))) ok++;
            sb.AppendLine($"{room,-10} {ok}/{seeds}");
        }
        Force = null;
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

    /// <summary>Generate a level for a seed, retrying sub-seeds until the bot can solve it.</summary>
    public static Result Generate(int seed, int difficulty)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var r = Build(seed, difficulty, attempt);
            if (Verify(r)) { r.Attempts = attempt + 1; return r; }
        }
        // extremely unlikely: fall back to the simplest possible chamber
        return Build(seed, 0, 999);
    }

    public static bool Verify(Result r)
    {
        var w = new World(r.Def);
        var bot = new Bot(w);
        using var e = r.Solution(bot).GetEnumerator();
        for (float t = 0; t < 120; t += 1 / 60f)
        {
            var input = e.MoveNext() ? e.Current : bot.In();
            TestRunner.StepFrame(w, input);
            if (w.Player!.Won) return true;
            if (w.Player.Dead) return false;
        }
        return false;
    }

    static Result Build(int seed, int difficulty, int attempt)
    {
        var rng = new Random(HashCode.Combine(seed, attempt, 0x5eed));
        int rooms = Math.Clamp(2 + difficulty / 2, 2, 5);

        // choose a sequence of archetypes (no immediate repeats; some need a floor-level entrance)
        var plan = new List<Room>();
        var all = Enum.GetValues<Room>();
        int entry = Floor;
        var widths = new List<int>();
        var exits = new List<int>();
        for (int i = 0; i < rooms; i++)
        {
            Room pick;
            int guard = 0;
            do pick = Force ?? all[rng.Next(all.Length)];
            while (Force == null && guard++ < 50 && ((plan.Count > 0 && plan[^1] == pick) || (NeedsFloorEntry(pick) && entry != Floor)
                                    || (difficulty == 0 && pick is Room.Fling or Room.Gel)
                                    || (Gated(pick) && plan.Count(Gated) >= 3)));
            plan.Add(pick);
            entry = pick is Room.HighLedge or Room.Gel ? -1 : Floor; // exit height decided when building
        }

        int width = 1 + 5 + 1 + plan.Sum(p => RoomWidth(p) + 1) + 6 + 1;
        var b = new LevelBuilder(width, H);
        var ctx = new Ctx { B = b, Rng = rng };

        // start room
        b.Fill(1, 1, 5, Floor, ' ');
        b.Set(2, Floor, 'P');
        int x0 = 7;
        int entryRow = Floor;
        Separator(b, 6, entryRow, null);

        foreach (var room in plan)
        {
            int w = RoomWidth(room);
            b.Fill(x0, 1, x0 + w - 1, Floor, ' ');
            int exitRow = BuildRoom(ctx, room, x0, w, entryRow);
            int? doorGroup = room is Room.CubeCross or Room.Laser or Room.Umbrella ? (ctx.Group - 1) % 3 : null;
            Separator(b, x0 + w, exitRow, doorGroup);
            x0 += w + 1;
            entryRow = exitRow;
        }

        // exit room
        b.Fill(x0, 1, x0 + 5, Floor, ' ');
        b.Set(x0 + 3, Floor, 'E');
        int exitX = x0 + 3;
        ctx.Steps.Add(bot => bot.WalkTo(X(exitX), hopCubes: true, timeout: 20));

        var hints = ctx.Hints.Distinct().Take(2).ToArray();
        var def = new LevelDef
        {
            Name = $"Random Test {seed}",
            Map = b.Build(),
            Hints = hints,
            Fling = ctx.Fling,
            Label = "??"
        };
        var steps = ctx.Steps.ToList();
        return new Result
        {
            Def = def,
            Seed = seed,
            Solution = bot => steps.SelectMany(s => s(bot))
        };
    }

    static bool Gated(Room r) => r is Room.CubeCross or Room.Laser or Room.Umbrella;

    static bool NeedsFloorEntry(Room r) => r is Room.Fling or Room.Laser;

    static int RoomWidth(Room r) => r switch
    {
        Room.CeilCross => 22,
        Room.HighLedge => 18,
        Room.CubeCross => 24,
        Room.Fling => 26,
        Room.Laser => 22,
        Room.Umbrella => 20,
        Room.Gel => 20,
        _ => 20
    };

    /// <summary>Wall between rooms with a 2-high opening at the exit height (or a door at floor level).</summary>
    static void Separator(LevelBuilder b, int x, int exitRow, int? doorGroup)
    {
        // the column is still solid from the initial fill (and may hold a receiver) - only cut the opening
        if (doorGroup is int g) b.Fill(x, Floor - 3, x, Floor, (char)('a' + g));
        else b.Fill(x, exitRow - 1, x, exitRow, ' ');
    }

    static IEnumerable<InputState> Through(Bot bot, float x, Func<bool> tele)
    {
        foreach (var i in bot.WalkTo(x, stopIf: tele)) yield return i;
        foreach (var i in bot.Until(tele)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.OnGround, 4)) yield return i;
    }

    /// <summary>Builds one room and appends its solution. Returns the row the player leaves at.</summary>
    static int BuildRoom(Ctx c, Room room, int x0, int w, int entryRow)
    {
        var b = c.B;
        var rng = c.Rng;
        int n = 0;
        Func<bool> MarkTele(Bot bot) { n = bot.Player.Teleports; return () => bot.Player.Teleports > n; }
        // every room starts by walking a little way in (dropping down from a high doorway if needed)
        c.Steps.Add(bot => bot.WalkTo(X(x0 + 1), hopCubes: true, timeout: 15));

        switch (room)
        {
            case Room.CeilCross:
            {
                int gooEnd = x0 + w - 7;
                b.Fill(x0 + 2, 20, x0 + 5, 20, '#');
                b.Fill(x0 + 7, 20, gooEnd, 21, '~');
                b.Fill(x0 + w - 6, 0, x0 + w - 2, 0, '#');
                if (rng.Next(2) == 0) b.Fill(x0 + 10, 1, x0 + 11, 5, 'X'); // a hanging pillar
                int ceil = x0 + w - 5;
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 3, 20)), bot.Fire(Orange, Ceil(ceil, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 4) * T, tele); });
                c.Hints.Add("Goo below. Find a way over it.");
                return Floor;
            }
            case Room.HighLedge:
            {
                int top = rng.Next(8, 13);
                b.Fill(x0 + 9, top, x0 + w - 1, Floor, 'X');
                b.Fill(x0 + 2, 20, x0 + 6, 20, '#');
                b.Fill(x0 + 11, 0, x0 + w - 3, 0, '#');
                c.Steps.Add(bot => Seq(bot.Fire(Blue, Bot.Floor(x0 + 3, 20)), bot.Fire(Orange, Ceil(x0 + 12, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 4) * T, tele); });
                c.Hints.Add("Up is just another direction.");
                return top - 1;
            }
            case Room.CubeCross:
            {
                int g = c.Group++ % 3;
                b.Set(x0 + 3, Floor, 'C');
                b.Fill(x0 + 5, 20, x0 + 7, 20, '#');
                b.Fill(x0 + 9, 20, x0 + w - 8, 21, '~');
                b.Fill(x0 + w - 7, 0, x0 + w - 4, 0, '#');
                b.Set(x0 + w - 5, Floor, (char)('1' + g));
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
                b.Fill(x0 - 1, 7, x0 - 1, 12, '#');      // launch face on the room's left wall
                b.Fill(x0 + 5, 5, x0 + 9, 5, 'X');        // shelf, detached from the wall
                b.Fill(x0 + 5, 0, x0 + 9, 0, '#');
                b.Fill(x0 + 1, 20, x0 + 3, 20, '#');
                b.Fill(x0 + 11, 20, x0 + 18, 21, '~');
                int launchRow = 7 + rng.Next(0, 2);
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0)), bot.Fire(Blue, Bot.Floor(x0 + 1, 20)), bot.Fire(Orange, Ceil(x0 + 5, 0))));
                c.Steps.Add(bot => { var tele = MarkTele(bot); return Through(bot, (x0 + 2) * T, tele); });
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
                int g = c.Group++ % 3;
                int e = rng.Next(6, 10);
                int r = rng.Next(2, 6);
                b.Set(x0 - 1, e, '>');
                b.Fill(x0 + 9, 2, x0 + 10, 14, 'X');
                b.Fill(x0 + 9, e - 2, x0 + 9, e + 3, '#');
                b.Fill(x0 + 10, 1, x0 + 10, 7, '#');
                if (rng.Next(2) == 0) b.Fill(x0 + 16, 1, x0 + 16, 14, '=');
                b.Set(x0 + w, r, (char)('4' + g));
                c.Steps.Add(bot => Seq(bot.WalkTo(X(x0 + 2)), bot.Fire(Blue, WallL(x0 + 9, e)), bot.WalkTo(X(x0 + 14)),
                    bot.Fire(Orange, WallR(x0 + 10, r)), bot.Wait(0.6f)));
                c.Hints.Add("Redirect the beam into the receiver.");
                return Floor;
            }
            case Room.Umbrella:
            {
                int g = c.Group++ % 3;
                b.Set(x0 + 3, Floor, 'C');
                b.Set(x0 + 8, 0, 'v');
                if (rng.Next(2) == 0) b.Set(x0 + 13, 0, 'v');
                b.Set(x0 + 17, Floor, (char)('1' + g));
                c.Steps.Add(bot => Seq(bot.Use(), bot.WalkTo((x0 + 16) * T + 8, aimRel: new Vector2(0, -400)),
                    bot.Drop(new Vector2((x0 + 20) * T, 700)), bot.Wait(0.5f)));
                c.Hints.Add("Cubes block lasers. Hold one over your head.");
                return Floor;
            }
            case Room.Gel:
            {
                int top = rng.Next(6, 9);
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
        }
        return Floor;
    }
}
