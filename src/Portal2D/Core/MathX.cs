using System.Numerics;
using Raylib_cs;

namespace Portal2D.Core;

public static class K
{
    public const int Tile = 32;
    public const int ScreenW = 1280;
    public const int ScreenH = 720;
    public const int SS = 2; // supersampling factor for the off-screen canvas
    public const int CanvasW = ScreenW * SS;
    public const int CanvasH = ScreenH * SS;
    public const float SimDt = 1f / 120f;
}

public readonly record struct Int2(int X, int Y)
{
    public static Int2 operator +(Int2 a, Int2 b) => new(a.X + b.X, a.Y + b.Y);
}

public static class MathX
{
    public static float Approach(float v, float target, float delta)
    {
        if (v < target) return MathF.Min(v + delta, target);
        return MathF.Max(v - delta, target);
    }

    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;
    public static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3 - 2 * t); }
    public static float EaseOutBack(float t)
    {
        t = Clamp01(t);
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }
    public static float EaseOutCubic(float t) { t = Clamp01(t); return 1 - MathF.Pow(1 - t, 3); }

    public static Vector2 SafeNormalize(Vector2 v, Vector2 fallback)
    {
        float l = v.Length();
        return l < 1e-5f ? fallback : v / l;
    }

    /// <summary>Deterministic per-cell hash in [0,1).</summary>
    public static float Hash(int x, int y, int seed = 0)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    public static Color C(int r, int g, int b, int a = 255) => new((byte)r, (byte)g, (byte)b, (byte)Math.Clamp(a, 0, 255));
    public static Color WithAlpha(this Color c, float a) => new(c.R, c.G, c.B, (byte)Math.Clamp((int)(a * 255), 0, 255));
    public static Color WithAlpha(this Color c, int a) => new(c.R, c.G, c.B, (byte)Math.Clamp(a, 0, 255));
    public static Color Mix(Color a, Color b, float t)
    {
        t = Clamp01(t);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }

    public static bool Overlaps(Rectangle a, Rectangle b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    /// <summary>Ray vs axis-aligned box (slab test). Returns entry distance or -1.</summary>
    public static float RayAabb(Vector2 o, Vector2 d, Rectangle r)
    {
        float tmin = 0, tmax = float.MaxValue;
        if (MathF.Abs(d.X) < 1e-6f) { if (o.X < r.X || o.X > r.X + r.Width) return -1; }
        else
        {
            float t1 = (r.X - o.X) / d.X, t2 = (r.X + r.Width - o.X) / d.X;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1); tmax = MathF.Min(tmax, t2);
        }
        if (MathF.Abs(d.Y) < 1e-6f) { if (o.Y < r.Y || o.Y > r.Y + r.Height) return -1; }
        else
        {
            float t1 = (r.Y - o.Y) / d.Y, t2 = (r.Y + r.Height - o.Y) / d.Y;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1); tmax = MathF.Min(tmax, t2);
        }
        return tmax >= tmin ? tmin : -1;
    }

    /// <summary>Distance from point p to segment ab.</summary>
    public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float l2 = ab.LengthSquared();
        float t = l2 < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }
}
