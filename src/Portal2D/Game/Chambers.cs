using System.Numerics;
using Portal2D.Core;
using Portal2D.Testing;
using static Portal2D.Testing.Bot;

namespace Portal2D.Game;

/// <summary>
/// Hand-designed chambers 22+. Every one has its own layout and its own trick, and carries the bot solution that
/// proves it can be finished (checked by <c>--test</c>).
/// </summary>
public static class Chambers
{
    const PortalColor Blue = PortalColor.Blue, Orange = PortalColor.Orange;
    static readonly Int2 Left = new(-1, 0), Right = new(1, 0), Up = new(0, -1), Down = new(0, 1);
    static readonly Vector2 Fwd = new(120, 0), Back = new(-120, 0), Up2 = new(20, -120);

    static LevelDef Def(string name, LevelBuilder b, string[] hints, Func<Bot, IEnumerable<InputState>> solution,
        bool fling = false, int theme = 0) => new()
    {
        Name = name, Map = b.Build(), Hints = hints, Fling = fling, Theme = theme, Solution = solution
    };

    /// <summary>Walk into a portal and keep going until the teleport happens, then land.</summary>
    static IEnumerable<InputState> Enter(Bot bot, float x, Func<bool> tele, float? landX = null)
    {
        foreach (var i in bot.WalkTo(x, stopIf: tele)) yield return i;
        foreach (var i in bot.Until(tele)) yield return i;
        if (landX is float lx)
            foreach (var i in bot.Steer(lx, () => bot.Player.OnGround, 4)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.OnGround, 4)) yield return i;
    }

    public static void Add(List<LevelDef> output)
    {
        var list = new List<LevelDef>();
        // Peephole: the far side of the wall is only visible through the window
        {
            var b = LevelBuilder.Room();
            b.Fill(17, 1, 19, 19, 'X').Fill(17, 3, 19, 8, ' ');
            b.Fill(4, 20, 9, 20, '#');
            b.Fill(21, 0, 28, 0, '#');
            b.Fill(30, 17, 30, 19, 'X');
            b.Set(2, 19, 'P').Set(36, 19, 'E');
            list.Add(Def("Peephole", b,
                ["The wall is solid, but the window isn't. Portal shots fly right through it.",
                 "Put an exit where you can see it, not where you can stand."],
                bot =>
                {
                    int n = 0;
                    bool Tele() => bot.Player.Teleports > n;
                    return Seq(bot.WalkTo(X(7)), bot.Fire(Blue, Floor(4, 20)), bot.Fire(Orange, Ceil(23, 0)),
                        bot.Do(() => n = bot.Player.Teleports), Enter(bot, 5 * 32, Tele), bot.WalkTo(X(36)));
                }));
        }

        // Stool Pigeon: the ledge is one jump too high - unless you stand on the cube
        {
            var b = LevelBuilder.Room();
            b.Fill(26, 16, 38, 19, 'X');
            b.Set(2, 19, 'P').Set(5, 19, 'C').Set(33, 15, 'E');
            list.Add(Def("Stool Pigeon", b,
                ["That ledge is just out of reach. Cubes make a decent step.",
                 "Put the cube against the wall, climb on, then jump again."],
                bot => Seq(bot.WalkTo(150), bot.Use(), bot.WalkTo(26 * 32 - 74, aimRel: Fwd), bot.Use(), bot.Wait(0.6f),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.Y < 610),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 27 * 32), bot.WalkTo(X(33)))));
        }

        // Pinball: nothing but launch plates and a goo floor
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 20, 38, 21, '~');
            b.Fill(28, 20, 38, 21, 'X').Set(28, 20, 'q');
            b.Fill(15, 12, 21, 19, 'X').Set(15, 12, 'q');
            b.Fill(1, 6, 8, 19, 'X');
            b.Set(36, 19, 'P').Set(3, 5, 'E');
            list.Add(Def("Pinball", b,
                ["Orange plates throw you. Left or right, depending on the arrow.",
                 "Keep holding a direction while you fly."],
                bot => Seq(bot.Move(-1, () => !bot.Player.OnGround), bot.Move(-1, () => bot.Player.OnGround, 4),
                    bot.Move(-1, () => !bot.Player.OnGround), bot.Move(-1, () => bot.Player.OnGround, 4), bot.WalkTo(X(3)))));
        }

        // Launch Pad: run on orange gel to clear the goo, then let the plate throw you the rest of the way
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 20, 8, 20, 'G');
            b.Fill(9, 20, 18, 21, '~');
            b.Set(26, 20, 'Q');
            b.Fill(32, 12, 38, 19, 'X');
            b.Set(1, 19, 'P').Set(36, 11, 'E');
            list.Add(Def("Launch Pad", b,
                ["Orange gel makes you fast. Fast enough to clear the goo.",
                 "The next plate does the rest."],
                bot => Seq(bot.Move(1, () => bot.Player.Pos.X >= 9 * 32 + 6, 4),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 19 * 32 + 8),
                    bot.Move(1, () => !bot.Player.OnGround), bot.Move(1, () => bot.Player.OnGround, 4), bot.WalkTo(X(36)))));
        }

        // Moat: the cube is on an island in the goo. Get it, and get it back
        {
            var b = LevelBuilder.Room();
            b.Fill(10, 20, 30, 21, '~').Fill(18, 20, 22, 20, '#').Fill(4, 20, 5, 20, '#');
            b.Fill(16, 0, 21, 0, '#').Fill(30, 0, 35, 0, '#');
            b.Fill(36, 1, 36, 15, 'X').Fill(36, 16, 36, 19, 'a');
            b.Set(33, 19, '1').Set(20, 19, 'C');
            b.Set(2, 19, 'P').Set(38, 19, 'E');
            list.Add(new LevelDef { Name = "Moat", Map = b.Build(), Hints = ["Fetch the cube from the island. Then get it back across.", "Cubes ride portals just like you do."], Solution = bot =>
            {
                int n = 0;
                bool Tele() => bot.Player.Teleports > n;
                IEnumerable<InputState> Mark() => bot.Do(() => n = bot.Player.Teleports);
                return Seq(bot.Fire(Blue, Floor(4, 20)), bot.Fire(Orange, Ceil(18, 0)), Mark(), Enter(bot, 5 * 32, Tele),
                    bot.WalkTo(X(20) - 40), bot.Use(), bot.WalkTo(X(21), aimRel: Fwd),
                    bot.Fire(Blue, Floor(20, 20)), bot.Fire(Orange, Ceil(31, 0)), Mark(),
                    bot.WalkTo(21 * 32 - 4, stopIf: Tele, aimRel: Fwd), bot.Until(Tele),
                    bot.Until(() => bot.Player.OnGround, 4), bot.WalkTo(X(35)), bot.Drop(new Vector2(31 * 32, 700)), bot.Wait(0.5f),
                    bot.WalkTo(X(38), hopCubes: true));
            } });
        }

        // Catapult: the button is out of reach, but a launch plate will throw a cube onto it
        {
            var b = LevelBuilder.Room();
            b.Set(11, 20, 'Q');
            b.Fill(18, 12, 27, 13, 'X').Set(20, 11, '1');
            b.Fill(33, 1, 33, 15, 'X').Fill(33, 16, 33, 19, 'a');
            b.Set(2, 19, 'P').Set(5, 19, 'C').Set(37, 19, 'E');
            list.Add(Def("Catapult", b,
                ["Launch plates throw anything that lands on them. Anything.",
                 "Mind the plate yourself - it doesn't care who steps on it."],
                bot => Seq(bot.WalkTo(150), bot.Use(), bot.WalkTo(330, aimRel: Fwd), bot.Use(), bot.Wait(1.5f),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 13 * 32), bot.WalkTo(X(37), timeout: 20))));
        }

        // Trampoline Park: a run of gel pads over a lake. Just keep going
        {
            var b = LevelBuilder.Room(56, 22);
            b.Fill(8, 20, 54, 21, '~');
            for (int i = 0; i < 5; i++) b.Fill(12 + i * 8, 20, 14 + i * 8, 20, 'J');
            b.Fill(6, 20, 7, 21, 'X').Fill(1, 20, 6, 20, 'X');
            b.Fill(50, 20, 54, 21, 'X');
            b.Set(2, 19, 'P').Set(53, 19, 'E');
            list.Add(Def("Trampoline Park", b,
                ["Blue gel bounces you. Land on it and you go right back up.",
                 "Hold a direction and keep landing on the pads."],
                bot => Seq(bot.WalkTo(7 * 32 + 8), bot.JumpTo(1, () => bot.Player.OnGround, 1.5f),
                    bot.Move(1, () => bot.Player.Pos.X > 50 * 32 && bot.Player.OnGround, 14), bot.WalkTo(X(53)))));
        }

        // Rush Hour: one timed button, three doors, and the clock is running
        {
            var b = LevelBuilder.Room(60, 22);
            b.Set(4, 19, 'T');
            foreach (int dx in new[] { 15, 32 }) b.Fill(dx, 1, dx, 15, 'X').Fill(dx, 16, dx, 19, 'a');
            foreach (int gx in new[] { 22, 36 }) { b.Fill(gx, 20, gx + 3, 21, '~'); b.Fill(gx - 7, 20, gx - 1, 20, 'G'); }
            b.Set(2, 19, 'P').Set(57, 19, 'E');
            list.Add(Def("Rush Hour", b,
                ["The button only holds the doors open for a few seconds after you step off it.",
                 "Orange gel is fast. Fast enough to make the next door."],
                bot => Seq(bot.WalkTo(X(4)), bot.Move(1, () => bot.Player.Pos.X >= 22 * 32 - 6, 8),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 26 * 32),
                    bot.Move(1, () => bot.Player.Pos.X >= 36 * 32 - 6, 8),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 40 * 32),
                    bot.WalkTo(X(57), timeout: 20))));
        }

        // Double Decker: the way up is through the hole in the floor above, as far as you can see through it
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 13, 38, 14, 'X').Fill(8, 13, 11, 14, ' ');
            b.Fill(3, 20, 4, 20, '#').Fill(16, 0, 17, 0, '#');
            b.Fill(34, 1, 34, 8, 'X').Fill(34, 9, 34, 12, 'a');
            b.Set(28, 19, '1').Set(24, 12, 'C');
            b.Set(2, 19, 'P').Set(37, 12, 'E');
            list.Add(Def("Double Decker", b,
                ["The ceiling upstairs is out of reach. It isn't out of sight.",
                 "Gravity works both ways through that hole. The button is downstairs."],
                bot =>
                {
                    int n = 0;
                    bool Tele() => bot.Player.Teleports > n;
                    IEnumerable<InputState> Mark() => bot.Do(() => n = bot.Player.Teleports);
                    return Seq(bot.WalkTo(8 * 32 + 14), bot.Fire(Orange, Ceil(16, 0)), bot.Fire(Blue, Floor(3, 20)),
                        Mark(), Enter(bot, 4 * 32, Tele),
                        bot.WalkTo(X(24) - 40), bot.Use(), bot.WalkTo(12 * 32 + 20, aimRel: Back), bot.Use(), bot.Wait(0.8f),
                        bot.WalkTo(X(10)), bot.Until(() => bot.Player.OnGround && bot.Player.Pos.Y > 15 * 32, 3),
                        bot.WalkTo(X(11)), bot.Use(), bot.WalkTo(X(26), aimRel: Fwd), bot.Drop(new Vector2(29 * 32, 700)), bot.Wait(0.6f),
                        bot.WalkTo(7 * 32, stopIf: () => false), Mark(), Enter(bot, 4 * 32, Tele),
                        bot.WalkTo(X(37), timeout: 20));
                }));
        }

        // Bank Shot: bounce the beam off a mirror cube. Getting the cube up there is the hard part
        {
            var b = LevelBuilder.Room();
            b.Set(5, 19, 'N').Set(6, 20, 'Q');
            b.Fill(13, 14, 19, 15, 'X').Set(17, 0, 'v');
            b.Fill(33, 1, 33, 15, 'X').Fill(33, 16, 33, 19, 'a').Set(33, 13, '4');
            b.Set(4, 19, 'P').Set(37, 19, 'E');
            list.Add(Def("Bank Shot", b,
                ["The cube with the mirror in it bends beams. This one is yours to place.",
                 "Riding a launch plate with a cube in your hands is allowed."],
                bot => Seq(bot.Use(), bot.Move(1, () => !bot.Player.OnGround, aimRel: Fwd),
                    bot.Move(1, () => bot.Player.OnGround, 4, aimRel: Fwd),
                    bot.WalkTo(X(17) - 36, aimRel: Fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(120, -2))), bot.Wait(0.6f),
                    bot.WalkTo(X(13) - 64, timeout: 4), bot.Until(() => bot.Player.OnGround, 3), bot.WalkTo(X(37), timeout: 12))));
        }

        // Ascent: a tall climb with a different trick at every landing
        {
            var b = LevelBuilder.Room(40, 34);
            b.Fill(14, 28, 38, 31, 'X');
            b.Fill(33, 20, 38, 27, 'X').Fill(33, 20, 34, 20, 'J');
            b.Fill(24, 14, 31, 15, 'X');
            b.Set(28, 28, 'Q');
            b.Set(2, 31, 'P').Set(6, 31, 'C').Set(27, 13, 'E');
            list.Add(Def("Ascent", b,
                ["Up, and up, and up. Nothing here is the same twice.",
                 "A cube is a step. A plate is a throw. Gel is a spring."],
                bot => Seq(bot.WalkTo(6 * 32 - 30), bot.Use(), bot.WalkTo(14 * 32 - 74, aimRel: Fwd), bot.Use(), bot.Wait(0.6f),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.Y < 1000),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 15 * 32),
                    bot.Move(1, () => !bot.Player.OnGround, 12), bot.Move(1, () => bot.Player.OnGround, 4),
                    bot.Move(-1, () => !bot.Player.OnGround, 6), bot.Move(-1, () => bot.Player.OnGround, 4),
                    bot.WalkTo(X(27)))));
        }

        // Disarmed: no gun. The portals are already there; a plate gets you to the first one
        {
            var b = LevelBuilder.Room();
            b.Fill(18, 1, 19, 19, 'X');
            b.Set(17, 9, 'u').Set(20, 17, 'o');
            b.Set(12, 20, 'Q');
            b.Set(24, 19, '1');
            b.Fill(34, 1, 34, 15, 'X').Fill(34, 16, 34, 19, 'a');
            b.Set(2, 19, 'P').Set(8, 19, 'C').Set(37, 19, 'E');
            var def = Def("Disarmed", b,
                ["No gun this time. The portals are already there.",
                 "That plate throws you at the wall. Take the cube along."],
                bot => Seq(bot.WalkTo(210), bot.Use(), bot.Move(1, () => !bot.Player.OnGround, 8, Fwd),
                    bot.Until(() => bot.Player.OnGround, 4, mx: 1, aimRel: Fwd), bot.WalkTo(X(22), aimRel: Fwd),
                    Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(100, 30))), bot.Wait(0.6f), bot.WalkTo(X(37), hopCubes: true, timeout: 15)));
            def.Gun = GunMode.None; def.BlueDir = Right; def.OrangeDir = Left;
            list.Add(def);
        }

        // Jailbreak: you start sealed in a cell. The only way out is a portal you can't see the other end of
        {
            var b = LevelBuilder.Room();
            b.Fill(28, 15, 33, 19, 'X').Fill(29, 16, 32, 19, ' ').Fill(30, 20, 31, 20, '#');
            b.Set(15, 1, 'o');
            b.Fill(16, 0, 18, 0, '#');
            b.Fill(12, 1, 12, 15, 'X').Fill(12, 16, 12, 19, 'a');
            b.Set(24, 19, '1');
            b.Set(29, 19, 'P').Set(32, 19, 'C').Set(3, 19, 'E');
            var def = Def("Jailbreak", b,
                ["You are locked in. The orange portal isn't.",
                 "Whatever is in the cell with you can come out too."],
                bot =>
                {
                    int n = 0;
                    bool Tele() => bot.Player.Teleports > n;
                    return Seq(bot.WalkTo(X(31)), bot.Use(), bot.Do(() => n = bot.Player.Teleports),
                        bot.Fire(Blue, Floor(30, 20)), bot.Until(Tele),
                        bot.Until(() => bot.Player.OnGround, 4), bot.WalkTo(X(26), aimRel: Back),
                        Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(-100, 30))), bot.Wait(0.6f),
                        bot.WalkTo(X(3), hopCubes: true, timeout: 20));
                });
            def.Gun = GunMode.BlueOnly; def.OrangeDir = Up;
            list.Add(def);
        }

        // Fizzler Row: the grid eats cubes, so the cube goes over it by portal - and over the goo behind it
        {
            var b = LevelBuilder.Room(44, 22);
            b.Fill(1, 20, 2, 20, '#').Fill(7, 20, 11, 21, '~').Fill(13, 0, 15, 0, '#');
            b.Fill(15, 20, 16, 20, '#');
            b.Fill(18, 1, 18, 19, '|');
            b.Fill(20, 20, 30, 21, '~').Fill(32, 0, 35, 0, '#');
            b.Fill(38, 1, 38, 15, 'X').Fill(38, 16, 38, 19, 'a');
            b.Set(35, 19, '1').Set(4, 19, 'P').Set(5, 19, 'C').Set(42, 19, 'E');
            list.Add(Def("Fizzler Row", b,
                ["The grid eats cubes. The goo eats everything else.",
                 "You can shoot through a grid. Just don't touch it."],
                bot =>
                {
                    int n = 0;
                    bool Tele() => bot.Player.Teleports > n;
                    IEnumerable<InputState> Mark() => bot.Do(() => n = bot.Player.Teleports);
                    return Seq(bot.Use(), bot.Fire(Blue, Floor(1, 20)), bot.Fire(Orange, Ceil(13, 0)), Mark(),
                        Enter(bot, 64, Tele, 14 * 32 + 16),
                        bot.WalkTo(14 * 32, aimRel: Fwd),
                        bot.Fire(Blue, Floor(15, 20)), bot.Fire(Orange, Ceil(32, 0)), Mark(),
                        Enter(bot, 16 * 32, Tele, 34 * 32),
                        bot.WalkTo(X(37), aimRel: Fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(-100, 30))), bot.Wait(0.6f),
                        bot.WalkTo(X(42), hopCubes: true, timeout: 20));
                }));
        }

        // Hold the Door: the door only stays open a few seconds, so the cube has to come with you
        {
            var b = LevelBuilder.Room(44, 22);
            b.Set(7, 19, 'T');
            b.Fill(9, 20, 12, 21, '~');
            b.Fill(22, 1, 22, 15, 'X').Fill(22, 16, 22, 19, 'a');
            b.Set(28, 19, '2');
            b.Fill(34, 1, 34, 15, 'X').Fill(34, 16, 34, 19, 'b');
            b.Set(2, 19, 'P').Set(4, 19, 'C').Set(40, 19, 'E');
            list.Add(Def("Hold the Door", b,
                ["The button only holds the door for a few seconds.",
                 "The next door wants something heavy. Don't leave it behind."],
                bot => Seq(bot.WalkTo(100), bot.Use(), bot.WalkTo(7 * 32 + 16, aimRel: Fwd), bot.WalkTo(8 * 32 + 20, aimRel: Up2),
                    bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > 13 * 32, 3),
                    bot.WalkTo(X(25), aimRel: Fwd),
                    bot.WalkTo(X(26), aimRel: Fwd), Lazy(() => bot.Drop(bot.Player.Pos + new Vector2(120, 20))), bot.Wait(0.6f),
                    bot.WalkTo(X(40), hopCubes: true, timeout: 20))));
        }

        // Spotlight: the beam is on this side of the wall. The receiver is on the other
        {
            var b = LevelBuilder.Room();
            b.Fill(18, 1, 20, 19, 'X').Fill(18, 3, 20, 8, ' ').Fill(18, 11, 18, 12, '#');
            b.Fill(18, 16, 20, 19, 'a');
            b.Set(0, 12, '>');
            b.Fill(21, 0, 28, 0, '#');
            b.Set(23, 6, '4');
            b.Set(2, 19, 'P').Set(22, 19, 'E');
            list.Add(Def("Spotlight", b,
                ["The beam can't get through the wall. A portal can.",
                 "Put the other end where it will do some good - and where you can see it."],
                bot => Seq(bot.WalkTo(X(7)), bot.Fire(Orange, Ceil(23, 0)), bot.Fire(Blue, WallL(18, 11)), bot.Wait(1.2f),
                    bot.WalkTo(X(22), timeout: 20))));
        }

        // Terminal Velocity: the shelf is too low for a fling that long. Loop the fall until it isn't
        {
            var b = LevelBuilder.Room(56, 34);
            b.Fill(1, 24, 8, 31, 'X').Fill(0, 14, 0, 15, '#');
            b.Fill(11, 32, 12, 32, '#').Fill(11, 0, 12, 0, '#');
            b.Fill(14, 32, 32, 33, '~');
            b.Set(4, 23, 'P').Set(53, 31, 'E');
            list.Add(Def("Terminal Velocity", b,
                ["The goo is too wide for that fall. Falling faster is a matter of falling further.",
                 "Portals don't care which way is up. Make the shaft longer."],
                bot =>
                {
                    int n = 0;
                    bool Tele() => bot.Player.Teleports > n;
                    var p = bot.Player;
                    return Seq(bot.WalkTo(9 * 32 + 4), bot.Fire(Blue, Floor(11, 32)), bot.Fire(Orange, Ceil(11, 0)),
                        bot.Do(() => n = p.Teleports), bot.Move(1, () => !p.OnGround, 3), bot.Steer(12 * 32, Tele, 6),
                        bot.Steer(12 * 32, () => p.Pos.Y > 12 * 32, 4),
                        bot.Fire(Orange, WallR(0, 14)), bot.Until(() => p.Teleports > n + 1 && p.OnGround, 8),
                        bot.WalkTo(X(53), timeout: 20));
                }, fling: true));
        }

        // Safe Cracker: three timed buttons, one door, and not much time. Find the quick way round
        {
            var b = LevelBuilder.Room();
            b.Set(3, 19, 'T').Set(7, 20, 'Q');
            b.Fill(12, 12, 19, 13, 'X').Set(17, 11, 'T');
            b.Set(22, 19, 'T');
            b.Fill(25, 1, 25, 15, 'X').Fill(25, 16, 25, 19, 'a');
            b.Set(2, 19, 'P').Set(31, 19, 'E');
            list.Add(Def("Safe Cracker", b,
                ["All three buttons have to be down at once. Each one only lasts a few seconds.",
                 "Plates are faster than stairs."],
                bot => Seq(bot.WalkTo(X(3)), bot.Move(1, () => !bot.Player.OnGround, 8), bot.Move(1, () => bot.Player.OnGround, 4),
                    bot.WalkTo(X(17), timeout: 6), bot.WalkTo(X(19) + 6, timeout: 6), bot.Move(1, () => bot.Player.OnGround && bot.Player.Pos.Y > 600, 4),
                    bot.WalkTo(X(22), timeout: 6), bot.WalkTo(X(31), timeout: 12))));
        }

        // Rooftops: nothing down there but goo. Four jumps, four different ways of making them
        {
            var b = LevelBuilder.Room(68, 22);
            b.Fill(1, 20, 66, 21, '~');
            b.Fill(1, 16, 7, 21, 'X').Fill(1, 16, 7, 16, 'G');
            b.Fill(17, 16, 24, 21, 'X').Set(24, 16, 'Q');
            b.Fill(30, 11, 37, 21, 'X').Fill(35, 11, 37, 11, 'J');
            b.Fill(41, 11, 48, 21, 'X').Fill(45, 11, 46, 11, '#');
            b.Fill(58, 14, 65, 21, 'X').Fill(59, 0, 62, 0, '#');
            b.Set(1, 15, 'P').Set(64, 13, 'E');
            list.Add(Def("Rooftops", b,
                ["Four roofs, four ways across. Gel, plate, gel, portal.",
                 "Whatever you do, don't touch the street."],
                bot =>
                {
                    int n = 0;
                    bool Tele() => bot.Player.Teleports > n;
                    var p = bot.Player;
                    return Seq(bot.Move(1, () => p.Pos.X >= 8 * 32 + 6, 4), bot.JumpTo(1, () => p.OnGround && p.Pos.X > 17 * 32 + 8),
                        bot.Move(1, () => !p.OnGround, 6), bot.Move(1, () => p.OnGround, 4),
                        bot.Move(1, () => !p.OnGround, 6), bot.Move(1, () => p.OnGround, 4),
                        bot.Wait(0.3f),
                        bot.Fire(Blue, Floor(45, 11)), bot.Fire(Orange, Ceil(59, 0)), bot.Do(() => n = p.Teleports),
                        Enter(bot, 46 * 32, Tele), bot.WalkTo(X(64)));
                }));
        }

        // easiest first: each one is its own idea, so the order is about how much it asks of you
        string[] order =
        [
            "Stool Pigeon", "Pinball", "Launch Pad", "Trampoline Park", "Peephole", "Catapult", "Moat", "Hold the Door",
            "Rush Hour", "Safe Cracker", "Disarmed", "Jailbreak", "Bank Shot", "Spotlight", "Double Decker",
            "Fizzler Row", "Ascent", "Terminal Velocity", "Rooftops"
        ];
        if (order.Length != list.Count || order.Any(n => list.All(d => d.Name != n)))
            throw new InvalidOperationException("Chambers.Add: order list does not match the chambers");
        for (int i = 0; i < order.Length; i++)
        {
            var def = list.Single(d => d.Name == order[i]);
            def.Theme = 1 + (i + 3) % 6;
            output.Add(def);
        }
    }
}
