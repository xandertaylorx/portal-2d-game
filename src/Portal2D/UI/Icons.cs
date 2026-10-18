using System.Numerics;
using Raylib_cs;
using Portal2D.Game;
using static Portal2D.Core.MathX;

namespace Portal2D;

/// <summary>Little pictograms for the mechanics used in a chamber (like the signs in the original game).</summary>
public static class Icons
{
    enum Mech { Portal, Cube, Button, Goo, Grid, Laser, Glass, Fling }

    static List<Mech> Mechanics(LevelDef def)
    {
        var all = string.Concat(def.Map);
        var list = new List<Mech> { Mech.Portal };
        if (all.Contains('C')) list.Add(Mech.Cube);
        if (all.Any(ch => World.ButtonGroup(ch) >= 0)) list.Add(Mech.Button);
        if (all.Contains('~')) list.Add(Mech.Goo);
        if (all.Contains('|')) list.Add(Mech.Grid);
        if (all.IndexOfAny(['<', '>', '^', 'v']) >= 0) list.Add(Mech.Laser);
        if (all.Contains('=')) list.Add(Mech.Glass);
        if (def.Fling) list.Add(Mech.Fling);
        return list;
    }

    public static void Draw(LevelDef def, Vector2 pos, float size, Color col, bool alignRight, bool filled = false)
    {
        var list = Mechanics(def);
        float step = size + 6;
        for (int i = 0; i < list.Count; i++)
        {
            float x = alignRight ? pos.X - size - i * step : pos.X + i * step;
            var r = new Rectangle(x, pos.Y, size, size);
            if (filled) Raylib.DrawRectangleRounded(r, 0.2f, 4, col);
            else Raylib.DrawRectangleRoundedLinesEx(r, 0.2f, 4, 1.5f, col);
            var fg = filled ? C(236, 240, 243, col.A) : col;
            Glyph(list[i], new Vector2(x + size / 2, pos.Y + size / 2), size * 0.5f, fg);
        }
    }

    static void Glyph(Mech m, Vector2 c, float s, Color col)
    {
        switch (m)
        {
            case Mech.Portal:
                Raylib.DrawEllipseLines((int)c.X - (int)(s * 0.35f), (int)c.Y, s * 0.22f, s * 0.75f, col);
                Raylib.DrawEllipseLines((int)c.X + (int)(s * 0.35f), (int)c.Y, s * 0.22f, s * 0.75f, col);
                break;
            case Mech.Cube:
                Raylib.DrawRectangleLinesEx(new Rectangle(c.X - s * 0.6f, c.Y - s * 0.6f, s * 1.2f, s * 1.2f), 1.5f, col);
                Raylib.DrawCircleV(c, s * 0.22f, col);
                break;
            case Mech.Button:
                Raylib.DrawRectangle((int)(c.X - s * 0.7f), (int)(c.Y + s * 0.3f), (int)(s * 1.4f), (int)(s * 0.25f), col);
                Raylib.DrawCircleSector(c + new Vector2(0, s * 0.3f), s * 0.45f, 180, 360, 10, col);
                break;
            case Mech.Goo:
                for (int k = 0; k < 2; k++)
                {
                    float y = c.Y - s * 0.2f + k * s * 0.45f;
                    Raylib.DrawSplineSegmentBezierQuadratic(new Vector2(c.X - s * 0.7f, y), new Vector2(c.X - s * 0.35f, y - s * 0.4f), new Vector2(c.X, y), 1.5f, col);
                    Raylib.DrawSplineSegmentBezierQuadratic(new Vector2(c.X, y), new Vector2(c.X + s * 0.35f, y + s * 0.4f), new Vector2(c.X + s * 0.7f, y), 1.5f, col);
                }
                break;
            case Mech.Grid:
                for (int k = -1; k <= 1; k++)
                    Raylib.DrawLineEx(new Vector2(c.X + k * s * 0.4f, c.Y - s * 0.7f), new Vector2(c.X + k * s * 0.4f, c.Y + s * 0.7f), 1.5f, col);
                break;
            case Mech.Laser:
                Raylib.DrawCircleV(c + new Vector2(-s * 0.5f, 0), s * 0.25f, col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.5f, 0), c + new Vector2(s * 0.75f, 0), 2, col);
                Raylib.DrawLineEx(c + new Vector2(s * 0.45f, -s * 0.35f), c + new Vector2(s * 0.75f, 0), 1.5f, col);
                Raylib.DrawLineEx(c + new Vector2(s * 0.45f, s * 0.35f), c + new Vector2(s * 0.75f, 0), 1.5f, col);
                break;
            case Mech.Glass:
                Raylib.DrawRectangleLinesEx(new Rectangle(c.X - s * 0.55f, c.Y - s * 0.7f, s * 1.1f, s * 1.4f), 1.5f, col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.3f, s * 0.3f), c + new Vector2(s * 0.2f, -s * 0.3f), 1.5f, col);
                break;
            case Mech.Fling:
                Raylib.DrawSplineSegmentBezierQuadratic(c + new Vector2(-s * 0.7f, s * 0.6f), c + new Vector2(-s * 0.5f, -s * 0.8f), c + new Vector2(s * 0.6f, -s * 0.2f), 2, col);
                Raylib.DrawTriangle(c + new Vector2(s * 0.75f, -s * 0.15f), c + new Vector2(s * 0.35f, -s * 0.45f), c + new Vector2(s * 0.4f, s * 0.05f), col);
                break;
        }
    }
}
