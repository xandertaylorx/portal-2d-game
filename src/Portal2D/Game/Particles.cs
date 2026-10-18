using System.Numerics;
using Raylib_cs;
using Portal2D.Core;

namespace Portal2D.Game;

public enum PKind { Dot, Spark, Square, Ring }

public struct Particle
{
    public Vector2 Pos, Vel;
    public float Life, MaxLife, Size, Gravity, Drag;
    public Color Color;
    public PKind Kind;
    public bool Additive;
}

public struct ShotTrail
{
    public Vector2 A, B;
    public Color Color;
    public float Life;
}

public sealed class Particles
{
    public readonly List<Particle> Items = new(1024);
    public readonly List<ShotTrail> Trails = new();
    readonly Random rng = new(1234);

    public float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    public void Add(Particle p)
    {
        if (Items.Count < 3000) Items.Add(p);
    }

    public void Burst(Vector2 pos, Color color, int count, float speed, float life, float size,
                      float gravity = 0, PKind kind = PKind.Dot, bool additive = true, Vector2? bias = null, float spread = MathF.PI)
    {
        float baseAng = bias.HasValue ? MathF.Atan2(bias.Value.Y, bias.Value.X) : 0;
        for (int i = 0; i < count; i++)
        {
            float a = bias.HasValue ? baseAng + R(-spread, spread) : R(0, MathF.Tau);
            float s = R(speed * 0.3f, speed);
            Add(new Particle
            {
                Pos = pos, Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * s,
                Life = R(life * 0.6f, life), MaxLife = life, Size = R(size * 0.6f, size),
                Gravity = gravity, Drag = 2f, Color = color, Kind = kind, Additive = additive
            });
        }
    }

    public void Trail(Vector2 a, Vector2 b, Color c) => Trails.Add(new ShotTrail { A = a, B = b, Color = c, Life = 0.25f });

    public void Update(float dt)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var p = Items[i];
            p.Life -= dt;
            if (p.Life <= 0) { Items.RemoveAt(i); continue; }
            p.Vel.Y += p.Gravity * dt;
            p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
            p.Pos += p.Vel * dt;
            Items[i] = p;
        }
        for (int i = Trails.Count - 1; i >= 0; i--)
        {
            var t = Trails[i];
            t.Life -= dt;
            if (t.Life <= 0) Trails.RemoveAt(i); else Trails[i] = t;
        }
    }
}
