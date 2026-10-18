using System.Numerics;
using Portal2D.Core;
using Portal2D.Game;
using static Portal2D.Testing.Bot;

namespace Portal2D.Testing;

/// <summary>Reference solutions for every chamber, used to prove each level is solvable.</summary>
public static class Solutions
{
    const PortalColor Blue = PortalColor.Blue, Orange = PortalColor.Orange;

    public static IEnumerable<InputState> For(int level, Bot b)
    {
        var p = b.Player;
        int n = 0;
        IEnumerable<InputState> Mark() => b.Do(() => n = p.Teleports);
        bool Tele() => p.Teleports > n;
        IEnumerable<InputState> Land(float t = 3) => b.Until(() => p.OnGround && p.Teleports > 0, t);

        return level switch
        {
            0 => b.WalkTo(X(36)),
            1 => Seq(b.Fire(Blue, WallR(0, 18)), Mark(), b.Move(-1, Tele), b.WalkTo(X(31))),
            2 => Seq(b.Fire(Blue, WallR(0, 18)), b.Fire(Orange, Ceil(30, 0)), Mark(), b.Move(-1, Tele),
                     Land(), b.WalkTo(X(36))),
            3 => Seq(b.Fire(Blue, Floor(12, 20)), b.Fire(Orange, Ceil(3, 0)), Mark(), b.WalkTo(416, stopIf: Tele),
                     b.Until(Tele), Land(), b.Use(), b.Fire(Blue, Ceil(1, 0)),
                     b.AimAt(new Vector2(900, 640)), b.WalkTo(700), b.Drop(new Vector2(760, 640)), b.Wait(0.5f),
                     b.WalkTo(X(36), hopCubes: true)),
            4 => Seq(b.Fire(Blue, WallR(0, 16)), b.Fire(Orange, Ceil(32, 0)), b.WalkTo(170), b.Use(),
                     b.AimAt(new Vector2(0, 540)), Mark(), b.Move(-1, Tele), Land(), b.WalkTo(1100),
                     b.Drop(new Vector2(1040, 600)), b.Wait(0.5f), b.WalkTo(X(38))),
            5 => Seq(b.WalkTo(104), b.Fire(Orange, WallR(0, 7)), b.WalkTo(262), b.Fire(Blue, Floor(10, 20)),
                     Mark(), b.Move(1, () => !p.OnGround), b.Steer(352, Tele), b.Until(() => p.OnGround, 4), b.WalkTo(X(37))),
            6 => Seq(b.Fire(Blue, Floor(10, 20)), b.Fire(Orange, Ceil(27, 0)), b.WalkTo(230), b.Use(),
                     b.AimAt(new Vector2(330, 640)), Mark(), b.WalkTo(352, stopIf: Tele), b.Until(Tele), Land(),
                     b.WalkTo(960), b.Drop(new Vector2(900, 650)), b.Wait(0.5f), b.WalkTo(X(37))),
            7 => Seq(b.Fire(Blue, WallL(20, 10)), b.WalkTo(800), b.Fire(Orange, WallR(21, 4)), b.Wait(0.6f), b.WalkTo(X(37))),
            8 => Seq(b.Use(), b.Fire(Blue, WallR(0, 18)), b.Fire(Orange, Floor(16, 20)), b.AimAt(new Vector2(0, 600)),
                     Mark(), b.Move(-1, Tele), b.Until(() => p.OnGround && p.Pos.X > 590, 3, mx: 1),
                     b.WalkTo(740), b.Drop(new Vector2(800, 650)), b.Wait(0.3f),
                     b.Fire(Blue, Floor(6, 20)), b.Fire(Orange, WallR(0, 5)), b.Wait(0.6f), b.WalkTo(X(37), hopCubes: true)),
            9 => Seq(b.WalkTo(230), b.Fire(Blue, Floor(9, 20)), b.Fire(Orange, Floor(30, 20)), Mark(),
                     b.Move(1, () => !p.OnGround), b.Steer(320, Tele), b.Until(() => p.Pos.Y < 250, 2),
                     b.Until(() => p.OnGround, 3, mx: 1), b.WalkTo(X(37))),
            10 => Seq(b.WalkTo(440), b.Fire(Blue, Floor(10, 20)), b.Fire(Orange, Ceil(31, 0)), Mark(),
                      b.WalkTo(352, stopIf: Tele), b.Until(Tele), b.Until(() => p.OnGround && p.Pos.X > 900, 3),
                      b.WalkTo(1090), b.Use(), b.WalkTo(905), b.Fire(Blue, Floor(30, 12)), b.Fire(Orange, Floor(2, 20)),
                      b.AimAt(new Vector2(1100, 356)), Mark(), b.WalkTo(992, stopIf: Tele), b.Until(Tele),
                      b.Until(() => p.OnGround, 3, mx: 1),
                      b.WalkTo(215), b.Drop(new Vector2(150, 650)), b.Wait(0.5f), b.WalkTo(440),
                      b.Fire(Orange, Ceil(7, 0)), b.Fire(Blue, Floor(11, 20)), Mark(), b.WalkTo(384, stopIf: Tele),
                      b.Until(Tele), b.Until(() => p.OnGround, 3), b.WalkTo(X(2))),
            11 => Seq(b.Fire(Blue, WallR(0, 18)), b.Fire(Orange, Ceil(22, 0)), Mark(), b.Move(-1, Tele),
                      b.Until(() => p.Vel.Y < -300 && p.Pos.Y < 320, 4), b.Until(() => p.OnGround, 4, mx: 1), b.WalkTo(X(36))),
            12 => Seq(b.WalkTo(150), b.Use(), b.WalkTo(420), b.Drop(new Vector2(380, 640)), b.Wait(0.3f),
                      b.Fire(Blue, Floor(16, 20)), b.Fire(Orange, Ceil(32, 0)), Mark(), b.WalkTo(544, stopIf: Tele),
                      b.Until(Tele), b.Until(() => p.OnGround, 3), b.Use(), b.WalkTo(880), b.Drop(new Vector2(800, 650)),
                      b.Wait(0.6f), b.WalkTo(X(38))),
            13 => Seq(b.WalkTo(270), b.JumpTo(1, () => p.OnGround && p.Pos.X > 680), b.Fire(Blue, Floor(24, 20)),
                      b.Fire(Orange, Ceil(33, 0)), Mark(), b.WalkTo(800, stopIf: Tele), b.Until(Tele),
                      b.Until(() => p.OnGround, 3), b.WalkTo(X(36))),
            14 => Seq(b.Fire(Blue, Floor(9, 20)), b.Fire(Orange, Ceil(3, 0)), Mark(), b.WalkTo(320, stopIf: Tele),
                      b.Until(Tele), b.Until(() => p.OnGround, 3), b.Use(), b.Fire(Blue, Ceil(1, 0)),
                      b.WalkTo(940, aimRel: new Vector2(0, -400)), b.Drop(new Vector2(1100, 700)), b.Wait(0.6f),
                      b.WalkTo(X(37), hopCubes: true)),
            15 => Seq(b.Fire(Blue, WallL(20, 9)), b.WalkTo(800), b.Fire(Orange, WallR(21, 3)), b.Wait(0.6f), b.WalkTo(X(37))),
            16 => Seq(b.WalkTo(150), b.Use(), b.Fire(Blue, Floor(8, 20)), b.Fire(Orange, Ceil(16, 0)),
                      b.AimAt(new Vector2(400, 700)), Mark(), b.WalkTo(288, stopIf: Tele), b.Until(Tele),
                      b.Until(() => p.OnGround, 3), b.WalkTo(940, aimRel: new Vector2(0, -400)),
                      b.Drop(new Vector2(1100, 700)), b.Wait(0.6f), b.WalkTo(X(37), hopCubes: true)),
            17 => Seq(b.Fire(Blue, Ceil(6, 0)), b.WalkTo(760), b.Fire(Orange, WallR(20, 11)), b.Wait(0.6f), b.WalkTo(X(37))),
            18 => Seq(b.WalkTo(104), b.Fire(Orange, WallR(0, 7)), b.WalkTo(262), b.Fire(Blue, Floor(10, 20)),
                      Mark(), b.Move(1, () => !p.OnGround), b.Steer(352, Tele), b.Until(() => p.OnGround, 4), b.WalkTo(X(37))),
            19 => Seq(b.WalkTo(X(8) - 44), b.Use(), b.Fire(Blue, WallR(0, 18)), b.Fire(Orange, Ceil(22, 0)),
                      b.AimAt(new Vector2(-3000, 600)), Mark(), b.Move(-1, Tele), b.AimAt(new Vector2(5000, -3000)),
                      b.Until(() => p.Vel.Y < -300 && p.Pos.Y < 320, 4), b.Until(() => p.OnGround, 4, mx: 1),
                      b.WalkTo(1010), b.Drop(new Vector2(900, 260)), b.Wait(0.6f), b.WalkTo(X(37))),
            20 => Seq(b.Fire(Blue, Floor(8, 24)), b.Fire(Orange, Ceil(3, 0)), Mark(), b.WalkTo(288, stopIf: Tele),
                      b.Until(Tele), b.Until(() => p.OnGround, 3), b.Use(), b.Fire(Blue, Ceil(1, 0)),
                      b.AimAt(new Vector2(900, 780)), b.WalkTo(450), b.Drop(new Vector2(520, 780)), b.Wait(0.6f),
                      b.WalkTo(1150, hopCubes: true), b.Fire(Blue, WallR(19, 4)), b.Fire(Orange, WallR(19, 8)), b.Wait(0.6f),
                      b.WalkTo(1300), b.Fire(Blue, Floor(41, 24)), b.Fire(Orange, Ceil(44, 0)), Mark(),
                      b.WalkTo(1344, stopIf: Tele), b.Until(Tele), b.Until(() => p.OnGround, 3),
                      b.WalkTo(1390), b.Fire(Orange, WallR(39, 13)), Mark(), b.Move(-1, () => !p.OnGround),
                      b.Steer(1344, Tele), b.Until(() => p.OnGround, 4), b.WalkTo(X(57))),
            _ => b.Wait(1)
        };
    }
}

public static class TestRunner
{
    /// <summary>Plays every chamber headlessly with its reference solution. Returns process exit code.</summary>
    public static int Run(bool verbose, int only = -1)
    {
        int failures = CheckPortalMath();
        for (int i = 0; i < Levels.All.Length; i++)
        {
            if (only >= 0 && i != only) continue;
            var def = Levels.All[i];
            var w = new World(def);
            var bot = new Bot(w);
            using var e = Solutions.For(i, bot).GetEnumerator();
            float t = 0;
            string result = "TIMEOUT";
            int lastPortals = -1;
            while (t < 90)
            {
                var input = e.MoveNext() ? e.Current : bot.In();
                StepFrame(w, input);
                t += 1 / 60f;
                var p = w.Player!;
                if (verbose && w.PortalsPlaced != lastPortals)
                {
                    lastPortals = w.PortalsPlaced;
                    Console.WriteLine($"    t={t:0.00} portals: B={Describe(w.Portals[0])} O={Describe(w.Portals[1])}");
                }
                if (verbose && (int)(t * 60) % 15 == 0)
                    Console.WriteLine($"    t={t:0.00} p=({p.Pos.X / K.Tile:0.00},{p.Pos.Y / K.Tile:0.00}) v=({p.Vel.X:0},{p.Vel.Y:0}) g={p.OnGround} hold={p.Holding != null} " +
                        string.Join(" ", w.Cubes.Select(c => $"cube=({c.Pos.X / K.Tile:0.00},{c.Pos.Y / K.Tile:0.00}){(c.Held ? "H" : "")}{(c.Dissolving ? "D" : "")}")));
                if (p.Won) { result = $"SOLVED in {t:0.0}s"; break; }
                if (p.Dead) { result = $"DIED at ({p.Pos.X / K.Tile:0.0},{p.Pos.Y / K.Tile:0.0}) t={t:0.0}s"; break; }
            }
            var pl = w.Player!;
            if (!result.StartsWith("SOLVED"))
            {
                failures++;
                result += $"  player=({pl.Pos.X / K.Tile:0.0},{pl.Pos.Y / K.Tile:0.0}) vel=({pl.Vel.X:0},{pl.Vel.Y:0}) doors=[{string.Join(",", w.DoorOpen.Select(d => d.ToString("0.0")))}] " +
                          $"B={Describe(w.Portals[0])} O={Describe(w.Portals[1])}";
            }
            Console.WriteLine($"{i + 1,2}. {def.Name,-18} {result}");
        }
        Console.WriteLine(failures == 0 ? "All chambers solvable." : $"{failures} chamber(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The render matrix (see-through view, clones) must agree with the teleport transform.</summary>
    static int CheckPortalMath()
    {
        Vector2[] normals = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
        var rng = new Random(3);
        Span<float> m = stackalloc float[16];
        int bad = 0;
        foreach (var na in normals)
        foreach (var nb in normals)
        {
            var a = new Portal(PortalColor.Blue, new Vector2(rng.Next(0, 900), rng.Next(0, 600)), na, default, default, false);
            var b = new Portal(PortalColor.Orange, new Vector2(rng.Next(0, 900), rng.Next(0, 600)), nb, default, default, false);
            Portal.Matrix(a, b, m);
            for (int k = 0; k < 5; k++)
            {
                var pt = new Vector2(rng.Next(-300, 300), rng.Next(-300, 300)) + a.Center;
                var viaMatrix = new Vector2(m[0] * pt.X + m[4] * pt.Y + m[12], m[1] * pt.X + m[5] * pt.Y + m[13]);
                var viaMap = Portal.MapPoint(a, b, pt);
                var back = Portal.MapPoint(b, a, viaMap);
                if (Vector2.Distance(viaMatrix, viaMap) > 0.01f || Vector2.Distance(back, pt) > 0.01f) bad++;
            }
            // walking into A must come out of B moving away from B
            var outDir = Portal.MapDir(a, b, -a.Normal);
            if (Vector2.Distance(outDir, b.Normal) > 1e-4f) bad++;
            // wall portals never flip "up"
            if (a.IsWall && b.IsWall && Vector2.Dot(Portal.MapDir(a, b, new Vector2(0, -1)), new Vector2(0, -1)) < 0.99f) bad++;
        }
        Console.WriteLine(bad == 0 ? "Portal transform math: OK" : $"Portal transform math: {bad} FAILURES");
        return bad == 0 ? 0 : 1;
    }

    static string Describe(Portal? p) => p == null ? "-" : $"({p.TileA.X},{p.TileA.Y})-({p.TileB.X},{p.TileB.Y}) n=({p.Normal.X},{p.Normal.Y})";

    public static void StepFrame(World w, InputState input)
    {
        w.Update(K.SimDt, input);
        input.JumpPressed = input.UsePressed = input.FireBlue = input.FireOrange = false;
        w.Update(K.SimDt, input);
        w.Events.Clear();
    }
}
