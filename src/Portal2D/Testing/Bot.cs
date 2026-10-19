using System.Numerics;
using Portal2D.Core;
using Portal2D.Game;

namespace Portal2D.Testing;

/// <summary>
/// A tiny input-driving bot. Solutions are written as lazy sequences of per-frame inputs so the exact
/// same physics code the player uses can be verified headlessly (see <see cref="TestRunner"/>).
/// </summary>
public sealed class Bot
{
    public readonly World W;
    public Vector2 Aim;
    const float Frame = 1 / 60f;
    const int T = K.Tile;

    public Bot(World w)
    {
        W = w;
        Aim = P.Pos + new Vector2(200, 0);
    }

    Player P => W.Player!;

    public InputState In(int mx = 0, bool jump = false, bool use = false, bool blue = false, bool orange = false) => new()
    {
        MoveX = mx, JumpPressed = jump, JumpHeld = true, UsePressed = use,
        FireBlue = blue, FireOrange = orange, AimWorld = Aim
    };

    // ---- aim targets (points just inside a tile face, choosing which two tiles the portal covers)
    /// <summary>Floor/top face of tile row, portal covering columns c and c+1.</summary>
    public static Vector2 Floor(int c, int row) => new(c * T + 24, row * T + 0.5f);
    /// <summary>Ceiling/bottom face of tile row, portal covering columns c and c+1.</summary>
    public static Vector2 Ceil(int c, int row) => new(c * T + 24, (row + 1) * T - 0.5f);
    /// <summary>Wall face pointing right (+x) of tile column, covering rows r and r+1.</summary>
    public static Vector2 WallR(int col, int r) => new((col + 1) * T - 0.5f, r * T + 24);
    /// <summary>Wall face pointing left (-x) of tile column, covering rows r and r+1.</summary>
    public static Vector2 WallL(int col, int r) => new(col * T + 0.5f, r * T + 24);
    public static float X(int col) => col * T + T / 2f;

    public IEnumerable<InputState> Wait(float s)
    {
        for (int i = 0; i < s * 60; i++) yield return In();
    }

    public IEnumerable<InputState> Fire(PortalColor c, Vector2 target)
    {
        Aim = target;
        yield return In();
        yield return In(blue: c == PortalColor.Blue, orange: c == PortalColor.Orange);
        foreach (var i in Wait(0.3f)) yield return i;
    }

    public IEnumerable<InputState> AimAt(Vector2 target)
    {
        Aim = target;
        yield return In();
        yield return In();
    }

    /// <summary>Swing the held cube toward a point, let it settle, then let go.</summary>
    public IEnumerable<InputState> Drop(Vector2 toward) => Seq(AimAt(toward), Wait(0.35f), Use());

    /// <summary>Jump while holding a direction, then keep holding it until landing past x.</summary>
    public IEnumerable<InputState> JumpTo(int dir, Func<bool> landed, float timeout = 6)
    {
        yield return In(dir, jump: true);
        float t = 0;
        while (!landed() && t < timeout && !P.Dead) { yield return In(dir); t += Frame; }
    }

    public IEnumerable<InputState> Use()
    {
        yield return In(use: true);
        foreach (var i in Wait(0.2f)) yield return i;
    }

    /// <summary>Walk to an x position, auto-jumping obstacles (and optionally cubes in the way).</summary>
    public IEnumerable<InputState> WalkTo(float x, bool hopCubes = false, Func<bool>? stopIf = null, float timeout = 12, Vector2? aimRel = null)
    {
        float t = 0;
        int stuck = 0;
        while ((MathF.Abs(P.Pos.X - x) > 3 || !P.OnGround) && t < timeout && !P.Dead && !P.Won && !(stopIf?.Invoke() ?? false))
        {
            if (aimRel is { } ar) Aim = P.Pos + ar;
            float dx = x - P.Pos.X;
            int dir = Math.Sign(dx);
            float v = MathF.Abs(P.Vel.X);
            float stop = v * v / (2 * 2600) + 2;
            if (MathF.Abs(dx) <= stop && MathF.Sign(P.Vel.X) == dir) dir = 0;
            bool jump = false;
            if (dir != 0 && P.OnGround && v < 30) { if (++stuck > 8) { jump = true; stuck = 0; } }
            else stuck = 0;
            if (hopCubes && P.OnGround && dir != 0)
                foreach (var c in W.Cubes)
                    if (c.Solid && !c.Held && MathF.Sign(c.Pos.X - P.Pos.X) == dir && MathF.Abs(c.Pos.X - P.Pos.X) < 52 && MathF.Abs(c.Pos.Y - P.Pos.Y) < 40)
                        jump = true;
            yield return In(dir, jump);
            t += Frame;
        }
        for (int i = 0; i < 20 && MathF.Abs(P.Vel.X) > 5 && P.OnGround; i++) yield return In();
    }

    /// <summary>Hold a direction until a condition holds.</summary>
    public IEnumerable<InputState> Move(int dir, Func<bool> until, float timeout = 8, Vector2? aimRel = null)
    {
        float t = 0;
        while (!until() && t < timeout && !P.Dead)
        {
            if (aimRel is { } ar) Aim = P.Pos + ar;
            yield return In(dir);
            t += Frame;
        }
    }

    /// <summary>Air-steer toward an x position until a condition holds (used while falling into a portal).</summary>
    public IEnumerable<InputState> Steer(float x, Func<bool> until, float timeout = 6)
    {
        float t = 0;
        while (!until() && t < timeout && !P.Dead)
        {
            float dx = x - P.Pos.X;
            int dir = MathF.Abs(dx) > 3 ? Math.Sign(dx) : 0;
            if (MathF.Abs(dx) < 30 && MathF.Sign(P.Vel.X) == Math.Sign(dx) && MathF.Abs(P.Vel.X) > 60) dir = -Math.Sign(dx);
            yield return In(dir);
            t += Frame;
        }
    }

    public IEnumerable<InputState> Until(Func<bool> cond, float timeout = 6, int mx = 0, Vector2? aimRel = null)
    {
        float t = 0;
        while (!cond() && t < timeout && !P.Dead)
        {
            if (aimRel is { } ar) Aim = P.Pos + ar;
            yield return In(mx);
            t += Frame;
        }
    }

    public IEnumerable<InputState> Do(Action a)
    {
        a();
        yield break;
    }

    /// <summary>Build a step only when it is reached, so it can read where the player has got to.</summary>
    public static IEnumerable<InputState> Lazy(Func<IEnumerable<InputState>> build)
    {
        foreach (var i in build()) yield return i;
    }

    public static IEnumerable<InputState> Seq(params IEnumerable<InputState>[] parts)
    {
        foreach (var p in parts)
            foreach (var i in p)
                yield return i;
    }

    public Player Player => P;
    public bool Teleported(Vector2 before, float dist = 120) => Vector2.Distance(P.Pos, before) > dist;
}
