using System.Numerics;
using Raylib_cs;
using Portal2D.Core;

namespace Portal2D.Game;

public abstract class Body
{
    public Vector2 Pos, Vel, Half;
    public bool OnGround;
    public float TeleportFlash;
    public int Teleports;

    public Rectangle Rect => new(Pos.X - Half.X, Pos.Y - Half.Y, Half.X * 2, Half.Y * 2);
    public float ExtentAlong(Vector2 axis) => MathF.Abs(axis.X) * Half.X + MathF.Abs(axis.Y) * Half.Y;
}

public sealed class Player : Body
{
    public bool Dead;
    public float DeadTimer;
    public int Facing = 1;
    public float WalkPhase;
    public float Coyote, JumpBuffer;
    public bool JumpCut;
    public Cube? Holding;
    public Vector2 Aim = new(1, 0);
    public float ShootCooldown;
    public PortalColor LastShot = PortalColor.Blue;
    public float GunKick;
    public float Squash;
    public bool Won;

    public Player(Vector2 pos)
    {
        Pos = pos;
        Half = new Vector2(11, 22);
    }
}

public sealed class Cube : Body
{
    public Vector2 Spawn;
    public bool Held;
    public bool Dissolving;
    public float DissolveT;
    public float RespawnT;
    public bool Gone;
    public float OnButtonGlow;

    public Cube(Vector2 spawn)
    {
        Spawn = spawn;
        Pos = spawn;
        Half = new Vector2(14, 14);
    }

    public bool Solid => !Gone && !Dissolving;
}

public sealed class FloorButton
{
    public int X, Y, Group;
    public bool Pressed;
    public float Press; // animated 0..1
    public Rectangle Trigger => new(X * K.Tile - 10, Y * K.Tile + K.Tile - 12, K.Tile + 20, 12);
}

public sealed class Emitter
{
    public int X, Y;
    public Vector2 Dir;
}

public sealed class Receiver
{
    public int X, Y, Group;
    public bool Active;
    public float Glow;
}

public struct LaserSeg
{
    public Vector2 A, B;
}
