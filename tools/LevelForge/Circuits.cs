using System.Numerics;
using Portal2D.Core;
using Portal2D.Game;
using Portal2D.Testing;
using static Portal2D.Testing.Bot;

// Connected objectives: these are authored puzzles, not dimensional variants of traversal rooms.
static class Circuits
{
    const PortalColor Blue = PortalColor.Blue, Orange = PortalColor.Orange;
    static readonly Vector2 Right = new(120, 0), Left = new(-120, 0);
    public static readonly string[] Names = ["Light Well", "About Turn", "Dry Dock", "Freight Lift",
        "Beam Split", "Flyover", "Closing Time", "Rebound"];

    static IEnumerable<InputState> Enter(Bot bot, float x, int count, float? land = null)
    {
        foreach (var i in bot.WalkTo(x, stopIf: () => bot.Player.Teleports >= count)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.Teleports >= count)) yield return i;
        if (land is float lx) foreach (var i in bot.Steer(lx, () => bot.Player.OnGround)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.OnGround)) yield return i;
    }

    static IEnumerable<InputState> Place(Bot bot, int col, int row, int side = -1) => Seq(
        bot.WalkTo(X(col) + side * 44, aimRel: new(-side * 40, 60)),
        bot.Drop(new(X(col), (row + 1) * 32 + 60)), bot.Wait(0.7f));

    public static LevelDef Build(int kind)
    {
        var b = LevelBuilder.Room(48, 22);
        Func<Bot, IEnumerable<InputState>> solution;
        string[] hints;
        switch (kind)
        {
            case 0: // Two high stores, two weights on one circuit. Neither cube can substitute for the other.
            {
                b.Fill(1, 9, 9, 10, 'X').Fill(29, 12, 40, 13, 'X');
                b.Fill(2, 0, 7, 0, '#').Fill(30, 0, 36, 0, '#');
                b.Fill(12, 20, 16, 20, '#').Fill(24, 20, 27, 20, '#');
                b.Set(5, 8, 'C').Set(34, 11, 'C').Set(18, 19, '1').Set(24, 19, '1');
                b.Fill(43, 1, 43, 15, 'X').Fill(43, 16, 43, 19, 'a');
                b.Set(20, 19, 'P').Set(46, 19, 'E');
                solution = bot => Seq(bot.Fire(Orange, Ceil(4, 0)), bot.Fire(Blue, Floor(13, 20)),
                    Enter(bot, 14 * 32, 1), bot.WalkTo(X(5) - 30), bot.Use(), bot.Fire(Blue, Ceil(2, 0)),
                    Place(bot, 18, 19),
                    bot.WalkTo(X(23)), bot.Fire(Orange, Ceil(32, 0)), bot.Fire(Blue, Floor(25, 20)),
                    Enter(bot, 26 * 32, 2), bot.WalkTo(X(34) - 30), bot.Use(), bot.Fire(Blue, Ceil(35, 0)),
                    Place(bot, 24, 19, 1),
                    bot.WalkTo(X(46), hopCubes: true));
                hints = ["Both weights must stay on the circuit.", "Plan where you will land before recovering each cube."];
                break;
            }
            case 1: // Open the store with light, recover a cube, then repurpose the beam for the exit.
            {
                b.Fill(4, 1, 4, 15, 'X').Fill(4, 16, 4, 19, 'b');
                b.Fill(18, 1, 18, 15, 'X').Fill(18, 16, 18, 19, 'a');
                b.Set(5, 5, '>').Fill(18, 4, 18, 7, '#');
                b.Fill(7, 0, 9, 0, '#').Fill(14, 0, 16, 0, '#');
                b.Set(15, 9, '4').Set(8, 11, '5').Set(10, 19, '2');
                b.Fill(26, 12, 37, 13, 'X').Set(20, 20, 'Q').Set(30, 11, 'C');
                b.Set(7, 19, 'P').Set(2, 19, 'E');
                solution = bot => Seq(bot.Fire(Orange, Ceil(15, 0)), bot.WalkTo(X(14)), bot.Fire(Blue, WallL(18, 4)), bot.Wait(0.6f),
                    bot.Move(1, () => !bot.Player.OnGround, 8), bot.Move(1, () => bot.Player.OnGround, 4),
                    bot.WalkTo(X(30) - 30), bot.Use(), bot.WalkTo(X(24), aimRel: Left),
                    bot.Until(() => bot.Player.OnGround, 4), bot.JumpTo(-1, () => bot.Player.OnGround && bot.Player.Pos.X < 19 * 32),
                    Place(bot, 10, 19, 1),
                    bot.Fire(Orange, Ceil(8, 0)), bot.Wait(0.6f), bot.WalkTo(X(2), hopCubes: true));
                hints = ["The store and exit need different light paths.", "Recover the cargo before changing the beam's destination."];
                break;
            }
            case 2: // Two islands each hold a required cube; no one-shot teleport to the exit can win.
            {
                b.Fill(12, 20, 45, 21, '~').Fill(20, 20, 25, 21, 'X').Fill(36, 20, 41, 21, 'X');
                b.Fill(7, 20, 10, 20, '#').Fill(6, 0, 10, 0, '#');
                foreach (int x in new[] { 20, 36 }) b.Fill(x, 20, x + 3, 20, '#').Fill(x, 0, x + 4, 0, '#').Set(x + 2, 19, 'C');
                b.Set(6, 19, '1').Set(10, 19, '1');
                b.Fill(4, 1, 4, 15, 'X').Fill(4, 16, 4, 19, 'a');
                b.Set(8, 19, 'P').Set(2, 19, 'E');
                solution = bot => Recover(bot);
                hints = ["The dock needs both weights returned.", "Leave a safe landing zone beside the controls."];
                break;
            }
            case 3: // Freight down, player up, then use the same portal pair to power the upper exit.
            {
                b = LevelBuilder.Room(40, 22);
                b.Fill(1, 13, 38, 14, 'X').Fill(8, 13, 11, 14, ' ');
                b.Fill(3, 20, 4, 20, '#').Fill(16, 0, 17, 0, '#').Fill(25, 0, 27, 0, '#');
                b.Fill(34, 1, 34, 8, 'X').Fill(34, 9, 34, 12, 'b');
                b.Set(28, 19, '1').Set(24, 12, 'C').Set(26, 7, '5');
                b.Set(30, 10, 'a');
                b.Set(33, 10, '<').Fill(0, 9, 0, 12, '#');
                b.Fill(15, 9, 33, 9, 'X');
                b.Fill(34, 5, 34, 12, 'b');
                b.Set(2, 19, 'P').Set(37, 12, 'E');
                solution = bot => Seq(bot.WalkTo(8 * 32 + 14), bot.Fire(Orange, Ceil(16, 0)), bot.Fire(Blue, Floor(3, 20)),
                    Enter(bot, 4 * 32, 1), bot.WalkTo(X(13)), bot.Until(() => bot.Player.OnGround && bot.Player.Pos.Y > 10 * 32),
                    bot.WalkTo(X(24) - 40), bot.Use(),
                    bot.WalkTo(12 * 32 + 20, aimRel: Left), bot.Use(), bot.Wait(0.8f),
                    bot.WalkTo(X(10)), bot.Until(() => bot.Player.OnGround && bot.Player.Pos.Y > 15 * 32),
                    bot.WalkTo(X(11)), bot.Use(), bot.WalkTo(X(26), aimRel: Right),
                    bot.Drop(new(X(29), 700)), bot.Wait(0.6f), Enter(bot, 4 * 32, 2),
                    bot.Fire(Orange, Ceil(26, 0)), bot.Fire(Blue, WallR(0, 9)), bot.Wait(0.6f), bot.WalkTo(X(37)));
                hints = ["The upper exit needs weight below and light above.", "Finish the transport job before borrowing its portals for the beam."];
                break;
            }
            case 4: // Two mirror cubes on a gantry must split two beams in opposite directions.
            {
                b = LevelBuilder.Room(40, 22);
                b.Set(5, 19, 'N').Set(3, 19, 'M').Set(6, 20, 'Q');
                b.Fill(13, 14, 29, 15, 'X').Set(17, 0, 'v').Set(24, 0, 'v');
                b.Set(20, 13, '4').Set(21, 13, '4');
                b.Fill(33, 1, 33, 15, 'X').Fill(33, 16, 33, 19, 'a');
                b.Set(4, 19, 'P').Set(37, 19, 'E');
                solution = bot => Seq(bot.WalkTo(X(5) - 26), bot.Use(),
                    bot.Move(1, () => !bot.Player.OnGround, aimRel: Right), bot.Move(1, () => bot.Player.OnGround, 4, Right),
                    bot.WalkTo(X(17) - 36, aimRel: Right), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(120, -2))), bot.Wait(0.6f),
                    bot.WalkTo(X(11)), bot.Until(() => bot.Player.OnGround), bot.WalkTo(X(8)),
                    bot.JumpTo(-1, () => bot.Player.OnGround && bot.Player.Pos.X < 5 * 32),
                    bot.WalkTo(X(3) + 30, aimRel: Left), bot.Use(),
                    bot.Move(1, () => !bot.Player.OnGround, aimRel: Right), bot.Move(1, () => bot.Player.OnGround, 4, Right),
                    bot.WalkTo(X(16)), bot.AimAt(new(X(17), -2000)), bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 22 * 32),
                    bot.WalkTo(X(24) + 50, aimRel: new(0, -120)), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(-120, -2))), bot.Wait(0.6f),
                    bot.WalkTo(X(30) + 20), bot.Until(() => bot.Player.OnGround), bot.WalkTo(X(37)));
                hints = ["Both receivers need light at the same time.", "The two mirrors turn their beams in opposite directions."];
                break;
            }
            case 5: // Carry a shield through a laser gallery, leave it on a switch, then solve the beam route.
            {
                b = LevelBuilder.Room(44, 22);
                b.Set(5, 19, 'C').Set(11, 0, 'v').Set(16, 0, 'v');
                b.Set(22, 19, '1').Fill(26, 1, 26, 15, 'X').Fill(26, 16, 26, 19, 'a');
                b.Set(27, 7, '>').Fill(36, 2, 36, 12, 'X').Fill(36, 6, 36, 9, '#');
                b.Fill(29, 0, 34, 0, '#').Set(31, 11, '5');
                b.Fill(39, 1, 39, 15, 'X').Fill(39, 16, 39, 19, 'b');
                b.Set(3, 19, 'P').Set(41, 19, 'E');
                solution = bot => Seq(bot.WalkTo(X(5) - 30), bot.Use(),
                    bot.WalkTo(X(20), aimRel: new(0, -400)), Place(bot, 22, 19),
                    bot.WalkTo(X(29)), bot.Fire(Orange, Ceil(31, 0)), bot.Fire(Blue, WallL(36, 6)),
                    bot.Wait(0.6f), bot.WalkTo(X(41)));
                hints = ["Your shield is also the key to the next room.", "The last door needs a different kind of power."];
                break;
            }
            case 6: // Two timers at opposite ends, one shared door. Set a shortcut before starting the clock.
            {
                b = LevelBuilder.Room(58, 22);
                b.Set(4, 19, 'T').Set(39, 19, 'T');
                b.Fill(7, 20, 10, 20, '#').Fill(35, 0, 38, 0, '#');
                b.Fill(18, 1, 20, 15, 'X').Fill(18, 3, 20, 11, ' ').Fill(18, 16, 20, 19, '|');
                b.Fill(44, 1, 44, 15, 'X').Fill(44, 16, 44, 19, 'a');
                b.Set(2, 19, 'P').Set(53, 19, 'E');
                solution = bot => Seq(bot.Fire(Orange, Ceil(36, 0)), bot.Fire(Blue, Floor(8, 20)),
                    bot.WalkTo(X(4)), bot.Wait(0.1f), Enter(bot, 9 * 32, 1, X(38)),
                    bot.WalkTo(X(39)), bot.WalkTo(X(53)));
                hints = ["Both timers must overlap.", "Prepare a route before starting the first timer."];
                break;
            }
            default: // Keep a cube through a bounce, then combine its weight with an upper timer.
            {
                b = LevelBuilder.Room(50, 22);
                b.Fill(0, 16, 0, 19, '#').Fill(21, 0, 25, 0, '#').Fill(20, 20, 25, 20, 'J');
                b.Fill(28, 9, 48, 19, 'X').Set(32, 8, '1');
                b.Fill(37, 1, 37, 4, 'X').Fill(37, 5, 37, 8, 'a');
                b.Set(4, 19, 'P').Set(8, 19, 'C').Set(45, 8, 'E');
                // A separate timer on the approach prevents simply throwing the cube onto the switch and waiting.
                b.Set(35, 8, 'T');
                solution = bot => Seq(bot.WalkTo(X(8) - 44), bot.Use(), bot.Fire(Blue, WallR(0, 18)), bot.Fire(Orange, Ceil(22, 0)),
                    bot.AimAt(new(-3000, 600)), bot.Move(-1, () => bot.Player.Teleports > 0),
                    bot.AimAt(new(5000, -3000)), bot.Until(() => bot.Player.Vel.Y < -300 && bot.Player.Pos.Y < 320, 4),
                    bot.Until(() => bot.Player.OnGround, 4, mx: 1), Place(bot, 32, 8, 1),
                    bot.WalkTo(X(35), hopCubes: true), bot.WalkTo(X(45)));
                hints = ["Keep the cargo through the bounce.", "A weight and a timer share the upper door."];
                break;
            }
        }
        return new LevelDef { Name = Names[kind], Map = b.Build(), Solution = solution, Hints = hints, Theme = 1 + kind % 6 };
    }

    static IEnumerable<InputState> Recover(Bot bot)
    {
        int count = 0;
        foreach (int island in new[] { 36, 20 }) {
            foreach (var i in Seq(bot.Fire(Orange, Ceil(island + 1, 0)), bot.Fire(Blue, Floor(8, 20)),
                Enter(bot, 9 * 32, ++count, X(island + 4)), bot.WalkTo(X(island + 2) - 40), bot.Use(),
                bot.Fire(Orange, Ceil(7, 0)), bot.Fire(Blue, Floor(island, 20)),
                bot.AimAt(new(X(island + 4), 640)), Enter(bot, (island + 1) * 32, ++count),
                bot.Fire(Blue, Ceil(9, 0)), Place(bot, island == 36 ? 6 : 10, 19, island == 36 ? 1 : -1))) yield return i;
        }
        foreach (var i in bot.WalkTo(X(2), hopCubes: true)) yield return i;
    }
}
