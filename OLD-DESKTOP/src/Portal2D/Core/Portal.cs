using System.Numerics;

namespace Portal2D.Core;

public enum PortalColor { Blue = 0, Orange = 1 }

/// <summary>
/// A portal lives on the face of two adjacent solid tiles. It is described by a centre on the
/// wall surface, an outward normal (axis aligned) and an in-plane "up" vector.
///
/// The portal transform maps the space in front of portal A to the space in front of portal B:
///   normal(A) -> -normal(B)   (walking *into* A means walking *out of* B)
///   up(A)     ->  up(B)       (walls keep world-up; floors/ceilings keep world-right)
/// That makes wall portals behave like a hole through the wall (never upside down), and
/// floor-to-floor portals mirror vertically - the natural 2D cross-section of a 3D portal.
/// The same transform drives teleporting, momentum, lasers and the see-through rendering.
/// </summary>
public sealed class Portal
{
    public const float HalfLen = 32f;

    public PortalColor Color;
    public Vector2 Center;
    public Vector2 Normal;
    public Vector2 Up;
    public Int2 TileA, TileB;
    public bool Fixed;
    public float Open;      // 0..1 opening animation
    public float Age;

    public Portal(PortalColor color, Vector2 center, Vector2 normal, Int2 a, Int2 b, bool isFixed)
    {
        Color = color;
        Center = center;
        Normal = normal;
        Up = MathF.Abs(normal.X) > 0.5f ? new Vector2(0, -1) : new Vector2(1, 0);
        TileA = a;
        TileB = b;
        Fixed = isFixed;
    }

    public bool IsWall => MathF.Abs(Normal.X) > 0.5f;
    public Vector2 End1 => Center + Up * HalfLen;
    public Vector2 End2 => Center - Up * HalfLen;
    public bool Covers(int x, int y) => (TileA.X == x && TileA.Y == y) || (TileB.X == x && TileB.Y == y);

    public float U(Vector2 p) => Vector2.Dot(p - Center, Up);
    public float V(Vector2 p) => Vector2.Dot(p - Center, Normal);

    public static Vector2 MapDir(Portal a, Portal b, Vector2 d)
    {
        float n = Vector2.Dot(d, a.Normal);
        float u = Vector2.Dot(d, a.Up);
        return -n * b.Normal + u * b.Up;
    }

    public static Vector2 MapPoint(Portal a, Portal b, Vector2 p) => b.Center + MapDir(a, b, p - a.Center);

    /// <summary>Column-major 4x4 matrix (OpenGL layout) for the affine map a -> b.</summary>
    public static void Matrix(Portal a, Portal b, Span<float> m)
    {
        var ex = MapDir(a, b, new Vector2(1, 0));
        var ey = MapDir(a, b, new Vector2(0, 1));
        var t = b.Center - (ex * a.Center.X + ey * a.Center.Y);
        m.Clear();
        m[0] = ex.X; m[1] = ex.Y;
        m[4] = ey.X; m[5] = ey.Y;
        m[10] = 1;
        m[12] = t.X; m[13] = t.Y;
        m[15] = 1;
    }
}
