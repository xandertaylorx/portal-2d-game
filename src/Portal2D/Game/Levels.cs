using Portal2D.Core;

namespace Portal2D.Game;

public enum GunMode { None, BlueOnly, Both }

/// <summary>
/// Tile legend
///   ' ' empty           '#' white panel (portal-able)   'X' metal (no portals)   '=' glass
///   '~' toxic goo       '|' emancipation grid          'P' player start         'E' exit
///   'C' cube spawn      '1','2','3' floor buttons      '4','5','6' laser receivers
///   'a','b','c' doors (opened by group 1/2/3)          '>','<','^','v' laser emitters
///   'J' blue repulsion gel (bounce pad)
///   'u' / 'o' fixed blue / orange portal marker (portal sits on the wall in BlueDir / OrangeDir)
/// </summary>
public sealed class LevelDef
{
    public required string Name;
    public required string[] Map;
    public GunMode Gun = GunMode.Both;
    public Int2 BlueDir;
    public Int2 OrangeDir;
    public string[] Hints = [];
    public bool Fling;
    /// <summary>Painted on the back wall (chamber number).</summary>
    public string Label = "";
}

public sealed class LevelBuilder
{
    readonly char[,] g;
    public readonly int W, H;

    public LevelBuilder(int w, int h, char fill = 'X')
    {
        W = w; H = h;
        g = new char[w, h];
        Fill(0, 0, w - 1, h - 1, fill);
    }

    /// <summary>A metal box with an open interior and a two-tile thick floor.</summary>
    public static LevelBuilder Room(int w = 40, int h = 22)
    {
        var b = new LevelBuilder(w, h);
        b.Fill(1, 1, w - 2, h - 3, ' ');
        return b;
    }

    public LevelBuilder Fill(int x0, int y0, int x1, int y1, char c)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                g[x, y] = c;
        return this;
    }

    public LevelBuilder Set(int x, int y, char c) { g[x, y] = c; return this; }

    public string[] Build()
    {
        var rows = new string[H];
        for (int y = 0; y < H; y++)
        {
            var chars = new char[W];
            for (int x = 0; x < W; x++) chars[x] = g[x, y];
            rows[y] = new string(chars);
        }
        return rows;
    }
}

public static class Levels
{
    static readonly Int2 Left = new(-1, 0), Right = new(1, 0), Up = new(0, -1), Down = new(0, 1);

    public static readonly LevelDef Menu = BuildMenu();
    public static readonly LevelDef[] All = BuildAll();

    static LevelDef BuildMenu()
    {
        var b = LevelBuilder.Room();
        b.Fill(17, 20, 22, 20, '#').Fill(17, 0, 22, 0, '#');
        b.Set(19, 19, 'u').Set(19, 1, 'o');
        b.Set(20, 9, 'C');
        b.Fill(1, 15, 6, 19, 'X').Fill(2, 14, 5, 14, ' ');
        b.Fill(30, 18, 38, 19, '~').Fill(29, 17, 29, 19, 'X');
        b.Fill(9, 1, 9, 19, '|');
        b.Set(39, 6, '<').Fill(25, 3, 26, 9, '=');
        b.Set(13, 19, '1');
        return new LevelDef { Name = "Menu", Map = b.Build(), Gun = GunMode.None, BlueDir = Down, OrangeDir = Up };
    }

    static LevelDef[] BuildAll()
    {
        var list = new List<LevelDef>();

        // 01 - movement + a pre-placed portal pair (look through it!)
        {
            var b = LevelBuilder.Room();
            b.Fill(19, 1, 20, 19, 'X');
            b.Set(19, 18, '#').Set(19, 19, '#').Set(18, 18, 'u');
            b.Set(20, 7, '#').Set(20, 8, '#').Set(21, 7, 'o');
            b.Fill(8, 19, 9, 19, 'X').Fill(12, 18, 13, 19, 'X');
            b.Fill(27, 15, 31, 15, 'X');
            b.Set(2, 19, 'P').Set(36, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Awakening", Map = b.Build(), Gun = GunMode.None, BlueDir = Right, OrangeDir = Left,
                Hints = ["A / D to move    W / SPACE to jump",
                         "Walk into the blue portal. Look closely - you can see THROUGH it."]
            });
        }

        // 02 - blue gun only, orange is fixed high up
        {
            var b = LevelBuilder.Room();
            b.Fill(28, 10, 38, 19, 'X');
            b.Set(39, 7, '#').Set(39, 8, '#').Set(38, 7, 'o');
            b.Fill(0, 13, 0, 19, '#');
            b.Set(6, 19, 'P').Set(31, 9, 'E');
            list.Add(new LevelDef
            {
                Name = "Blue Sky", Map = b.Build(), Gun = GunMode.BlueOnly, OrangeDir = Right,
                Hints = ["LEFT MOUSE fires the blue portal. Portals only stick to WHITE panels.",
                         "The orange portal is fixed. Make an entrance that leads to it."]
            });
        }

        // 03 - both portals
        {
            var b = LevelBuilder.Room();
            b.Fill(24, 8, 38, 19, 'X');
            b.Fill(26, 0, 37, 0, '#');
            b.Fill(0, 14, 0, 19, '#');
            b.Fill(12, 15, 13, 19, 'X');
            b.Set(4, 19, 'P').Set(36, 7, 'E');
            list.Add(new LevelDef
            {
                Name = "Two Ends", Map = b.Build(),
                Hints = ["RIGHT MOUSE fires orange. Enter one, exit the other.",
                         "Ceilings work too. Gravity does the rest."]
            });
        }

        // 04 - cube, button, door
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 9, 6, 9, 'X').Set(3, 8, 'C');
            b.Fill(1, 0, 6, 0, '#');
            b.Fill(10, 20, 16, 20, '#');
            b.Fill(30, 1, 30, 15, 'X').Fill(30, 16, 30, 19, 'a');
            b.Set(23, 19, '1');
            b.Set(20, 19, 'P').Set(36, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Weighted", Map = b.Build(),
                Hints = ["E picks up / drops a cube. Buttons need something heavy on them.",
                         "Stand on a floor portal to drop through it."]
            });
        }

        // 05 - goo + carrying a cube through portals
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 18, 9, 19, 'X').Fill(10, 18, 30, 19, '~').Fill(31, 18, 38, 19, 'X');
            b.Fill(0, 13, 0, 17, '#');
            b.Fill(31, 0, 35, 0, '#');
            b.Fill(36, 1, 36, 13, 'X').Fill(36, 14, 36, 17, 'a');
            b.Set(33, 17, '1').Set(6, 17, 'C');
            b.Set(3, 17, 'P').Set(38, 17, 'E');
            list.Add(new LevelDef
            {
                Name = "Toxic", Map = b.Build(),
                Hints = ["Goo is lethal. Don't touch it.",
                         "Cubes come with you when you carry them through a portal."]
            });
        }

        // 06 - fling across goo
        {
            var b = LevelBuilder.Room();
            b.Fill(3, 5, 7, 5, 'X');
            b.Fill(0, 1, 0, 4, '#').Fill(0, 7, 0, 14, '#');
            b.Fill(1, 20, 12, 20, '#');
            b.Fill(13, 20, 20, 21, '~');
            b.Set(5, 4, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Speedy Thing", Map = b.Build(), Fling = true,
                Hints = ["Speedy thing goes in, speedy thing comes out.",
                         "Fall a long way into a floor portal and you'll fly out of the other one."]
            });
        }

        // 07 - emancipation grid
        {
            var b = LevelBuilder.Room();
            b.Fill(20, 1, 20, 19, '|');
            b.Fill(3, 20, 12, 20, '#');
            b.Fill(24, 0, 32, 0, '#');
            b.Fill(34, 1, 34, 15, 'X').Fill(34, 16, 34, 19, 'a');
            b.Set(28, 19, '1').Set(8, 19, 'C');
            b.Set(4, 19, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Emancipation", Map = b.Build(),
                Hints = ["Emancipation grids vaporize cubes and erase your portals.",
                         "Your portal shots pass through the grid. Cubes do not."]
            });
        }

        // 08 - laser redirect, glass
        {
            var b = LevelBuilder.Room();
            b.Set(0, 10, '>');
            b.Fill(20, 2, 21, 15, 'X').Fill(20, 7, 20, 13, '#').Fill(21, 2, 21, 6, '#');
            b.Set(39, 4, '4');
            b.Fill(30, 1, 30, 15, '=').Fill(30, 16, 30, 19, 'a');
            b.Set(4, 19, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Through the Glass", Map = b.Build(),
                Hints = ["Lasers travel through portals - and through glass. You don't.",
                         "Aim the beam at the receiver. Where it enters a portal decides where it leaves."]
            });
        }

        // 09 - one portal pair, several jobs
        {
            var b = LevelBuilder.Room();
            b.Set(6, 0, 'v');
            b.Fill(4, 20, 9, 20, '#').Fill(14, 20, 20, 20, '#');
            b.Fill(0, 2, 0, 19, '#');
            b.Set(24, 19, '2');
            b.Fill(30, 1, 30, 15, '=').Fill(30, 16, 30, 19, 'a');
            b.Fill(34, 1, 34, 15, '=').Fill(34, 16, 34, 19, 'b');
            b.Set(39, 6, '4');
            b.Set(2, 19, 'P').Set(4, 19, 'C').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Relay", Map = b.Build(),
                Hints = ["One pair of portals. Several jobs. Do them in the right order.",
                         "First get past the beam. Then put the beam to work."]
            });
        }

        // 10 - fling upwards
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 5, 6, 5, 'X');
            b.Fill(0, 1, 0, 4, '#');
            b.Fill(1, 20, 10, 20, '#');
            b.Fill(11, 20, 20, 21, '~');
            b.Fill(21, 20, 31, 20, '#');
            b.Fill(33, 9, 38, 9, 'X');
            b.Set(2, 4, 'P').Set(37, 8, 'E');
            list.Add(new LevelDef
            {
                Name = "Ascension", Map = b.Build(), Fling = true,
                Hints = ["A floor portal can launch you UP. The further you fall, the higher you fly.",
                         "Steer in mid-air with A / D."]
            });
        }

        // 11 - there and back again with a cube
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 8, 9, 8, 'X');
            b.Fill(6, 1, 6, 3, 'X').Fill(6, 4, 6, 7, 'a');
            b.Fill(7, 0, 9, 0, '#');
            b.Fill(2, 20, 13, 20, '#');
            b.Fill(15, 20, 27, 21, '~');
            b.Fill(28, 12, 38, 19, 'X').Fill(29, 12, 33, 12, '#');
            b.Fill(29, 0, 36, 0, '#');
            b.Set(5, 19, '1').Set(35, 11, 'C');
            b.Set(12, 19, 'P').Set(2, 7, 'E');
            list.Add(new LevelDef
            {
                Name = "Up and Over", Map = b.Build(),
                Hints = ["The cube is across the goo. Go get it - and bring it back.",
                         "The exit is up high, behind the door."]
            });
        }

        // 12 - repulsion gel
        {
            var b = LevelBuilder.Room();
            b.Fill(27, 7, 38, 19, 'X');
            b.Fill(22, 20, 24, 20, 'J');
            b.Fill(21, 0, 25, 0, '#');
            b.Fill(0, 16, 0, 19, '#');
            b.Set(4, 19, 'P').Set(36, 6, 'E');
            list.Add(new LevelDef
            {
                Name = "Spring Loaded", Map = b.Build(),
                Hints = ["Blue gel bounces you. The harder you land, the higher you go.",
                         "Fall from far enough and the gel will throw you onto the ledge."]
            });
        }

        // 13 - two buttons, two cubes
        {
            var b = LevelBuilder.Room();
            b.Fill(30, 9, 36, 9, 'X').Set(33, 8, 'C');
            b.Fill(30, 0, 35, 0, '#');
            b.Fill(14, 20, 20, 20, '#');
            b.Fill(37, 1, 37, 15, 'X').Fill(37, 16, 37, 19, 'a');
            b.Set(12, 19, '1').Set(26, 19, '1');
            b.Set(6, 19, 'C').Set(3, 19, 'P').Set(38, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Two Weights", Map = b.Build(),
                Hints = ["The door needs BOTH buttons held down.",
                         "One cube is easy. The other one is up on the shelf."]
            });
        }

        // 14 - gel island over goo
        {
            var b = LevelBuilder.Room();
            b.Fill(9, 20, 12, 21, '~').Fill(13, 20, 15, 20, 'J').Fill(16, 20, 20, 21, '~');
            b.Fill(30, 8, 38, 19, 'X');
            b.Fill(31, 0, 36, 0, '#');
            b.Fill(22, 20, 28, 20, '#');
            b.Set(3, 19, 'P').Set(36, 7, 'E');
            list.Add(new LevelDef
            {
                Name = "Gel Pit", Map = b.Build(),
                Hints = ["You can't stand on gel - but you can use it to get across.",
                         "Keep moving while you bounce."]
            });
        }

        // 15 - cube as a laser shield
        {
            var b = LevelBuilder.Room();
            b.Fill(1, 9, 6, 9, 'X').Set(3, 8, 'C');
            b.Fill(1, 0, 6, 0, '#');
            b.Fill(8, 20, 12, 20, '#');
            b.Set(15, 0, 'v').Set(24, 0, 'v');
            b.Set(31, 19, '1');
            b.Fill(35, 1, 35, 15, 'X').Fill(35, 16, 35, 19, 'a');
            b.Set(11, 19, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Umbrella", Map = b.Build(),
                Hints = ["Lasers can't get through cubes.",
                         "Hold the cube above your head and walk under the beams."]
            });
        }

        // 16 - two beams through one portal pair
        {
            var b = LevelBuilder.Room();
            b.Set(0, 9, '>').Set(0, 10, '>');
            b.Fill(20, 2, 21, 15, 'X').Fill(20, 6, 20, 13, '#').Fill(21, 2, 21, 7, '#');
            b.Set(39, 3, '4').Set(39, 4, '4');
            b.Fill(30, 1, 30, 15, '=').Fill(30, 16, 30, 19, 'a');
            b.Set(4, 19, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Twin Beams", Map = b.Build(),
                Hints = ["Both receivers must be lit.",
                         "One portal pair can carry two beams at once - if you line it up."]
            });
        }

        // 17 - grid, cube transport and a shielded crossing
        {
            var b = LevelBuilder.Room();
            b.Fill(12, 1, 12, 19, '|');
            b.Fill(7, 20, 10, 20, '#');
            b.Fill(15, 0, 19, 0, '#');
            b.Set(24, 0, 'v');
            b.Set(30, 19, '1');
            b.Fill(34, 1, 34, 15, 'X').Fill(34, 16, 34, 19, 'a');
            b.Set(6, 19, 'C').Set(3, 19, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Grid Lock", Map = b.Build(),
                Hints = ["The grid eats cubes. The beam eats you.",
                         "Get the cube across both - in one piece."]
            });
        }

        // 18 - beam fired upward through a ceiling portal
        {
            var b = LevelBuilder.Room();
            b.Set(6, 20, '^');
            b.Fill(4, 0, 9, 0, '#');
            b.Fill(19, 8, 20, 15, 'X').Fill(20, 9, 20, 14, '#');
            b.Set(39, 12, '4');
            b.Fill(30, 1, 30, 15, '=').Fill(30, 16, 30, 19, 'a');
            b.Set(12, 19, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Skylight", Map = b.Build(),
                Hints = ["This beam points straight up.",
                         "Catch it in the ceiling and send it sideways."]
            });
        }

        // 19 - fling over goo, straight through an emancipation grid
        {
            var b = LevelBuilder.Room();
            b.Fill(3, 5, 7, 5, 'X');
            b.Fill(0, 1, 0, 4, '#').Fill(0, 7, 0, 14, '#');
            b.Fill(1, 20, 12, 20, '#');
            b.Fill(13, 20, 22, 21, '~');
            b.Fill(18, 1, 18, 19, '|');
            b.Set(5, 4, 'P').Set(37, 19, 'E');
            list.Add(new LevelDef
            {
                Name = "Overpass", Map = b.Build(), Fling = true,
                Hints = ["The gap is wider now. Fall further, fly further.",
                         "Flying through the grid wipes your portals - after you've used them."]
            });
        }

        // 20 - cube + gel: carry a cube up to a button on the ledge
        {
            var b = LevelBuilder.Room();
            b.Fill(27, 7, 38, 19, 'X');
            b.Fill(22, 20, 24, 20, 'J');
            b.Fill(21, 0, 25, 0, '#');
            b.Fill(0, 16, 0, 19, '#');
            b.Set(30, 6, '1');
            b.Fill(34, 1, 34, 2, 'X').Fill(34, 3, 34, 6, 'a');
            b.Set(8, 19, 'C').Set(4, 19, 'P').Set(37, 6, 'E');
            list.Add(new LevelDef
            {
                Name = "Bounce House", Map = b.Build(),
                Hints = ["The button is up on the ledge. So is the exit.",
                         "Cubes don't bounce while you hold them. You still do."]
            });
        }

        // 21 - final exam: cube room, laser room with a grid, fling room
        {
            var b = LevelBuilder.Room(60, 26);
            // Room A
            b.Fill(1, 12, 6, 12, 'X').Set(3, 11, 'C');
            b.Fill(1, 0, 6, 0, '#');
            b.Fill(8, 24, 13, 24, '#');
            b.Set(15, 23, '1');
            b.Fill(19, 1, 19, 19, 'X').Fill(19, 2, 19, 10, '#').Fill(19, 20, 19, 23, 'a');
            // Room B
            b.Fill(31, 1, 31, 23, '|');
            b.Fill(39, 1, 39, 19, 'X').Set(39, 4, '<').Set(39, 8, '5');
            b.Fill(39, 13, 39, 19, '#').Fill(39, 20, 39, 23, 'b');
            // Room C
            b.Fill(43, 12, 47, 12, 'X');
            b.Fill(43, 0, 47, 0, '#');
            b.Fill(41, 24, 47, 24, '#');
            b.Fill(48, 24, 54, 25, '~');
            b.Fill(55, 21, 58, 23, 'X');
            b.Set(10, 23, 'P').Set(57, 20, 'E');
            list.Add(new LevelDef
            {
                Name = "Final Exam", Map = b.Build(), Fling = true,
                Hints = ["Everything you've learned. Three rooms. One way out.",
                         "Remember: portal shots pass through emancipation grids."]
            });
        }

        for (int i = 0; i < list.Count; i++) list[i].Label = $"{i + 1:00}";
        return list.ToArray();
    }
}
