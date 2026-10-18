using Raylib_cs;
using Portal2D.Core;
using Portal2D.Game;
using static Portal2D.Core.MathX;

namespace Portal2D.Render;

/// <summary>
/// Bakes the static parts of a level into two pixel buffers (16 px per tile):
/// the back wall (drawn behind everything) and the solid tiles (drawn in front of characters,
/// which is what hides the half of a body that is inside a portal).
/// </summary>
public sealed class LevelArt
{
    public const int P = 16;
    public readonly Pix Back, Front;

    public LevelArt(World w)
    {
        Back = new Pix(w.W * P, w.H * P);
        Front = new Pix(w.W * P, w.H * P);
        PaintBack(w);
        for (int ty = 0; ty < w.H; ty++)
            for (int tx = 0; tx < w.W; tx++)
                PaintTile(w, tx, ty);
    }

    static bool StaticSolid(char c) => c is '#' or 'X' or '<' or '>' or '^' or 'v' or 'J' || World.IsReceiver(c);
    static bool Mass(World w, int x, int y) => x < 0 || y < 0 || x >= w.W || y >= h(w) || StaticSolid(w.At(x, y));
    static int h(World w) => w.H;

    // ------------------------------------------------------------ back wall

    void PaintBack(World w)
    {
        var b = Back;
        b.Rect(0, 0, b.W, b.H, PP.Bg);
        const int panel = 64;
        for (int py = 0; py * panel < b.H; py++)
            for (int px = 0; px * panel < b.W; px++)
            {
                int x0 = px * panel, y0 = py * panel;
                float hsh = Hash(px, py, 21);
                var tone = Mix(PP.Bg, C(33, 38, 49), Hash(px, py, 5));
                b.Rect(x0, y0, panel, panel, tone);
                b.HLine(x0, y0, panel, PP.BgSeam);
                b.VLine(x0, y0, panel, PP.BgSeam);
                b.HLine(x0 + 1, y0 + 1, panel - 2, PP.BgHi);
                b.VLine(x0 + 1, y0 + 1, panel - 2, PP.BgHi);
                foreach (var (rx, ry) in new[] { (4, 4), (59, 4), (4, 59), (59, 59) })
                {
                    b.Set(x0 + rx, y0 + ry, C(48, 54, 68));
                    b.Set(x0 + rx + 1, y0 + ry + 1, PP.BgSeam);
                }
                if (hsh < 0.12f)
                {
                    // vent
                    for (int k = 0; k < 5; k++)
                    {
                        b.Rect(x0 + 18, y0 + 20 + k * 5, 28, 2, C(15, 17, 23));
                        b.HLine(x0 + 18, y0 + 22 + k * 5, 28, C(44, 50, 63));
                    }
                }
                else if (hsh < 0.2f)
                {
                    // wall monitor
                    b.Rect(x0 + 16, y0 + 18, 32, 22, C(44, 50, 63));
                    b.Rect(x0 + 18, y0 + 20, 28, 18, C(12, 26, 32));
                    for (int k = 0; k < 4; k++)
                    {
                        int len = 6 + (int)(Hash(px * 7 + k, py, 9) * 18);
                        b.HLine(x0 + 20, y0 + 22 + k * 4, len, C(63, 191, 191, 200));
                    }
                }
                else if (hsh < 0.26f)
                {
                    // hazard band
                    for (int i = 0; i < 40; i++)
                        for (int j = 0; j < 6; j++)
                            b.Set(x0 + 12 + i, y0 + 46 + j, ((i + j) / 4) % 2 == 0 ? C(120, 100, 40) : C(30, 30, 30));
                }
                else if (hsh < 0.33f)
                {
                    // faint ring emblem
                    for (int y = 0; y < 30; y++)
                        for (int x = 0; x < 30; x++)
                        {
                            float d = MathF.Sqrt((x - 14.5f) * (x - 14.5f) + (y - 14.5f) * (y - 14.5f));
                            if (d > 11 && d < 14) b.Set(x0 + 17 + x, y0 + 17 + y, C(40, 46, 59));
                            if (d < 4) b.Set(x0 + 17 + x, y0 + 17 + y, C(40, 46, 59));
                        }
                }
            }

        // pipes running along some rows
        for (int ty = 2; ty < w.H - 3; ty++)
        {
            if (Hash(ty, 3, 77) > 0.12f) continue;
            int y = ty * P + 6;
            b.Rect(0, y, b.W, 5, C(46, 52, 66));
            b.HLine(0, y, b.W, C(70, 78, 96));
            b.HLine(0, y + 4, b.W, C(26, 29, 37));
            for (int x = 20; x < b.W; x += 72)
            {
                b.Rect(x, y - 1, 4, 7, C(80, 88, 108));
                b.VLine(x + 3, y - 1, 7, C(40, 45, 58));
            }
        }

        // warm safety stripe along the lower wall
        int stripeY = (w.H - 5) * P + 2;
        b.Rect(0, stripeY, b.W, 1, C(190, 176, 150));
        b.Rect(0, stripeY + 2, b.W, 4, C(128, 66, 48));
        b.Rect(0, stripeY + 6, b.W, 1, C(70, 36, 28));

        // big painted chamber number in the first open area near the top
        if (w.Def.Label.Length > 0)
        {
            int s = 7, tw = PixelFont.PixWidth(w.Def.Label, s) + 8, th = 7 * s + 8;
            for (int ty = 2; ty < w.H / 2; ty++)
            {
                bool placed = false;
                for (int tx = w.W - 3 - tw / P; tx >= 2; tx--)
                {
                    int x0 = tx * P, y0 = ty * P;
                    bool free = true;
                    for (int cy = y0 / P; cy <= (y0 + th) / P && free; cy++)
                        for (int cx = x0 / P; cx <= (x0 + tw) / P && free; cx++)
                            if (w.At(cx, cy) != ' ') free = false;
                    if (!free) continue;
                    b.Rect(x0 - 2, y0, tw + 4, th + 6, C(31, 35, 46));
                    PixelFont.DrawPix(b, w.Def.Label, x0 + 4, y0 + 6, s, C(40, 46, 60));
                    PixelFont.DrawPix(b, w.Def.Label, x0 + 2, y0 + 4, s, C(52, 59, 75));
                    b.Rect(x0 + 2, y0 + th + 2, tw - 4, 3, C(128, 66, 48));
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }

        // ambient occlusion: tiles cast a soft shadow down-right onto the wall
        var shadow = new bool[b.W * b.H];
        for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                int sx = (x - 3) / P, sy = (y - 4) / P;
                if (x - 3 >= 0 && y - 4 >= 0 && IsShadowCaster(w.At(sx, sy))) shadow[y * b.W + x] = true;
            }
        for (int i = 0; i < shadow.Length; i++)
            if (shadow[i]) b.Shade(i % b.W, i / b.W, 0.4f);
    }

    static bool IsShadowCaster(char c) => StaticSolid(c) || World.IsDoor(c);

    // ------------------------------------------------------------ tiles

    void PaintTile(World w, int tx, int ty)
    {
        char c = w.At(tx, ty);
        switch (c)
        {
            case '#': Panel(w, tx, ty); break;
            case 'X': Metal(w, tx, ty); break;
            case '=': Glass(w, tx, ty); break;
            case '<' or '>' or '^' or 'v': Metal(w, tx, ty); Emitter(tx, ty, c); break;
            case var rc when World.IsReceiver(rc): Metal(w, tx, ty); Receiver(tx, ty); break;
            case 'J': Metal(w, tx, ty); Gel(w, tx, ty); break;
        }
    }

    struct Edges { public bool U, D, L, R, UL, UR, DL, DR; }

    static Edges Exposed(World w, int x, int y) => new()
    {
        U = !Mass(w, x, y - 1), D = !Mass(w, x, y + 1), L = !Mass(w, x - 1, y), R = !Mass(w, x + 1, y),
        UL = !Mass(w, x - 1, y - 1), UR = !Mass(w, x + 1, y - 1), DL = !Mass(w, x - 1, y + 1), DR = !Mass(w, x + 1, y + 1)
    };

    void ApplyEdges(int ox, int oy, Edges e, Color hi, Color hi2, Color lo, Color outline)
    {
        var f = Front;
        if (e.U) { f.HLine(ox, oy + 1, P, hi); f.HLine(ox, oy + 2, P, hi2); }
        if (e.L) f.VLine(ox + 1, oy, P, hi2);
        if (e.D) { f.HLine(ox, oy + P - 2, P, lo); f.HLine(ox, oy + P - 3, P, Mix(lo, hi2, 0.4f)); }
        if (e.R) f.VLine(ox + P - 2, oy, P, lo);
        if (e.U) f.HLine(ox, oy, P, outline);
        if (e.D) f.HLine(ox, oy + P - 1, P, outline);
        if (e.L) f.VLine(ox, oy, P, outline);
        if (e.R) f.VLine(ox + P - 1, oy, P, outline);
        // rounded outer corners, dotted inner corners
        if (e.U && e.L) { f.Set(ox, oy, PP.Clear); f.Set(ox + 1, oy + 1, outline); }
        if (e.U && e.R) { f.Set(ox + P - 1, oy, PP.Clear); f.Set(ox + P - 2, oy + 1, outline); }
        if (e.D && e.L) { f.Set(ox, oy + P - 1, PP.Clear); f.Set(ox + 1, oy + P - 2, outline); }
        if (e.D && e.R) { f.Set(ox + P - 1, oy + P - 1, PP.Clear); f.Set(ox + P - 2, oy + P - 2, outline); }
        if (!e.U && !e.L && e.UL) f.Set(ox, oy, outline);
        if (!e.U && !e.R && e.UR) f.Set(ox + P - 1, oy, outline);
        if (!e.D && !e.L && e.DL) f.Set(ox, oy + P - 1, outline);
        if (!e.D && !e.R && e.DR) f.Set(ox + P - 1, oy + P - 1, outline);
    }

    void Panel(World w, int tx, int ty)
    {
        var f = Front;
        int ox = tx * P, oy = ty * P;
        for (int y = 0; y < P; y++)
            for (int x = 0; x < P; x++)
            {
                var col = PP.Cream;
                if ((tx % 2 == 0 && x == 0) || (ty % 2 == 0 && y == 0)) col = PP.CreamSeam;
                else if ((tx % 2 == 0 && x == 1) || (ty % 2 == 0 && y == 1)) col = PP.CreamHi;
                else if ((tx % 2 == 1 && x == P - 1) || (ty % 2 == 1 && y == P - 1)) col = C(214, 207, 192);
                else if (Hash(ox + x, oy + y, 3) < 0.035f) col = PP.CreamNoise;
                f.Set(ox + x, oy + y, col);
            }
        ApplyEdges(ox, oy, Exposed(w, tx, ty), C(255, 253, 247), PP.CreamHi, PP.CreamLo, PP.TileOut);
    }

    void Metal(World w, int tx, int ty)
    {
        var f = Front;
        int ox = tx * P, oy = ty * P;
        var e = Exposed(w, tx, ty);
        bool interior = !(e.U || e.D || e.L || e.R || e.UL || e.UR || e.DL || e.DR);
        for (int y = 0; y < P; y++)
            for (int x = 0; x < P; x++)
            {
                Color col;
                if (interior)
                {
                    col = PP.MetalDeep;
                    if ((tx % 2 == 0 && x == 0) || (ty % 2 == 0 && y == 0)) col = C(30, 33, 42);
                    else if (((ox + x) + (oy + y)) % 6 == 0) col = PP.MetalDeep2;
                }
                else
                {
                    col = PP.Metal;
                    if ((tx % 2 == 0 && x == 0) || (ty % 2 == 0 && y == 0)) col = PP.MetalSeam;
                    else if ((tx % 2 == 0 && x == 1) || (ty % 2 == 0 && y == 1)) col = PP.MetalMid;
                    else if (((ox + x) - (oy + y)) % 7 == 0) col = C(74, 80, 96);
                }
                f.Set(ox + x, oy + y, col);
            }
        if (!interior)
        {
            int rx = tx % 2 == 0 ? 3 : P - 4, ry = ty % 2 == 0 ? 3 : P - 4;
            f.Set(ox + rx, oy + ry, C(130, 139, 160));
            f.Set(ox + rx + 1, oy + ry + 1, PP.MetalSeam);
        }
        ApplyEdges(ox, oy, e, PP.MetalHi, PP.MetalMid, PP.MetalLo, C(17, 18, 23));
    }

    void Glass(World w, int tx, int ty)
    {
        var f = Front;
        int ox = tx * P, oy = ty * P;
        f.Rect(ox, oy, P, P, C(140, 210, 255, 46));
        bool u = w.At(tx, ty - 1) != '=', d = w.At(tx, ty + 1) != '=', l = w.At(tx - 1, ty) != '=', r = w.At(tx + 1, ty) != '=';
        var edge = C(212, 241, 255, 220);
        var edge2 = C(159, 216, 255, 110);
        if (u) { f.HLine(ox, oy, P, edge); f.HLine(ox, oy + 1, P, edge2); }
        if (d) { f.HLine(ox, oy + P - 1, P, edge); f.HLine(ox, oy + P - 2, P, edge2); }
        if (l) { f.VLine(ox, oy, P, edge); f.VLine(ox + 1, oy, P, edge2); }
        if (r) { f.VLine(ox + P - 1, oy, P, edge); f.VLine(ox + P - 2, oy, P, edge2); }
        for (int i = 0; i < P; i++)
        {
            int k = (i + ty * 5 + tx * 3) % 20;
            if (k < 2) for (int j = 0; j < 2; j++) f.Blend(ox + i, oy + (P - 1 - i + j) % P, C(255, 255, 255, 70));
        }
    }

    void Emitter(int tx, int ty, char dir)
    {
        var f = Front;
        int ox = tx * P, oy = ty * P;
        var house = C(29, 31, 38);
        switch (dir)
        {
            case '>': f.Rect(ox + 11, oy + 3, 5, 10, house); f.Rect(ox + 14, oy + 5, 2, 6, PP.Laser); f.Rect(ox + 15, oy + 7, 1, 2, PP.LaserCore); break;
            case '<': f.Rect(ox, oy + 3, 5, 10, house); f.Rect(ox, oy + 5, 2, 6, PP.Laser); f.Rect(ox, oy + 7, 1, 2, PP.LaserCore); break;
            case 'v': f.Rect(ox + 3, oy + 11, 10, 5, house); f.Rect(ox + 5, oy + 14, 6, 2, PP.Laser); f.Rect(ox + 7, oy + 15, 2, 1, PP.LaserCore); break;
            case '^': f.Rect(ox + 3, oy, 10, 5, house); f.Rect(ox + 5, oy, 6, 2, PP.Laser); f.Rect(ox + 7, oy, 2, 1, PP.LaserCore); break;
        }
    }

    void Receiver(int tx, int ty)
    {
        var f = Front;
        int ox = tx * P, oy = ty * P;
        for (int y = 0; y < P; y++)
            for (int x = 0; x < P; x++)
            {
                float d = MathF.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f));
                if (d < 6.2f) f.Set(ox + x, oy + y, C(29, 31, 38));
                if (d < 4.2f) f.Set(ox + x, oy + y, C(74, 32, 24));
                if (d >= 6.2f && d < 7.2f) f.Set(ox + x, oy + y, C(110, 118, 138));
            }
    }

    void Gel(World w, int tx, int ty)
    {
        if (Mass(w, tx, ty - 1)) return;
        var f = Front;
        int ox = tx * P, oy = ty * P;
        f.Rect(ox, oy, P, 6, PP.Gel);
        f.HLine(ox, oy, P, C(20, 60, 120));
        f.HLine(ox, oy + 1, P, C(166, 220, 255));
        f.HLine(ox, oy + 5, P, C(28, 95, 184));
        for (int x = 0; x < P; x += 5) f.Set(ox + x + (tx % 3), oy + 3, C(220, 242, 255));
    }

    // ------------------------------------------------------------ textures

    public Texture2D BackTex, FrontTex;
    public bool Uploaded;

    public void Upload()
    {
        BackTex = Back.ToTexture();
        FrontTex = Front.ToTexture();
        Uploaded = true;
    }

    public void Unload()
    {
        if (!Uploaded) return;
        Raylib.UnloadTexture(BackTex);
        Raylib.UnloadTexture(FrontTex);
        Uploaded = false;
    }
}
