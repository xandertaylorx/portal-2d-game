using System.Numerics;
using Raylib_cs;
using Portal2D.Core;

namespace Portal2D.Game;

public enum Sfx
{
    PortalBlue, PortalOrange, PortalFail, Jump, Land, Teleport, ButtonDown, ButtonUp,
    DoorOpen, DoorClose, Pickup, Drop, Death, Win, Fizzle, CubeDissolve, CubeSpawn, ReceiverOn
}

public struct GameEvent
{
    public Sfx Sound;
    public Vector2 Pos;
    public float Shake;
}

public struct InputState
{
    public int MoveX;
    public bool JumpPressed, JumpHeld, UsePressed, FireBlue, FireOrange;
    public Vector2 AimWorld;
}

public struct RayHit
{
    public bool Hit;
    public float Dist;
    public Int2 Tile;
    public Vector2 Normal;
    public Vector2 Point;
}

public sealed class World
{
    public const float Gravity = 1900f;
    public const float JumpVel = 640f;
    public const float MaxWalk = 250f;
    public const float MaxFall = 1500f;
    public const float GelBounce = 1000f;
    public const float PlateVy = 1060f, PlateVx = 340f;
    public const float FastWalk = 520f;
    public const float ButtonHold = 4f;
    const int T = K.Tile;

    public readonly LevelDef Def;
    public readonly int W, H;
    public readonly char[,] Tiles;
    public Player? Player;
    public readonly List<Cube> Cubes = new();
    public readonly List<FloorButton> Buttons = new();
    public readonly List<Emitter> Emitters = new();
    public readonly List<Receiver> Receivers = new();
    public readonly Portal?[] Portals = new Portal?[2];
    public const int Groups = 6;
    public readonly float[] DoorOpen = new float[Groups];
    public readonly bool[] GroupActive = new bool[Groups];
    public readonly List<LaserSeg> Lasers = new();
    public readonly List<Vector2> LaserImpacts = new();
    public readonly List<GameEvent> Events = new();
    public readonly Particles Fx = new();
    public Int2 Exit = new(-10, -10);
    public bool HasExit;
    public float Time;
    public int PortalsPlaced;

    public bool Linked => Portals[0] != null && Portals[1] != null;
    public float WidthPx => W * T;
    public float HeightPx => H * T;

    public World(LevelDef def)
    {
        Def = def;
        H = def.Map.Length;
        W = def.Map.Max(r => r.Length);
        Tiles = new char[W, H];
        Int2? blueMarker = null, orangeMarker = null;

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            char c = x < def.Map[y].Length ? def.Map[y][x] : 'X';
            var standPos = new Vector2(x * T + T / 2f, (y + 1) * T);
            switch (c)
            {
                case 'P': Player = new Player(standPos - new Vector2(0, 15)); c = ' '; break;
                case 'C': Cubes.Add(new Cube(standPos - new Vector2(0, 14))); c = ' '; break;
                case 'M': Cubes.Add(new Cube(standPos - new Vector2(0, 14)) { Mirror = 1 }); c = ' '; break;
                case 'N': Cubes.Add(new Cube(standPos - new Vector2(0, 14)) { Mirror = -1 }); c = ' '; break;
                case 'E': Exit = new Int2(x, y); HasExit = true; c = ' '; break;
                case var bc when ButtonGroup(bc) >= 0: Buttons.Add(new FloorButton { X = x, Y = y, Group = ButtonGroup(bc), Timed = IsTimedButton(bc) }); c = ' '; break;
                case var rc when ReceiverGroup(rc) >= 0: Receivers.Add(new Receiver { X = x, Y = y, Group = ReceiverGroup(rc) }); break;
                case '>': Emitters.Add(new Emitter { X = x, Y = y, Dir = new Vector2(1, 0) }); break;
                case '<': Emitters.Add(new Emitter { X = x, Y = y, Dir = new Vector2(-1, 0) }); break;
                case '^': Emitters.Add(new Emitter { X = x, Y = y, Dir = new Vector2(0, -1) }); break;
                case 'v': Emitters.Add(new Emitter { X = x, Y = y, Dir = new Vector2(0, 1) }); break;
                case 'u': blueMarker = new Int2(x, y); c = ' '; break;
                case 'o': orangeMarker = new Int2(x, y); c = ' '; break;
            }
            Tiles[x, y] = c;
        }

        if (blueMarker is { } bm) Portals[0] = MakeFixed(PortalColor.Blue, bm, def.BlueDir);
        if (orangeMarker is { } om) Portals[1] = MakeFixed(PortalColor.Orange, om, def.OrangeDir);
        foreach (var p in Portals) if (p != null) p.Open = 1;

        UpdateGroups(0, initial: true);
    }

    Portal MakeFixed(PortalColor color, Int2 marker, Int2 dir)
    {
        var a = marker + dir;
        var normal = new Vector2(-dir.X, -dir.Y);
        var hitPoint = new Vector2(a.X * T + T / 2f, a.Y * T + T / 2f) + normal * (T / 2f);
        // marker marks the first (top / left) tile of the pair
        var p = BuildPortal(color, a, normal, hitPoint, isFixed: true, forcePair: true);
        return p ?? throw new InvalidOperationException($"Bad fixed portal in level {Def.Name}");
    }

    // ---------------------------------------------------------------- tiles

    public char At(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? 'X' : Tiles[x, y];

    // circuit groups 0-5: doors a-f, buttons 1 2 3 x y z, laser receivers 4 5 6 r s t
    public static bool IsDoor(char c) => c is >= 'a' and <= 'f';
    public static int ButtonGroup(char c) => c switch { '1' or 'T' => 0, '2' or 'U' => 1, '3' or 'V' => 2, 'x' or 'W' => 3, 'y' or 'Y' => 4, 'z' or 'Z' => 5, _ => -1 };
    /// <summary>Timed buttons ('T','U','V','W','Y','Z') stay down for a few seconds after they are pressed.</summary>
    public static bool IsTimedButton(char c) => c is 'T' or 'U' or 'V' or 'W' or 'Y' or 'Z';
    public static char TimedButtonChar(int g) => "TUVWYZ"[g];
    public static bool IsPlate(char c) => c is 'Q' or 'q';
    public static int ReceiverGroup(char c) => c switch { '4' => 0, '5' => 1, '6' => 2, 'r' => 3, 's' => 4, 't' => 5, _ => -1 };
    public static bool IsReceiver(char c) => ReceiverGroup(c) >= 0;
    public static char DoorChar(int g) => (char)('a' + g);
    public static char ButtonChar(int g) => "123xyz"[g];
    public static char ReceiverChar(int g) => "456rst"[g];

    public bool IsSolid(int x, int y)
    {
        char c = At(x, y);
        return c switch
        {
            '#' or 'X' or '=' or '<' or '>' or '^' or 'v' or 'J' or 'Q' or 'q' or 'G' => true,
            >= 'a' and <= 'f' => DoorOpen[c - 'a'] < 0.98f,
            _ when IsReceiver(c) => true,
            _ => false
        };
    }

    public bool BlocksLaser(int x, int y) => At(x, y) != '=' && IsSolid(x, y);
    public bool IsPortalable(int x, int y) => At(x, y) == '#';

    public RayHit CastGrid(Vector2 o, Vector2 d, float maxDist, Func<int, int, bool> blocks)
    {
        int x = (int)MathF.Floor(o.X / T), y = (int)MathF.Floor(o.Y / T);
        if (blocks(x, y)) return new RayHit { Hit = true, Dist = 0, Tile = new Int2(x, y), Normal = -d, Point = o };
        int sx = MathF.Sign(d.X), sy = MathF.Sign(d.Y);
        float tdx = sx != 0 ? T / MathF.Abs(d.X) : float.MaxValue;
        float tdy = sy != 0 ? T / MathF.Abs(d.Y) : float.MaxValue;
        float tmx = sx > 0 ? ((x + 1) * T - o.X) / d.X : sx < 0 ? (x * T - o.X) / d.X : float.MaxValue;
        float tmy = sy > 0 ? ((y + 1) * T - o.Y) / d.Y : sy < 0 ? (y * T - o.Y) / d.Y : float.MaxValue;
        for (int i = 0; i < 4096; i++)
        {
            float t; Vector2 n;
            if (tmx < tmy) { t = tmx; x += sx; tmx += tdx; n = new Vector2(-sx, 0); }
            else { t = tmy; y += sy; tmy += tdy; n = new Vector2(0, -sy); }
            if (t > maxDist) break;
            if (x < -1 || y < -1 || x > W || y > H) break;
            if (blocks(x, y)) return new RayHit { Hit = true, Dist = t, Tile = new Int2(x, y), Normal = n, Point = o + d * t };
        }
        return new RayHit { Hit = false, Dist = maxDist, Point = o + d * maxDist };
    }

    // ---------------------------------------------------------------- portals

    /// <summary>Find a valid two-tile placement around the hit tile. Returns null if none.</summary>
    public Portal? BuildPortal(PortalColor color, Int2 tile, Vector2 normal, Vector2 point, bool isFixed = false, bool forcePair = false)
    {
        bool wall = MathF.Abs(normal.X) > 0.5f;
        var step = wall ? new Int2(0, 1) : new Int2(1, 0);
        var nrm = new Int2((int)normal.X, (int)normal.Y);
        float frac = wall ? point.Y / T - tile.Y : point.X / T - tile.X;
        int[] offsets = forcePair ? [0] : frac < 0.5f ? [-1, 0] : [0, -1];

        foreach (int off in offsets)
        {
            var a = new Int2(tile.X + step.X * off, tile.Y + step.Y * off);
            var b = a + step;
            if (!PairTileOk(a, nrm, isFixed) || !PairTileOk(b, nrm, isFixed)) continue;
            var other = Portals[1 - (int)color];
            if (other != null && other.Normal == normal && (other.Covers(a.X, a.Y) || other.Covers(b.X, b.Y))) continue;

            Vector2 center = wall
                ? new Vector2(normal.X > 0 ? (a.X + 1) * T : a.X * T, b.Y * T)
                : new Vector2(b.X * T, normal.Y > 0 ? (a.Y + 1) * T : a.Y * T);
            return new Portal(color, center, normal, a, b, isFixed);
        }
        return null;
    }

    bool PairTileOk(Int2 t, Int2 n, bool isFixed)
    {
        if (t.X < 0 || t.Y < 0 || t.X >= W || t.Y >= H) return false;
        if (!isFixed && !IsPortalable(t.X, t.Y)) return false;
        var front = t + n;
        return !IsSolid(front.X, front.Y);
    }

    public bool GunAllows(PortalColor c) => Def.Gun switch
    {
        GunMode.Both => true,
        GunMode.BlueOnly => c == PortalColor.Blue,
        _ => false
    };

    /// <summary>Preview what a shot in this direction would do (used for the aim reticle).</summary>
    public (RayHit hit, Portal? candidate) PreviewShot(Vector2 origin, Vector2 dir, PortalColor color)
    {
        var hit = CastGrid(origin, dir, 3000, IsSolid);
        if (!hit.Hit || hit.Dist <= 0) return (hit, null);
        return (hit, BuildPortal(color, hit.Tile, hit.Normal, hit.Point));
    }

    void Shoot(Player p, PortalColor color)
    {
        var origin = GunOrigin(p);
        var (hit, cand) = PreviewShot(origin, p.Aim, color);
        var col = color == PortalColor.Blue ? Palette.Blue : Palette.Orange;
        Fx.Trail(origin + p.Aim * 18, hit.Point, col);
        p.ShootCooldown = 0.22f;
        p.LastShot = color;
        p.GunKick = 1;
        if (cand != null && !(Portals[(int)color]?.Fixed ?? false))
        {
            Portals[(int)color] = cand;
            PortalsPlaced++;
            Fx.Burst(cand.Center, col, 26, 260, 0.6f, 3.5f, 0, PKind.Spark, true, cand.Normal, 1.2f);
            Fx.Burst(cand.Center, Palette.White, 8, 120, 0.4f, 2.5f);
            Emit(color == PortalColor.Blue ? Sfx.PortalBlue : Sfx.PortalOrange, cand.Center);
        }
        else
        {
            Fx.Burst(hit.Point, MathX.Mix(col, Palette.White, 0.5f), 10, 160, 0.35f, 2.5f, 400, PKind.Spark, true, hit.Normal, 1.0f);
            Emit(Sfx.PortalFail, hit.Point);
        }
    }

    public static Vector2 GunOrigin(Player p) => p.Pos + new Vector2(0, -2);

    public Portal? PortalOnFace(Int2 tile, Vector2 normal)
    {
        foreach (var p in Portals)
            if (p != null && p.Normal == normal && p.Covers(tile.X, tile.Y)) return p;
        return null;
    }

    public void ClearPlayerPortals()
    {
        bool any = false;
        for (int i = 0; i < 2; i++)
        {
            var p = Portals[i];
            if (p != null && !p.Fixed)
            {
                Fx.Burst(p.Center, i == 0 ? Palette.Blue : Palette.Orange, 30, 200, 0.7f, 3f);
                Portals[i] = null;
                any = true;
            }
        }
        if (any && Player != null) Emit(Sfx.Fizzle, Player.Pos);
    }

    // ---------------------------------------------------------------- update

    public void Emit(Sfx s, Vector2 pos, float shake = 0) => Events.Add(new GameEvent { Sound = s, Pos = pos, Shake = shake });

    public void Update(float dt, InputState input)
    {
        Time += dt;
        foreach (var p in Portals)
            if (p != null) { p.Open = MathF.Min(1, p.Open + dt * 5); p.Age += dt; }

        if (Player != null) UpdatePlayer(Player, dt, input);
        foreach (var c in Cubes) UpdateCube(c, dt);

        ComputeLasers();
        UpdateGroups(dt, initial: false);
        UpdateHazards();

        if (Player != null && !Player.Dead) Unstick(Player);
        foreach (var c in Cubes) if (c.Solid && !c.Held) Unstick(c);

        AmbientFx(dt);
        Fx.Update(dt);
    }

    void UpdatePlayer(Player p, float dt, InputState input)
    {
        p.TeleportFlash = MathF.Max(0, p.TeleportFlash - dt * 3);
        p.GunKick = MathF.Max(0, p.GunKick - dt * 8);
        p.Squash = MathX.Approach(p.Squash, 0, dt * 2.5f);
        if (p.Dead) { p.DeadTimer += dt; return; }
        if (p.Won)
        {
            p.Vel.X = MathX.Approach(p.Vel.X, 0, 2000 * dt);
            p.Vel.Y = MathF.Min(p.Vel.Y + Gravity * dt, MaxFall);
            MoveBody(p, dt, true);
            return;
        }

        p.Aim = MathX.SafeNormalize(input.AimWorld - GunOrigin(p), new Vector2(p.Facing, 0));
        p.Facing = p.Aim.X >= 0 ? 1 : -1;
        p.ShootCooldown -= dt;

        // jumping with coyote time + buffering
        if (p.OnGround) p.Coyote = 0.1f; else p.Coyote -= dt;
        if (input.JumpPressed) p.JumpBuffer = 0.14f; else p.JumpBuffer -= dt;
        if (p.JumpBuffer > 0 && p.Coyote > 0)
        {
            p.Vel.Y = -JumpVel;
            p.Coyote = 0; p.JumpBuffer = 0; p.JumpCut = false;
            p.Squash = -0.22f;
            Emit(Sfx.Jump, p.Pos);
        }
        if (!input.JumpHeld && !p.JumpCut && p.Vel.Y < -200 && p.Vel.Y > -JumpVel - 1) { p.Vel.Y *= 0.5f; p.JumpCut = true; }

        int dir = input.MoveX;
        if (p.OnGround)
        {
            float top = TileUnder(p) == 'G' ? FastWalk : MaxWalk;
            if (dir != 0 && MathF.Abs(p.Vel.X) > top && MathF.Sign(p.Vel.X) == dir)
                p.Vel.X = MathX.Approach(p.Vel.X, dir * top, 1400 * dt);
            else
                p.Vel.X = MathX.Approach(p.Vel.X, dir * top, (dir == 0 ? 2600 : 2400) * dt);
        }
        else
        {
            if (dir != 0) { if (p.Vel.X * dir < MaxWalk) p.Vel.X = MathX.Approach(p.Vel.X, dir * MaxWalk, 1500 * dt); }
            else p.Vel.X = MathX.Approach(p.Vel.X, 0, 120 * dt);
        }
        p.Vel.Y = MathF.Min(p.Vel.Y + Gravity * dt, MaxFall);

        bool wasGround = p.OnGround;
        float prevVy = p.Vel.Y;
        MoveBody(p, dt, true);
        if (!wasGround && p.OnGround && prevVy > 350)
        {
            p.Squash = MathF.Min(0.35f, prevVy / 3000f);
            Emit(Sfx.Land, p.Pos + new Vector2(0, p.Half.Y), prevVy > 1000 ? 6 : 0);
            Fx.Burst(p.Pos + new Vector2(0, p.Half.Y), Palette.Dust, 8, 90, 0.4f, 3, 200, PKind.Dot, false, new Vector2(0, -1), 1.3f);
        }
        if (p.OnGround && MathF.Abs(p.Vel.X) > 10) p.WalkPhase += MathF.Abs(p.Vel.X) * dt * 0.045f;
        else if (p.OnGround) p.WalkPhase = MathX.Approach(p.WalkPhase % MathF.PI, 0, dt * 6);

        // pick up / drop
        if (input.UsePressed)
        {
            if (p.Holding != null) DropCube(p, true);
            else
            {
                Cube? best = null; float bestD = 80;
                foreach (var c in Cubes)
                {
                    if (!c.Solid) continue;
                    float d = Vector2.Distance(c.Pos, p.Pos);
                    if (d < bestD && !CastGrid(p.Pos, MathX.SafeNormalize(c.Pos - p.Pos, Vector2.UnitX), d, IsSolid).Hit)
                    { best = c; bestD = d; }
                }
                if (best != null) { p.Holding = best; best.Held = true; Emit(Sfx.Pickup, best.Pos); }
            }
        }

        if (p.ShootCooldown <= 0)
        {
            if (input.FireBlue && GunAllows(PortalColor.Blue)) Shoot(p, PortalColor.Blue);
            else if (input.FireOrange && GunAllows(PortalColor.Orange)) Shoot(p, PortalColor.Orange);
        }

        // exit
        if (HasExit && MathX.Overlaps(p.Rect, new Rectangle(Exit.X * T + 6, (Exit.Y - 1) * T, T - 12, T * 2)))
        {
            p.Won = true;
            DropCube(p, false);
            Emit(Sfx.Win, p.Pos);
            Fx.Burst(p.Pos, Palette.Green, 40, 220, 0.9f, 3f);
        }
    }

    void DropCube(Player p, bool toss)
    {
        var c = p.Holding;
        if (c == null) return;
        c.Held = false;
        if (toss) c.Vel = p.Vel * 0.6f + p.Aim * 110f;
        p.Holding = null;
        Emit(Sfx.Drop, c.Pos);
    }

    void UpdateCube(Cube c, float dt)
    {
        c.TeleportFlash = MathF.Max(0, c.TeleportFlash - dt * 3);
        if (c.Gone)
        {
            c.RespawnT -= dt;
            if (c.RespawnT <= 0)
            {
                c.Gone = false; c.Dissolving = false; c.DissolveT = 0;
                c.Pos = c.Spawn; c.Vel = Vector2.Zero;
                Fx.Burst(c.Pos, Palette.Blue, 20, 140, 0.5f, 2.5f);
                Emit(Sfx.CubeSpawn, c.Pos);
            }
            return;
        }
        if (c.Dissolving)
        {
            c.DissolveT += dt;
            c.Vel *= 0.9f;
            c.Pos += c.Vel * dt + new Vector2(0, -12 * dt);
            if (Fx.R(0, 1) < 0.6f) Fx.Add(new Particle
            {
                Pos = c.Pos + new Vector2(Fx.R(-14, 14), Fx.R(-14, 14)), Vel = new Vector2(Fx.R(-20, 20), Fx.R(-60, -20)),
                Life = 0.6f, MaxLife = 0.6f, Size = 2.5f, Color = Palette.White, Kind = PKind.Square, Additive = true
            });
            if (c.DissolveT > 0.8f) { c.Gone = true; c.RespawnT = 0.7f; }
            return;
        }

        if (c.Held)
        {
            var p = Player;
            if (p == null || p.Dead) { c.Held = false; if (p != null) p.Holding = null; return; }
            var target = p.Pos + new Vector2(0, -2) + p.Aim * 36f;
            var to = target - c.Pos;
            c.Vel = to * 60f;
            if (c.Vel.Length() > 2400) c.Vel = Vector2.Normalize(c.Vel) * 2400;
            MoveBody(c, dt, false);
            if (Vector2.Distance(c.Pos, target) > 90) DropCube(p, false);
            return;
        }

        c.Vel.Y = MathF.Min(c.Vel.Y + Gravity * dt, MaxFall);
        c.Vel.X = MathX.Approach(c.Vel.X, 0, (c.OnGround ? 1600 : 50) * dt);
        bool wasGround = c.OnGround;
        float prevVy = c.Vel.Y;
        MoveBody(c, dt, true);
        if (!wasGround && c.OnGround && prevVy > 400) Emit(Sfx.Land, c.Pos, 0);
    }

    // ---------------------------------------------------------------- physics

    /// <summary>The tile directly beneath a body's feet.</summary>
    public char TileUnder(Body b) => At((int)MathF.Floor(b.Pos.X / T), (int)MathF.Floor((b.Pos.Y + b.Half.Y + 2) / T));

    bool UsesPortals(Body b) => b is not Cube { Held: true };

    bool PassableFor(Body b, int x, int y)
    {
        if (!Linked || !UsesPortals(b)) return false;
        foreach (var p in Portals)
        {
            if (p == null || !p.Covers(x, y)) continue;
            float u = p.U(b.Pos), v = p.V(b.Pos);
            if (MathF.Abs(u) + b.ExtentAlong(p.Up) <= Portal.HalfLen + 1.5f && v > -6f) return true;
        }
        return false;
    }

    bool SolidFor(Body b, int x, int y) => IsSolid(x, y) && !PassableFor(b, x, y);

    IEnumerable<Cube> SolidBoxesFor(Body b)
    {
        foreach (var c in Cubes)
            if (c != b && c.Solid && !c.Held) yield return c;
    }

    public void MoveBody(Body b, float dt, bool canTeleport)
    {
        var delta = b.Vel * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) / 5f));
        b.OnGround = false;
        for (int i = 0; i < steps; i++)
        {
            var step = b.Vel * dt / steps;
            b.Pos.X += step.X; ResolveX(b, step.X);
            b.Pos.Y += step.Y; ResolveY(b, step.Y);
            if (canTeleport) TryTeleport(b);
        }
    }

    void TileRange(Rectangle r, out int x0, out int x1, out int y0, out int y1)
    {
        x0 = (int)MathF.Floor(r.X / T); x1 = (int)MathF.Floor((r.X + r.Width - 0.01f) / T);
        y0 = (int)MathF.Floor(r.Y / T); y1 = (int)MathF.Floor((r.Y + r.Height - 0.01f) / T);
    }

    void ResolveX(Body b, float dx)
    {
        if (dx == 0) return;
        TileRange(b.Rect, out int x0, out int x1, out int y0, out int y1);
        if (dx > 0)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (SolidFor(b, x, y)) { b.Pos.X = x * T - b.Half.X - 0.01f; if (b.Vel.X > 0) b.Vel.X = 0; goto boxes; }
        }
        else
        {
            for (int x = x1; x >= x0; x--)
                for (int y = y0; y <= y1; y++)
                    if (SolidFor(b, x, y)) { b.Pos.X = (x + 1) * T + b.Half.X + 0.01f; if (b.Vel.X < 0) b.Vel.X = 0; goto boxes; }
        }
        boxes:
        foreach (var c in SolidBoxesFor(b))
        {
            if (!MathX.Overlaps(b.Rect, c.Rect)) continue;
            if (b is Player pl && pl.Pos.Y + pl.Half.Y > c.Pos.Y - c.Half.Y + 6)
            {
                // push the cube
                float pen = dx > 0 ? (b.Pos.X + b.Half.X) - (c.Pos.X - c.Half.X) : (b.Pos.X - b.Half.X) - (c.Pos.X + c.Half.X);
                c.Pos.X += pen;
                ResolveX(c, pen);
                c.Vel.X = b.Vel.X * 0.8f;
            }
            if (!MathX.Overlaps(b.Rect, c.Rect)) continue;
            if (dx > 0) { b.Pos.X = c.Pos.X - c.Half.X - b.Half.X - 0.01f; if (b.Vel.X > 0) b.Vel.X = 0; }
            else { b.Pos.X = c.Pos.X + c.Half.X + b.Half.X + 0.01f; if (b.Vel.X < 0) b.Vel.X = 0; }
        }
    }

    void ResolveY(Body b, float dy)
    {
        if (dy == 0) return;
        TileRange(b.Rect, out int x0, out int x1, out int y0, out int y1);
        if (dy > 0)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (SolidFor(b, x, y))
                    {
                        b.Pos.Y = y * T - b.Half.Y - 0.01f;
                        if (At(x, y) == 'J' && b is not Cube { Held: true })
                        {
                            // repulsion gel: bounce back up, keeping (most of) the impact speed
                            float speed = MathF.Max(b.Vel.Y * 0.95f, GelBounce);
                            b.Vel.Y = -speed;
                            if (b is Player pl) pl.JumpCut = true;
                            Fx.Burst(new Vector2(b.Pos.X, y * T), Palette.Blue, 10, 160, 0.4f, 2.5f, 300, PKind.Dot, true, new Vector2(0, -1), 0.9f);
                            Emit(Sfx.Jump, b.Pos);
                            goto boxes;
                        }
                        if (IsPlate(At(x, y)) && b is not Cube { Held: true })
                        {
                            // launch plate: a fixed kick up and sideways, whatever the impact speed
                            b.Vel = new Vector2(At(x, y) == 'Q' ? PlateVx : -PlateVx, -PlateVy);
                            if (b is Player pl) pl.JumpCut = true;
                            Fx.Burst(new Vector2(b.Pos.X, y * T), Palette.Orange, 12, 200, 0.4f, 2.5f, 300, PKind.Dot, true, new Vector2(0, -1), 0.9f);
                            Emit(Sfx.Jump, b.Pos, 2);
                            goto boxes;
                        }
                        if (b.Vel.Y > 0) b.Vel.Y = 0;
                        b.OnGround = true;
                        goto boxes;
                    }
        }
        else
        {
            for (int y = y1; y >= y0; y--)
                for (int x = x0; x <= x1; x++)
                    if (SolidFor(b, x, y)) { b.Pos.Y = (y + 1) * T + b.Half.Y + 0.01f; if (b.Vel.Y < 0) b.Vel.Y = 0; goto boxes; }
        }
        boxes:
        foreach (var c in SolidBoxesFor(b))
        {
            if (!MathX.Overlaps(b.Rect, c.Rect)) continue;
            if (dy > 0) { b.Pos.Y = c.Pos.Y - c.Half.Y - b.Half.Y - 0.01f; if (b.Vel.Y > 0) b.Vel.Y = 0; b.OnGround = true; }
            else { b.Pos.Y = c.Pos.Y + c.Half.Y + b.Half.Y + 0.01f; if (b.Vel.Y < 0) b.Vel.Y = 0; }
        }
    }

    bool TryTeleport(Body b)
    {
        if (!Linked || !UsesPortals(b)) return false;
        for (int i = 0; i < 2; i++)
        {
            var p = Portals[i]!;
            var q = Portals[1 - i]!;
            float v = p.V(b.Pos);
            if (v >= 0 || v < -24) continue;
            if (MathF.Abs(p.U(b.Pos)) + b.ExtentAlong(p.Up) > Portal.HalfLen + 1.5f) continue;

            var np = Portal.MapPoint(p, q, b.Pos);
            var nv = Portal.MapDir(p, q, b.Vel);
            float vout = q.V(np);
            if (vout < 0.5f) np += q.Normal * (0.5f - vout);
            float minOut = q.Normal.Y < -0.5f ? 330f : 160f;
            float along = Vector2.Dot(nv, q.Normal);
            if (along < minOut) nv += q.Normal * (minOut - along);
            float speed = nv.Length();
            if (speed > 1800) nv *= 1800 / speed;

            b.Pos = np;
            b.Vel = nv;
            b.TeleportFlash = 1;
            b.Teleports++;
            b.OnGround = false;
            var colA = i == 0 ? Palette.Blue : Palette.Orange;
            var colB = i == 0 ? Palette.Orange : Palette.Blue;
            Fx.Burst(p.Center, colA, 12, 150, 0.4f, 2.5f, 0, PKind.Spark, true, -p.Normal, 1.0f);
            Fx.Burst(q.Center, colB, 16, 220, 0.5f, 2.5f, 0, PKind.Spark, true, q.Normal, 1.0f);
            if (b is Player pl)
            {
                pl.JumpCut = true;
                Emit(Sfx.Teleport, q.Center);
                if (pl.Holding is { } held)
                {
                    held.Pos = np + q.Normal * 18;
                    held.Vel = nv;
                    held.TeleportFlash = 1;
                }
            }
            else Emit(Sfx.Teleport, q.Center);
            return true;
        }
        return false;
    }

    bool OverlapsSolid(Body b, Vector2 pos)
    {
        var r = new Rectangle(pos.X - b.Half.X, pos.Y - b.Half.Y, b.Half.X * 2, b.Half.Y * 2);
        TileRange(r, out int x0, out int x1, out int y0, out int y1);
        var saved = b.Pos;
        b.Pos = pos;
        bool hit = false;
        for (int y = y0; y <= y1 && !hit; y++)
            for (int x = x0; x <= x1 && !hit; x++)
                if (SolidFor(b, x, y)) hit = true;
        b.Pos = saved;
        return hit;
    }

    void Unstick(Body b)
    {
        if (!OverlapsSolid(b, b.Pos)) return;
        Span<Vector2> dirs = [new(0, -1), new(0, 1), new(-1, 0), new(1, 0), new(-1, -1), new(1, -1)];
        for (int d = 2; d <= 64; d += 2)
            foreach (var dir in dirs)
                if (!OverlapsSolid(b, b.Pos + dir * d)) { b.Pos += dir * d; return; }
    }

    // ---------------------------------------------------------------- lasers, groups, hazards

    public void ComputeLasers()
    {
        Lasers.Clear();
        LaserImpacts.Clear();
        foreach (var r in Receivers) r.Active = false;
        bool hitPlayer = false;

        foreach (var e in Emitters)
        {
            var origin = new Vector2(e.X * T + T / 2f, e.Y * T + T / 2f) + e.Dir * (T / 2f + 0.05f);
            var dir = e.Dir;
            for (int bounce = 0; bounce < 10; bounce++)
            {
                var hit = CastGrid(origin, dir, 5000, BlocksLaser);
                float dist = hit.Dist;
                int kind = 0; // 0 tile/none, 1 cube, 2 player
                Cube? hitCube = null;
                foreach (var c in Cubes)
                {
                    if (!c.Solid) continue;
                    float t = MathX.RayAabb(origin, dir, c.Rect);
                    if (t >= 0 && t < dist) { dist = t; kind = 1; hitCube = c; }
                }
                if (Player is { Dead: false, Won: false } pl)
                {
                    var r = pl.Rect;
                    r = new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
                    float t = MathX.RayAabb(origin, dir, r);
                    if (t >= 0 && t < dist) { dist = t; kind = 2; }
                }
                var end = origin + dir * dist;
                Lasers.Add(new LaserSeg { A = origin, B = end });
                if (kind == 2) { hitPlayer = true; LaserImpacts.Add(end); break; }
                if (kind == 1 && hitCube is { Mirror: not 0 } mirror)
                {
                    // mirror cube: '/' turns a beam (dx,dy) into (-dy,-dx), '\' into (dy,dx)
                    dir = mirror.Mirror > 0 ? new Vector2(-dir.Y, -dir.X) : new Vector2(dir.Y, dir.X);
                    origin = mirror.Pos + dir * (mirror.Half.X + 0.6f);
                    LaserImpacts.Add(end);
                    continue;
                }
                if (kind == 1 || !hit.Hit) { LaserImpacts.Add(end); break; }

                var p = Linked ? PortalOnFace(hit.Tile, hit.Normal) : null;
                if (p != null)
                {
                    var q = Portals[1 - (int)p.Color]!;
                    dir = Portal.MapDir(p, q, dir);
                    origin = Portal.MapPoint(p, q, end) + dir * 0.05f;
                    continue;
                }
                foreach (var r in Receivers)
                    if (r.X == hit.Tile.X && r.Y == hit.Tile.Y) r.Active = true;
                LaserImpacts.Add(end);
                break;
            }
        }

        if (hitPlayer && Player != null) Kill(Player, Palette.LaserRed);
    }

    void UpdateGroups(float dt, bool initial)
    {
        foreach (var b in Buttons)
        {
            bool pressed = false;
            var trig = b.Trigger;
            if (Player is { Dead: false } pl && MathX.Overlaps(pl.Rect, trig)) pressed = true;
            foreach (var c in Cubes)
                if (c.Solid && !c.Held && MathX.Overlaps(c.Rect, trig)) { pressed = true; c.OnButtonGlow = 1; }
            if (b.Timed)
            {
                if (pressed) b.HoldT = ButtonHold; else b.HoldT = MathF.Max(0, b.HoldT - dt);
                pressed = b.HoldT > 0;
            }
            if (pressed != b.Pressed && !initial) Emit(pressed ? Sfx.ButtonDown : Sfx.ButtonUp, new Vector2(b.X * T + 16, b.Y * T + 28));
            b.Pressed = pressed;
            b.Press = MathX.Approach(b.Press, pressed ? 1 : 0, dt * 10);
        }
        foreach (var c in Cubes) c.OnButtonGlow = MathF.Max(0, c.OnButtonGlow - dt * 2);
        foreach (var r in Receivers)
        {
            if (r.Active && r.Glow < 0.05f && !initial) Emit(Sfx.ReceiverOn, new Vector2(r.X * T + 16, r.Y * T + 16));
            r.Glow = MathX.Approach(r.Glow, r.Active ? 1 : 0, dt * 4);
        }

        for (int g = 0; g < Groups; g++)
        {
            int total = 0, on = 0;
            foreach (var b in Buttons) if (b.Group == g) { total++; if (b.Pressed) on++; }
            foreach (var r in Receivers) if (r.Group == g) { total++; if (r.Active) on++; }
            bool active = total > 0 && on == total;
            if (active != GroupActive[g] && !initial) Emit(active ? Sfx.DoorOpen : Sfx.DoorClose, DoorCenter(g));
            GroupActive[g] = active;

            if (active) DoorOpen[g] = MathF.Min(1, DoorOpen[g] + dt * 2.5f);
            else if (!DoorBlocked(g)) DoorOpen[g] = MathF.Max(0, DoorOpen[g] - dt * 2.5f);
        }
    }

    Vector2 DoorCenter(int g)
    {
        Vector2 sum = Vector2.Zero; int n = 0;
        char dc = (char)('a' + g);
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            if (Tiles[x, y] == dc) { sum += new Vector2(x * T + 16, y * T + 16); n++; }
        return n > 0 ? sum / n : Player?.Pos ?? Vector2.Zero;
    }

    bool DoorBlocked(int g)
    {
        char dc = (char)('a' + g);
        IEnumerable<Body> bodies = Cubes.Where(c => c.Solid);
        if (Player is { Dead: false } p) bodies = bodies.Append(p);
        foreach (var b in bodies)
        {
            TileRange(b.Rect, out int x0, out int x1, out int y0, out int y1);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (At(x, y) == dc) return true;
        }
        return false;
    }

    void UpdateHazards()
    {
        if (Player is { Dead: false, Won: false } p)
        {
            var r = p.Rect;
            if (TouchesTile(new Rectangle(r.X + 3, r.Y, r.Width - 6, r.Height - 8), '~')) Kill(p, Palette.Goo);
            else if (TouchesTile(r, '|'))
            {
                ClearPlayerPortals();
                if (p.Holding is { } held) { p.Holding = null; held.Held = false; Dissolve(held); }
            }
        }
        foreach (var c in Cubes)
        {
            if (!c.Solid) continue;
            var r = c.Rect;
            if (TouchesTile(new Rectangle(r.X + 2, r.Y, r.Width - 4, r.Height - 6), '~') || TouchesTile(r, '|'))
            {
                if (c.Held && Player != null) { Player.Holding = null; c.Held = false; }
                Dissolve(c);
            }
        }
    }

    void Dissolve(Cube c)
    {
        if (c.Dissolving) return;
        c.Dissolving = true;
        c.DissolveT = 0;
        Emit(Sfx.CubeDissolve, c.Pos);
    }

    bool TouchesTile(Rectangle r, char ch)
    {
        TileRange(r, out int x0, out int x1, out int y0, out int y1);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            if (At(x, y) == ch) return true;
        return false;
    }

    public void Kill(Player p, Color color)
    {
        if (p.Dead || p.Won) return;
        p.Dead = true;
        p.DeadTimer = 0;
        if (p.Holding is { } c) { c.Held = false; p.Holding = null; }
        Fx.Burst(p.Pos, color, 50, 320, 0.9f, 4f, 600, PKind.Square, false);
        Fx.Burst(p.Pos, Palette.White, 20, 200, 0.5f, 3f);
        Emit(Sfx.Death, p.Pos, 10);
    }

    void AmbientFx(float dt)
    {
        // portal sparkles
        foreach (var p in Portals)
        {
            if (p == null || Fx.R(0, 1) > 0.5f) continue;
            var col = p.Color == PortalColor.Blue ? Palette.Blue : Palette.Orange;
            float u = Fx.R(-1, 1) * Portal.HalfLen;
            Fx.Add(new Particle
            {
                Pos = p.Center + p.Up * u + p.Normal * Fx.R(0, 4), Vel = p.Normal * Fx.R(15, 50) + p.Up * Fx.R(-10, 10),
                Life = 0.8f, MaxLife = 0.8f, Size = Fx.R(1, 2.2f), Color = col, Kind = PKind.Dot, Additive = true, Drag = 1
            });
        }
        // goo bubbles + fizzler sparkles
        if (Fx.R(0, 1) < 0.35f)
        {
            int x = (int)Fx.R(0, W), y = (int)Fx.R(0, H);
            char c = At(x, y);
            if (c == '~' && At(x, y - 1) != '~')
                Fx.Add(new Particle
                {
                    Pos = new Vector2(x * T + Fx.R(4, 28), y * T + 4), Vel = new Vector2(0, -Fx.R(10, 30)),
                    Life = 0.7f, MaxLife = 0.7f, Size = Fx.R(1.5f, 3.5f), Color = Palette.GooLight, Kind = PKind.Ring
                });
        }
        for (int i = 0; i < 3; i++)
        {
            int x = (int)Fx.R(0, W), y = (int)Fx.R(0, H);
            if (At(x, y) == '|')
                Fx.Add(new Particle
                {
                    Pos = new Vector2(x * T + Fx.R(8, 24), y * T + Fx.R(0, 32)), Vel = new Vector2(Fx.R(-8, 8), Fx.R(-30, 30)),
                    Life = 0.5f, MaxLife = 0.5f, Size = Fx.R(1, 2), Color = Palette.Fizzler, Kind = PKind.Dot, Additive = true
                });
        }
        foreach (var imp in LaserImpacts)
            if (Fx.R(0, 1) < 0.4f)
                Fx.Burst(imp, Palette.LaserRed, 1, 120, 0.3f, 2f, 300, PKind.Spark);
    }
}
