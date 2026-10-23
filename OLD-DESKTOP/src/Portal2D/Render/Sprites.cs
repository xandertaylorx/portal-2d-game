using Raylib_cs;
using Portal2D.Core;
using static Portal2D.Core.MathX;

namespace Portal2D.Render;

/// <summary>Pixel palette shared by every sprite and tile.</summary>
public static class PP
{
    public static readonly Color Clear = new(0, 0, 0, 0);
    public static readonly Color Ink = C(16, 17, 22);
    // hero (terracotta critter)
    public static readonly Color Body = C(217, 119, 87);
    public static readonly Color BodyHi = C(242, 165, 132);
    public static readonly Color BodyLo = C(176, 92, 62);
    public static readonly Color BodyOut = C(48, 20, 13);
    public static readonly Color Eye = C(26, 16, 16);
    // panels
    public static readonly Color Cream = C(232, 226, 213);
    public static readonly Color CreamHi = C(247, 243, 234);
    public static readonly Color CreamSeam = C(189, 182, 168);
    public static readonly Color CreamLo = C(176, 168, 152);
    public static readonly Color CreamNoise = C(220, 213, 199);
    public static readonly Color TileOut = C(28, 30, 37);
    // metal
    public static readonly Color Metal = C(69, 75, 90);
    public static readonly Color MetalHi = C(115, 124, 147);
    public static readonly Color MetalMid = C(88, 95, 114);
    public static readonly Color MetalLo = C(50, 55, 68);
    public static readonly Color MetalSeam = C(42, 46, 57);
    public static readonly Color MetalDeep = C(36, 39, 49);
    public static readonly Color MetalDeep2 = C(42, 46, 57);
    // background
    public static readonly Color Bg = C(29, 33, 43);
    public static readonly Color BgSeam = C(20, 23, 30);
    public static readonly Color BgHi = C(38, 43, 55);
    public static readonly Color BgDetail = C(24, 27, 35);
    // portals
    public static readonly Color[] BlueRamp = [C(14, 44, 96), C(30, 110, 214), C(63, 167, 255), C(166, 220, 255), C(236, 248, 255)];
    public static readonly Color[] OrangeRamp = [C(96, 38, 6), C(196, 86, 14), C(255, 138, 31), C(255, 208, 138), C(255, 245, 225)];
    // misc
    public static readonly Color GooTop = C(200, 245, 138);
    public static readonly Color Goo1 = C(143, 214, 85);
    public static readonly Color Goo2 = C(92, 158, 51);
    public static readonly Color Goo3 = C(61, 112, 34);
    public static readonly Color Goo4 = C(41, 78, 23);
    public static readonly Color Laser = C(255, 59, 59);
    public static readonly Color LaserCore = C(255, 228, 220);
    public static readonly Color Exit = C(94, 224, 138);
    public static readonly Color Hazard = C(245, 197, 66);
    public static readonly Color Fizz = C(143, 211, 255);
    public static readonly Color Gel = C(63, 167, 255);
    public static readonly Color SpeedGel = C(255, 138, 40);
}

public static class Sprites
{
    public const int PlayerFrames = 8;
    public static readonly Pix PlayerSheet = BuildPlayerSheet();
    public static readonly Pix Cube = BuildCube();
    public static readonly Pix ExitDoor = BuildExit();
    public static readonly Pix Lamp = BuildLamp();
    public const int PortalFrames = 6;
    // [color][linked?1:0] -> sheet of frames, vertical (12x40 each) and horizontal (40x12 each)
    public static readonly Pix[,] PortalV = new Pix[2, 2];
    public static readonly Pix[,] PortalH = new Pix[2, 2];

    static Sprites()
    {
        for (int c = 0; c < 2; c++)
            for (int l = 0; l < 2; l++)
            {
                PortalV[c, l] = BuildPortal(c == 0 ? PP.BlueRamp : PP.OrangeRamp, l == 1, vertical: true);
                PortalH[c, l] = BuildPortal(c == 0 ? PP.BlueRamp : PP.OrangeRamp, l == 1, vertical: false);
            }
    }

    // ------------------------------------------------------------ hero

    /// <summary>Frames: 0 idle, 1 blink, 2-5 walk, 6 jump, 7 fall. 16x16 each, facing right.</summary>
    static Pix BuildPlayerSheet()
    {
        var sheet = new Pix(16 * PlayerFrames, 16);
        for (int f = 0; f < PlayerFrames; f++)
        {
            var p = new Pix(16, 16);
            int bob = f is 3 or 5 ? 1 : 0;           // body dips on passing frames
            int top = 3 + bob, bottom = 11 + bob;
            if (f == 6) { top -= 1; bottom -= 1; }

            // legs first (body overlaps their tops)
            int[] legX = [3, 5, 10, 12];
            for (int i = 0; i < 4; i++)
            {
                int x = legX[i], len = 3;
                if (f == 2 && (i == 0 || i == 2)) { len = 2; x += 1; }
                if (f == 4 && (i == 1 || i == 3)) { len = 2; x += 1; }
                if (f == 6) len = 2;
                if (f == 7) { len = 3; x += i < 2 ? -1 : 1; }
                p.Rect(x, bottom + 1, 1, len, PP.BodyLo);
            }

            // body
            p.Rect(2, top, 12, bottom - top + 1, PP.Body);
            p.Set(2, top, PP.Clear); p.Set(13, top, PP.Clear); p.Set(2, bottom, PP.Clear); p.Set(13, bottom, PP.Clear);
            p.HLine(3, top, 10, PP.BodyHi);
            p.VLine(2, top + 1, bottom - top - 1, PP.BodyHi);
            p.HLine(3, bottom, 10, PP.BodyLo);
            p.VLine(13, top + 1, bottom - top - 1, PP.BodyLo);
            p.Set(4, top + 1, PP.BodyHi);

            // little claws on the sides
            int armY = f is 6 or 7 ? top + 1 : top + 4;
            p.Rect(0, armY, 2, 2, PP.Body);
            p.Set(0, armY + 1, PP.BodyLo);
            p.Rect(14, armY, 2, 2, PP.Body);
            p.Set(15, armY + 1, PP.BodyLo);

            // eyes (looking right)
            if (f == 1) { p.Set(8, top + 4, PP.Eye); p.Set(11, top + 4, PP.Eye); }
            else
            {
                p.Rect(8, top + 3, 1, 2, PP.Eye);
                p.Rect(11, top + 3, 1, 2, PP.Eye);
            }
            p.Outline(PP.BodyOut);
            sheet.Draw(p, f * 16, 0);
        }
        return sheet;
    }

    // ------------------------------------------------------------ cube

    static Pix BuildCube()
    {
        var p = new Pix(16, 16);
        var body = C(143, 150, 163); var hi = C(185, 192, 204); var lo = C(96, 103, 122); var pad = C(92, 99, 117); var padHi = C(135, 144, 163);
        p.Rect(1, 1, 14, 14, body);
        p.HLine(2, 1, 12, hi); p.VLine(1, 2, 12, hi);
        p.HLine(2, 14, 12, lo); p.VLine(14, 2, 12, lo);
        foreach (var (x, y) in new[] { (1, 1), (11, 1), (1, 11), (11, 11) })
        {
            p.Rect(x, y, 4, 4, pad);
            p.Set(x + 1, y + 1, padHi);
            p.Set(x, y, PP.Clear);
        }
        p.Set(14, 1, PP.Clear); p.Set(1, 14, PP.Clear); p.Set(14, 14, PP.Clear);
        // edge notches
        p.Rect(7, 1, 2, 2, lo); p.Rect(7, 13, 2, 2, lo); p.Rect(1, 7, 2, 2, lo); p.Rect(13, 7, 2, 2, lo);
        // centre disc with a heart
        p.Rect(5, 5, 6, 6, C(232, 228, 220));
        p.Set(5, 5, body); p.Set(10, 5, body); p.Set(5, 10, body); p.Set(10, 10, body);
        var heart = C(255, 111, 168);
        p.Set(6, 6, heart); p.Set(9, 6, heart);
        p.HLine(6, 7, 4, heart);
        p.HLine(6, 8, 4, heart);
        p.Set(7, 9, heart); p.Set(8, 9, heart);
        p.Outline(C(29, 32, 40));
        return p;
    }

    // ------------------------------------------------------------ exit & props

    static Pix BuildExit()
    {
        var p = new Pix(28, 44);
        var frame = C(58, 65, 80); var frameHi = C(92, 101, 122);
        p.Rect(0, 0, 28, 44, frame);
        p.HLine(1, 0, 26, frameHi); p.VLine(0, 1, 43, frameHi);
        for (int y = 4; y < 44; y++)
        {
            float t = (y - 4) / 40f;
            var c = Mix(C(18, 60, 40), C(86, 220, 140), t * t);
            p.Rect(4, y, 20, 1, c);
        }
        p.Rect(4, 4, 20, 2, C(170, 255, 200));
        p.Set(0, 0, PP.Clear); p.Set(27, 0, PP.Clear);
        p.Outline(PP.TileOut);
        return p;
    }

    static Pix BuildLamp()
    {
        var p = new Pix(14, 4);
        p.Rect(0, 0, 14, 2, C(58, 65, 80));
        p.Rect(1, 2, 12, 1, C(255, 244, 214));
        p.Rect(2, 3, 10, 1, C(255, 232, 180));
        return p;
    }

    // ------------------------------------------------------------ portals

    static Pix BuildPortal(Color[] ramp, bool linked, bool vertical)
    {
        const int w = 12, h = 40;
        var sheet = vertical ? new Pix(w * PortalFrames, h) : new Pix(h * PortalFrames, w);
        for (int f = 0; f < PortalFrames; f++)
        {
            float spark = f / (float)PortalFrames * MathF.Tau;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - w / 2f) / (w / 2f), dy = (y + 0.5f - h / 2f) / (h / 2f - 1);
                    float e = dx * dx + dy * dy;
                    if (e > 1) continue;
                    float ang = MathF.Atan2(dy, dx);
                    float da = MathF.Abs(MathF.IEEERemainder(ang - spark, MathF.Tau));
                    Color c;
                    if (e > 0.78f) c = da < 0.5f ? ramp[4] : ramp[1];
                    else if (e > 0.5f) c = da < 0.7f ? ramp[4] : ramp[2];
                    else if (e > 0.36f) c = ramp[3];
                    else if (linked) continue;
                    else
                    {
                        // swirling membrane
                        float sw = MathF.Sin(ang * 2 + e * 9 - spark * 2);
                        c = sw > 0.3f ? ramp[1] : sw > -0.4f ? ramp[0] : Mix(ramp[0], PP.Ink, 0.4f);
                    }
                    int px = vertical ? f * w + x : f * h + y;
                    int py = vertical ? y : x;
                    sheet.Set(px, py, c);
                }
        }
        return sheet;
    }

    // ------------------------------------------------------------ textures

    public static Texture2D PlayerTex, CubeTex, ExitTex, LampTex;
    public static readonly Texture2D[,] PortalVTex = new Texture2D[2, 2], PortalHTex = new Texture2D[2, 2];
    static bool uploaded;

    public static void Upload()
    {
        PlayerTex = PlayerSheet.ToTexture();
        CubeTex = Cube.ToTexture();
        ExitTex = ExitDoor.ToTexture();
        LampTex = Lamp.ToTexture();
        for (int c = 0; c < 2; c++)
            for (int l = 0; l < 2; l++)
            {
                PortalVTex[c, l] = PortalV[c, l].ToTexture();
                PortalHTex[c, l] = PortalH[c, l].ToTexture();
            }
        uploaded = true;
    }

    public static void Unload()
    {
        if (!uploaded) return;
        foreach (var t in new[] { PlayerTex, CubeTex, ExitTex, LampTex }) Raylib.UnloadTexture(t);
        foreach (var t in PortalVTex) Raylib.UnloadTexture(t);
        foreach (var t in PortalHTex) Raylib.UnloadTexture(t);
    }
}
