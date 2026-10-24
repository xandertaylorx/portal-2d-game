using System.Numerics;
using Portal2D.Core;
using Portal2D.Game;
using Portal2D.Testing;
using static Portal2D.Testing.Bot;

// Web-only authoring. Reuses the frozen reference physics, never changes the desktop campaign.
static class Puzzles
{
    const PortalColor Blue = PortalColor.Blue, Orange = PortalColor.Orange;
    public static readonly string[] Families = ["atrium", "switchback", "reservoir", "freight", "relay", "overpass",
        "clock", "ricochet", "islands", "periscope", "retrieval", "gantry"];
    static readonly string[][] Names = [
        ["Light Well", "Observation Deck", "Cloud Chamber", "Roof Access"],
        ["About Turn", "Spiral Stair", "Zigzag", "The Long Way Up"],
        ["Dry Dock", "Flood Control", "Causeway", "Deep Water"],
        ["Freight Lift", "Air Mail", "Upper Management", "Heavy Lifting"],
        ["Beam Split", "Optical Bench", "Signal Box", "Light Industry"],
        ["Flyover", "Underpass", "Sky Bridge", "Crossing Paths"],
        ["Closing Time", "Quick Change", "Against the Clock", "Last Orders"],
        ["Rebound", "Elastic Limit", "Blue Shift", "Escape Velocity"],
        ["Archipelago", "Lake Effect", "Rock Pool", "Island Hopping"],
        ["Line of Sight", "Blind Corner", "Keyhole", "Through the Looking Glass"],
        ["Lost Property", "Return to Sender", "Cold Storage", "Round Trip"],
        ["Loading Bay", "Counterweight", "Cargo Net", "Lift and Shift"]
    ];

    static IEnumerable<InputState> Enter(Bot bot, float x, int count = 1, float? land = null)
    {
        int n = count - 1;
        foreach (var i in bot.WalkTo(x, stopIf: () => bot.Player.Teleports > n)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.Teleports > n)) yield return i;
        if (land is float lx) foreach (var i in bot.Steer(lx, () => bot.Player.OnGround, 5)) yield return i;
        foreach (var i in bot.Until(() => bot.Player.OnGround, 5)) yield return i;
    }

    public static LevelDef Build(int family, int variant, string? name = null)
    {
        int v = variant % 4, stretch = variant / 4, w = 38 + v * 4 + stretch * 2, h = 26 + v * 2, f = h - 3;
        if (family is 1 or 3) { h = 26 + v * 2; f = h - 3; }
        var b = LevelBuilder.Room(w, h);
        Func<Bot, IEnumerable<InputState>> solve;
        string[] hints;
        bool fling = false;
        // Each recipe has a different route and combines mechanics in one connected space.
        switch (family)
        {
            case 0: // tall atrium: enter the ceiling above a solid observation platform
            {
                int ledge = 10 + v, wall = w / 2;
                b.Fill(wall, ledge, w - 2, f, 'X');
                b.Fill(3, f + 1, 6, f + 1, '#').Fill(wall + 2, 0, wall + 6, 0, '#');
                b.Fill(wall - 3, ledge + 6, wall - 1, ledge + 6, '=');
                b.Set(2, f, 'P').Set(w - 4, ledge - 1, 'E');
                // A fizzler on the upper route makes the portal ride one-way.
                b.Fill(w - 8, 1, w - 8, ledge - 1, '|');
                solve = bot => Seq(bot.Fire(Blue, Floor(3, f + 1)), bot.Fire(Orange, Ceil(wall + 3, 0)),
                    Enter(bot, 4 * 32), bot.WalkTo(X(w - 4)));
                hints = ["The observation deck has no stairs. Look above it.", "Cross the grid after you land."];
                break;
            }
            case 1: // open central shaft, two floors, finish back above the starting point
            {
                int shelf = 12 + v, hole = 10 + v, ceil = 24 + v;
                b.Fill(1, shelf, w - 2, shelf + 1, 'X').Fill(hole, shelf, hole + 8, shelf + 1, ' ');
                b.Fill(3, f + 1, 6, f + 1, '#').Fill(ceil, 0, ceil + 3, 0, '#');
                b.Fill(2, 0, 6, 0, '#').Fill(ceil, shelf, ceil + 3, shelf, '#');
                b.Fill(hole - 2, 1, hole - 2, shelf - 1, '|');
                b.Set(2, f, 'P').Set(3, shelf - 1, 'E');
                solve = bot => Seq(bot.WalkTo(X(hole + 2)), bot.Fire(Blue, Floor(3, f + 1)),
                    bot.Fire(Orange, Ceil(ceil, 0)), Enter(bot, 4 * 32),
                    bot.Fire(Orange, Ceil(3, 0)), bot.Fire(Blue, Floor(ceil, shelf)),
                    Enter(bot, (ceil + 1) * 32, 2), bot.WalkTo(X(3)));
                hints = ["Climb through the central shaft, then cross back over it.", "The exit is above where you started."];
                break;
            }
            case 2: // flooded basin, two ceiling transfers and a raised island
            {
                int a = 13 + v, z = w - 12, shelf = f - 6;
                b.Fill(8, f + 1, w - 9, h - 1, '~');
                b.Fill(a, shelf, a + 5, h - 1, 'X');
                b.Fill(a, shelf, a + 4, shelf, '#');
                b.Fill(3, f + 1, 5, f + 1, '#').Fill(a, 0, a + 4, 0, '#');
                b.Fill(w - 8, 0, w - 3, 0, '#');
                b.Fill(z, 1, z, 3, '=');
                b.Set(2, f, 'P').Set(w - 4, f, 'E');
                solve = bot => Seq(bot.Fire(Blue, Floor(3, f + 1)), bot.Fire(Orange, Ceil(a + 1, 0)),
                    Enter(bot, 4 * 32, land: X(a + 4)), bot.Fire(Orange, Ceil(w - 6, 0)),
                    bot.Fire(Blue, Floor(a + 1, shelf)), Enter(bot, (a + 2) * 32, 2), bot.WalkTo(X(w - 4)));
                hints = ["Use the high island as a staging point.", "Aim below the hanging glass."];
                break;
            }
            case 3: // cube upstairs, button downstairs, exit upstairs: a real return journey
            {
                int shelf = 12 + v, hole = 9 + v;
                b.Fill(1, shelf, w - 2, shelf + 1, 'X').Fill(hole, shelf, hole + 11, shelf + 1, ' ');
                b.Fill(3, f + 1, 5, f + 1, '#').Fill(23 + v, 0, 27 + v, 0, '#');
                b.Fill(w - 7, 1, w - 7, shelf - 1, 'a');
                b.Set(w - 12, shelf - 1, 'C').Set(w - 12, f, '1');
                b.Set(2, f, 'P').Set(w - 4, shelf - 1, 'E');
                solve = bot => Seq(bot.WalkTo(hole * 32 + 14), bot.Fire(Orange, Ceil(24 + v, 0)),
                    bot.Fire(Blue, Floor(3, f + 1)), Enter(bot, 4 * 32),
                    bot.WalkTo(X(w - 12) - 40), bot.Use(),
                    bot.WalkTo((hole + 12) * 32 + 20, aimRel: new(-120, 0)), bot.Use(), bot.Wait(1),
                    bot.WalkTo(X(hole + 9)), bot.Until(() => bot.Player.OnGround && bot.Player.Pos.Y > (shelf + 2) * 32),
                    bot.WalkTo(X(hole + 10)), bot.Use(), bot.WalkTo(X(w - 14), aimRel: new(120, 0)),
                    bot.Drop(new(X(w - 11), (f + 1) * 32)), bot.Wait(0.7f),
                    bot.WalkTo(hole * 32 + 14), bot.Fire(Orange, Ceil(24 + v, 0)),
                    bot.Fire(Blue, Floor(3, f + 1)), Enter(bot, 4 * 32, 2), bot.WalkTo(X(w - 4)));
                hints = ["The cube belongs downstairs. The exit is upstairs.", "Drop the cube through the opening, then follow it."];
                break;
            }
            case 4: // laser wall with a window, beam at a different height on each side
            {
                int x = 17 + v, beam = f - 8;
                b.Set(0, beam, '>');
                b.Fill(x, 1, x + 2, f - 4, 'X').Fill(x, f - 3, x + 2, f, 'a');
                b.Fill(x, 3, x + 2, beam - 3, ' ').Fill(x, beam - 1, x, beam, '#');
                b.Fill(x + 4, 0, w - 3, 0, '#');
                b.Set(x + 7, 6, '4');
                b.Fill(w - 7, 1, w - 7, f - 5, '=');
                b.Set(3, f, 'P').Set(w - 4, f, 'E');
                solve = bot => Seq(bot.WalkTo(X(8)), bot.Fire(Orange, Ceil(x + 7, 0)),
                    bot.Fire(Blue, WallL(x, beam - 1)), bot.Wait(0.8f), bot.WalkTo(X(w - 4)));
                hints = ["The receiver is higher than the beam. Bend the light through the window."];
                break;
            }
            case 5: // under a bridge, then ride a portal back onto it; exit lies behind spawn
            {
                int shelf = f - 9, end = w - 9;
                b.Fill(1, shelf, end, shelf + 1, 'X');
                b.Fill(w - 7, f + 1, w - 3, f + 1, '#').Fill(w - 11, 0, w - 7, 0, '#');
                b.Fill(2, shelf + 2, 2, f, 'X');
                b.Fill(w / 2, shelf + 2, w / 2, f - 4, '|');
                b.Set(5, f, 'P').Set(3, shelf - 1, 'E');
                solve = bot => Seq(bot.WalkTo(X(w - 5)), bot.Fire(Blue, Floor(w - 6, f + 1)),
                    bot.Fire(Orange, Ceil(w - 10, 0)),
                    bot.WalkTo((w - 5) * 32, stopIf: () => bot.Player.Teleports > 0),
                    bot.Until(() => bot.Player.Teleports > 0),
                    bot.Steer(X(w - 12), () => bot.Player.OnGround), bot.WalkTo(X(3)));
                hints = ["Walk underneath. Return over the top.", "The end of the bridge gives you a clear shot at the ceiling."];
                break;
            }
            case 6: // timed gate, staircase and gel sprint
            {
                b.Set(4, f, 'T');
                b.Fill(6, f + 1, 13, f + 1, 'G');
                b.Fill(15, 1, 15, f - 4, 'X').Fill(15, f - 3, 15, f, 'a');
                for (int s = 0; s < 4; s++) b.Fill(19 + s * 4, f - s, 22 + s * 4, f + 1, 'X');
                b.Fill(31, f - 3, w - 2, f + 1, 'X');
                b.Fill(w - 5, 1, w - 5, f - 7, 'X').Fill(w - 5, f - 6, w - 5, f - 4, 'b');
                int checkpoint = Math.Max(31, w - 14);
                b.Set(checkpoint, f - 4, 'U');
                b.Set(2, f, 'P').Set(w - 3, f - 4, 'E');
                solve = bot => Seq(bot.WalkTo(X(4)), bot.WalkTo(X(checkpoint)), bot.Wait(0.3f), bot.WalkTo(X(w - 3), timeout: 20));
                hints = ["Step on the timer, then commit to the climb.", "The orange strip buys you time."];
                break;
            }
            case 7: // tall repulsion shaft to raised gallery
            {
                int shelf = f - 10;
                b.Fill(17, shelf, w - 2, f, 'X');
                b.Fill(9, f + 1, 12, f + 1, 'J');
                b.Fill(8, 0, 13, 0, '#').Fill(3, f + 1, 5, f + 1, '#');
                b.Fill(w - 9, 1, w - 9, shelf - 1, '|');
                b.Set(2, f, 'P').Set(w - 4, shelf - 1, 'E');
                solve = bot => Seq(bot.Fire(Blue, Floor(3, f + 1)), bot.Fire(Orange, Ceil(9, 0)),
                    bot.WalkTo(4 * 32, stopIf: () => bot.Player.Teleports > 0),
                    bot.Until(() => bot.Player.Teleports > 0), bot.Steer(11 * 32, () => bot.Player.Vel.Y < -300),
                    bot.Until(() => bot.Player.Pos.Y < (shelf + 3) * 32, 4),
                    bot.Until(() => bot.Player.OnGround, 5, mx: 1), bot.WalkTo(X(w - 4)));
                hints = ["A longer fall makes a stronger bounce.", "Stay over the blue gel until it throws you upward."];
                fling = true;
                break;
            }
            case 8: // alternating safe islands, jump gaps, fizzlers remove portal shortcuts
            {
                w = 48 + v * 8 + stretch * 2; b = LevelBuilder.Room(w, h);
                for (int x = 8; x < w - 10; x += 10) {
                    b.Fill(x, f + 1, x + 3, h - 1, '~');
                    b.Fill(x + 5, f - 2 - (x / 10 % 2), x + 7, f - 2 - (x / 10 % 2), '=');
                }
                b.Set(2, f, 'P').Set(w - 4, f, 'E');
                int width = w;
                solve = bot => Islands(bot, width, f);
                hints = ["Jump from shore to shore. Let yourself land before the next gap."];
                break;
            }
            case 9: // two offset windows require repositioning before the second portal shot
            {
                int x = 15 + v, ledge = f - 6;
                b.Fill(x, 1, x + 2, f, 'X').Fill(x, 4 + v, x + 2, f - 7, ' ');
                b.Fill(x + 5, ledge, w - 2, f, 'X');
                b.Fill(3, f + 1, 7, f + 1, '#').Fill(x + 6, 0, x + 11, 0, '#');
                b.Fill(x - 5, 3, x - 3, 7, '=');
                b.Fill(w - 8, 1, w - 8, ledge - 1, '|');
                b.Set(2, f, 'P').Set(w - 4, ledge - 1, 'E');
                solve = bot => Seq(bot.WalkTo(X(8)), bot.Fire(Blue, Floor(4, f + 1)),
                    bot.Fire(Orange, Ceil(x + 7, 0)), Enter(bot, 5 * 32), bot.WalkTo(X(w - 4)));
                hints = ["Find an angle through the window to the ceiling beyond.", "Move until the window gives you a clear shot."];
                break;
            }
            case 10: // fetch cube over a pit, bring it home; exit is on the starting side
            {
                int island = w - 12;
                b.Fill(12, f + 1, w - 3, h - 1, '~').Fill(island, f + 1, island + 5, h - 1, 'X');
                b.Fill(island, f + 1, island + 3, f + 1, '#').Fill(island, 0, island + 4, 0, '#');
                b.Fill(7, f + 1, 10, f + 1, '#').Fill(6, 0, 10, 0, '#');
                b.Fill(4, 1, 4, f - 4, 'X').Fill(4, f - 3, 4, f, 'a');
                b.Set(7, f, 'P').Set(island + 2, f, 'C').Set(6, f, '1').Set(2, f, 'E');
                solve = bot => Seq(bot.Fire(Orange, Ceil(island + 1, 0)), bot.Fire(Blue, Floor(8, f + 1)),
                    Enter(bot, 9 * 32, land: X(island + 4)), bot.WalkTo(X(island + 2) - 40), bot.Use(),
                    bot.Fire(Orange, Ceil(7, 0)), bot.Fire(Blue, Floor(island, f + 1)),
                    bot.AimAt(new(X(island + 4), (f + 1) * 32)), Enter(bot, (island + 1) * 32, 2),
                    bot.WalkTo(X(9)), bot.Drop(new(X(5), (f + 1) * 32)), bot.Wait(0.8f), bot.WalkTo(X(2)));
                hints = ["The exit is behind you. Its key is across the water.", "Carry the cube home through the portals."];
                break;
            }
            default: // cube on elevated catapult gantry, plate carries freight onto button
            {
                int plate = 10 + v, shelf = f - 7;
                b.Set(plate, f + 1, 'Q');
                b.Fill(plate + 7, shelf, plate + 16, shelf + 1, 'X').Set(plate + 9, shelf - 1, '1');
                b.Fill(w - 5, 1, w - 5, f - 4, 'X').Fill(w - 5, f - 3, w - 5, f, 'a');
                b.Set(2, f, 'P').Set(5, f, 'C').Set(w - 3, f, 'E');
                solve = bot => Seq(bot.WalkTo(X(5) - 26), bot.Use(), bot.WalkTo(plate * 32 - 22, aimRel: new(120, 0)),
                    bot.Use(), bot.Wait(1.5f), bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > (plate + 2) * 32),
                    bot.WalkTo(X(w - 3), timeout: 20));
                hints = ["The button is on the gantry. Launch the cargo, then take the low route."];
                break;
            }
        }
        return new LevelDef { Name = name ?? Names[family][v], Map = b.Build(), Hints = hints,
            Theme = 1 + (family + v) % 6, Fling = fling, Solution = solve };
    }

    static IEnumerable<InputState> Islands(Bot bot, int w, int f)
    {
        for (int x = 8; x < w - 10; x += 10) {
            foreach (var i in bot.WalkTo(x * 32 - 20)) yield return i;
            foreach (var i in bot.JumpTo(1, () => bot.Player.OnGround && bot.Player.Pos.X > (x + 4) * 32 + 4)) yield return i;
        }
        foreach (var i in bot.WalkTo(X(w - 4))) yield return i;
    }
}
