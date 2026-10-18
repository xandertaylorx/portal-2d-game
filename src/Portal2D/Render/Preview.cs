using Raylib_cs;
using Portal2D.Core;
using Portal2D.Game;
using static Portal2D.Core.MathX;

namespace Portal2D.Render;

/// <summary>
/// CPU-only composite of a level (no GPU, no window) - used to eyeball pixel art from the command line:
///   Portal2D --preview 5 out.png      or      Portal2D --sprites out.png
/// </summary>
public static class Preview
{
    const int P = LevelArt.P;

    public static Pix Level(World w, float simulateSeconds = 0)
    {
        for (float t = 0; t < simulateSeconds; t += K.SimDt) w.Update(K.SimDt, default);
        w.Update(K.SimDt, default);
        var art = new LevelArt(w);
        var o = new Pix(art.Back.W, art.Back.H);
        o.Draw(art.Back, 0, 0);
        if (w.HasExit)
        {
            int ex = w.Exit.X * P + P / 2 - 14, ey = (w.Exit.Y + 1) * P - 44;
            o.Draw(Sprites.ExitDoor, ex, ey);
            o.Rect(ex + 1, ey - 12, 26, 10, C(20, 24, 28));
            PixelFont.DrawPix(o, "EXIT", ex + 3, ey - 10, 1, PP.Exit);
        }
        foreach (var b in w.Buttons)
        {
            int cx = b.X * P + P / 2, fy = (b.Y + 1) * P;
            o.Rect(cx - 14, fy - 3, 28, 3, C(43, 47, 58));
            o.HLine(cx - 14, fy - 3, 28, C(74, 81, 99));
            o.Rect(cx - 9, fy - 7, 18, 4, C(217, 69, 58));
            o.HLine(cx - 8, fy - 7, 16, C(255, 138, 122));
        }
        foreach (var c in w.Cubes) o.Draw(Sprites.Cube, (int)(c.Pos.X * K.Px) - 8, (int)(c.Pos.Y * K.Px) - 8);
        if (w.Player is { } p)
        {
            var frame = new Pix(16, 16);
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) frame.Set(x, y, Sprites.PlayerSheet.Get(x, y));
            o.Draw(frame, (int)(p.Pos.X * K.Px) - 8, (int)((p.Pos.Y + p.Half.Y) * K.Px) - 16);
        }
        o.Draw(art.Front, 0, 0);
        for (int ty = 0; ty < w.H; ty++)
            for (int tx = 0; tx < w.W; tx++)
            {
                char ch = w.At(tx, ty);
                int ox = tx * P, oy = ty * P;
                if (ch == '~')
                {
                    bool surf = w.At(tx, ty - 1) != '~';
                    for (int x = 0; x < P; x++)
                    {
                        int top = surf ? 3 + (int)MathF.Round(MathF.Sin((ox + x) * 0.45f) * 1.2f) : 0;
                        for (int y = top; y < P; y++)
                        {
                            var col = !surf ? PP.Goo4 : y == top ? PP.GooTop : y == top + 1 ? PP.Goo1 : y < 9 ? PP.Goo2 : PP.Goo3;
                            o.Set(ox + x, oy + y, col);
                        }
                    }
                }
                else if (ch == '|')
                {
                    o.BlendRect(ox + 3, oy, 10, P, C(42, 111, 191, 60));
                    foreach (int lx in new[] { 4, 8, 12 }) o.BlendRect(ox + lx, oy, 1, P, C(143, 211, 255, 150));
                }
                else if (World.IsDoor(ch))
                {
                    o.Rect(ox + 1, oy, 14, P, C(29, 31, 38));
                    o.Rect(ox + 3, oy, 10, P, C(93, 102, 124));
                    o.VLine(ox + 4, oy, P, C(122, 131, 153));
                    for (int y = 3; y < P; y += 4) o.HLine(ox + 3, oy + y, 10, C(70, 76, 92));
                }
            }
        foreach (var s in w.Lasers)
        {
            int ax = (int)(MathF.Min(s.A.X, s.B.X) * K.Px), ay = (int)(MathF.Min(s.A.Y, s.B.Y) * K.Px);
            int bx = (int)(MathF.Max(s.A.X, s.B.X) * K.Px), by = (int)(MathF.Max(s.A.Y, s.B.Y) * K.Px);
            if (ay == by) { o.Rect(ax, ay - 1, bx - ax, 3, PP.Laser); o.HLine(ax, ay, bx - ax, PP.LaserCore); }
            else { o.Rect(ax - 1, ay, 3, by - ay, PP.Laser); o.VLine(ax, ay, by - ay, PP.LaserCore); }
        }
        foreach (var pt in w.Portals)
        {
            if (pt == null) continue;
            int ci = (int)pt.Color, li = w.Linked ? 1 : 0;
            var sheet = pt.IsWall ? Sprites.PortalV[ci, li] : Sprites.PortalH[ci, li];
            int fw = pt.IsWall ? 12 : 40, fh = pt.IsWall ? 40 : 12;
            var frame = new Pix(fw, fh);
            for (int y = 0; y < fh; y++) for (int x = 0; x < fw; x++) frame.Set(x, y, sheet.Get(x, y));
            o.Draw(frame, (int)(pt.Center.X * K.Px) - fw / 2, (int)(pt.Center.Y * K.Px) - fh / 2);
        }
        return o;
    }

    /// <summary>README banner: a chamber backdrop, the title and the hero.</summary>
    public static Pix Banner()
    {
        var lvl = Level(new World(Levels.All[8]), 0.5f);
        var o = new Pix(640, 200);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o.Set(x, y, lvl.Get(x + 20, y + 60));
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o.Shade(x, y, 0.55f * MathF.Max(0, 1 - x / 520f) + 0.15f);
        void Outlined(string t, int x, int y, int sc, Color col)
        {
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (1, 1), (0, 2), (1, 2) })
                PixelFont.DrawPix(o, t, x + dx * sc, y + dy * sc, sc, PP.Ink);
            PixelFont.DrawPix(o, t, x, y, sc, col);
        }
        Outlined("PORTAL", 28, 38, 8, PP.CreamHi);
        Outlined("2D", 28 + PixelFont.PixWidth("PORTAL", 8) + 18, 38, 8, PP.OrangeRamp[2]);
        o.Rect(28, 108, 140, 4, PP.BlueRamp[2]);
        o.Rect(176, 108, 140, 4, PP.OrangeRamp[2]);
        PixelFont.DrawPix(o, "A PIXEL-ART PORTAL PUZZLER", 30, 126, 1, PP.Cream);
        PixelFont.DrawPix(o, "18 CHAMBERS  +  ENDLESS RANDOM TESTS", 30, 140, 1, C(150, 158, 172));
        var hero = new Pix(16, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) hero.Set(x, y, Sprites.PlayerSheet.Get(x, y));
        o.Draw(hero.Scaled(6), 470, 60, flipX: true);
        o.Rect(478, 157, 80, 4, C(0, 0, 0, 90));
        return o;
    }

    public static Pix SpriteSheet()
    {
        var o = new Pix(220, 64);
        o.Rect(0, 0, o.W, o.H, PP.Bg);
        o.Draw(Sprites.PlayerSheet, 2, 2);
        o.Draw(Sprites.Cube, 134, 2);
        o.Draw(Sprites.ExitDoor, 154, 2);
        o.Draw(Sprites.Lamp, 186, 2);
        for (int f = 0; f < 3; f++)
        {
            var src = Sprites.PortalV[f % 2, f / 2];
            var frame = new Pix(12, 40);
            for (int y = 0; y < 40; y++) for (int x = 0; x < 12; x++) frame.Set(x, y, src.Get(x, y));
            o.Draw(frame, 2 + f * 14, 20);
        }
        PixelFont.DrawPix(o, "PORTAL 2D: 0123456789!", 50, 22, 1, PP.Cream);
        PixelFont.DrawPix(o, "EXIT", 50, 34, 2, PP.Body);
        return o;
    }
}
