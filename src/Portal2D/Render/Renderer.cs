using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using Portal2D.Game;
using static Portal2D.Core.MathX;

namespace Portal2D.Render;

/// <summary>
/// Everything in the game is drawn procedurally - no sprite sheets.
///
/// The portal "window" effect works like this:
///  1. For each portal X (paired with Y) the whole world is rendered into an off-screen texture,
///     but every vertex is first pushed through the portal transform Y -> X. That makes the
///     space in front of Y appear *behind* X, exactly where you would see it if X were a hole.
///  2. On screen we compute the visibility cone from the player's eye through X's opening
///     (the two portal end points projected away from the eye).
///  3. That cone is drawn as a textured polygon sampling the off-screen texture with screen-space
///     UVs, fading out with distance. Everything outside the cone stays the real world.
/// </summary>
public sealed class Renderer
{
    const int T = K.Tile;
    readonly RenderTexture2D[] coneRt = new RenderTexture2D[2];
    World? lampWorld;
    readonly List<Vector2> lamps = new();
    readonly Vector2[] ringBuf = new Vector2[200];

    public bool ConesEnabled = true;
    public float Time;

    public Renderer()
    {
        for (int i = 0; i < 2; i++)
        {
            coneRt[i] = Raylib.LoadRenderTexture(K.CanvasW, K.CanvasH);
            Raylib.SetTextureFilter(coneRt[i].Texture, TextureFilter.Bilinear);
        }
    }

    public void Unload()
    {
        foreach (var rt in coneRt) Raylib.UnloadRenderTexture(rt);
    }

    // ================================================================ frame entry points

    public void PrepareCones(World w, Camera2D cam)
    {
        if (!ConesEnabled || !w.Linked) return;
        var screen = ViewRect(cam);
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 2; i++)
        {
            var x = w.Portals[i]!;
            var y = w.Portals[1 - i]!;
            // what part of the world ends up on screen once mapped Y -> X: map the screen back X -> Y
            var view = MappedBounds(x, y, screen);

            Raylib.BeginTextureMode(coneRt[i]);
            Raylib.ClearBackground(Palette.BackWall);
            Raylib.BeginMode2D(cam);
            Rlgl.PushMatrix();
            Portal.Matrix(y, x, m);
            unsafe { fixed (float* p = m) Rlgl.MultMatrixf(p); }
            DrawWorld(w, view, drawPortals: true);
            Rlgl.PopMatrix();
            Raylib.EndMode2D();
            Raylib.EndTextureMode();
        }
    }

    public void DrawScene(World w, Camera2D cam, Vector2 eye)
    {
        var view = ViewRect(cam);
        DrawWorld(w, view, drawPortals: false);
        if (ConesEnabled && w.Linked)
            for (int i = 0; i < 2; i++)
                DrawCone(w.Portals[i]!, eye, cam, coneRt[i].Texture);
        foreach (var p in w.Portals)
            if (p != null) DrawPortalRim(p, w.Linked);
    }

    public static Rectangle ViewRect(Camera2D cam)
    {
        var tl = Raylib.GetScreenToWorld2D(Vector2.Zero, cam);
        var br = Raylib.GetScreenToWorld2D(new Vector2(K.CanvasW, K.CanvasH), cam);
        return new Rectangle(tl.X - T, tl.Y - T, br.X - tl.X + 2 * T, br.Y - tl.Y + 2 * T);
    }

    static Rectangle MappedBounds(Portal from, Portal to, Rectangle r)
    {
        Span<Vector2> pts = [new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X, r.Y + r.Height), new(r.X + r.Width, r.Y + r.Height)];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            var q = Portal.MapPoint(from, to, p);
            minX = MathF.Min(minX, q.X); minY = MathF.Min(minY, q.Y);
            maxX = MathF.Max(maxX, q.X); maxY = MathF.Max(maxY, q.Y);
        }
        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    // ================================================================ the world

    public void DrawWorld(World w, Rectangle view, bool drawPortals)
    {
        int x0 = Math.Max(0, (int)(view.X / T)), y0 = Math.Max(0, (int)(view.Y / T));
        int x1 = Math.Min(w.W - 1, (int)((view.X + view.Width) / T)), y1 = Math.Min(w.H - 1, (int)((view.Y + view.Height) / T));

        DrawBackground(w, view, x0, y0, x1, y1);
        if (w.HasExit) DrawExit(w);
        foreach (var b in w.Buttons) DrawButton(b);

        foreach (var c in w.Cubes) { DrawWithClones(w, c, () => DrawCube(c)); }
        if (w.Player is { } pl) DrawWithClones(w, pl, () => DrawPlayer(pl));

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                DrawTile(w, x, y);

        DrawLasers(w);
        DrawParticles(w);
        if (drawPortals)
            foreach (var p in w.Portals)
                if (p != null) DrawPortalRim(p, w.Linked);
    }

    /// <summary>Draws a body, plus a copy of it emerging from the partner portal while it is mid-transit.</summary>
    void DrawWithClones(World w, Body b, Action draw)
    {
        draw();
        if (!w.Linked) return;
        if (b is Cube { Held: true }) return;
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 2; i++)
        {
            var p = w.Portals[i]!;
            float u = p.U(b.Pos), v = p.V(b.Pos);
            float extN = b.ExtentAlong(p.Normal), extT = b.ExtentAlong(p.Up);
            if (MathF.Abs(u) > Portal.HalfLen + extT || v > extN + 1 || v < -extN - 1) continue;
            Rlgl.PushMatrix();
            Portal.Matrix(p, w.Portals[1 - i]!, m);
            unsafe { fixed (float* f = m) Rlgl.MultMatrixf(f); }
            draw();
            Rlgl.PopMatrix();
        }
    }

    void EnsureLamps(World w)
    {
        if (lampWorld == w) return;
        lampWorld = w;
        lamps.Clear();
        for (int x = 3; x < w.W - 3; x += 7)
        {
            for (int y = 1; y < w.H - 4; y++)
            {
                if (!w.IsSolid(x, y) && w.IsSolid(x, y - 1) && !w.IsSolid(x, y + 1) && !w.IsSolid(x, y + 2) && w.At(x, y) != '|')
                {
                    lamps.Add(new Vector2(x * T + T / 2f, y * T));
                    break;
                }
            }
        }
    }

    void DrawBackground(World w, Rectangle view, int x0, int y0, int x1, int y1)
    {
        Raylib.DrawRectangleRec(view, Palette.BackWall);
        const int P = 128;
        int px0 = (int)MathF.Floor(view.X / P), px1 = (int)MathF.Floor((view.X + view.Width) / P);
        int py0 = (int)MathF.Floor(view.Y / P), py1 = (int)MathF.Floor((view.Y + view.Height) / P);
        for (int py = py0; py <= py1; py++)
            for (int px = px0; px <= px1; px++)
            {
                float h = Hash(px, py, 7);
                var col = Mix(Palette.BackWall, Palette.BackWall2, h);
                Raylib.DrawRectangle(px * P + 2, py * P + 2, P - 4, P - 4, col);
                Raylib.DrawRectangle(px * P + 2, py * P + 2, P - 4, 2, C(255, 255, 255, 8));
                if (h > 0.86f)
                {
                    // lit info panel
                    Raylib.DrawRectangle(px * P + 22, py * P + 30, 84, 6, C(90, 160, 210, 40));
                    Raylib.DrawRectangle(px * P + 22, py * P + 42, 60, 4, C(90, 160, 210, 25));
                    Raylib.DrawRectangle(px * P + 22, py * P + 52, 70, 4, C(90, 160, 210, 25));
                }
                else if (h < 0.08f)
                {
                    for (int k = 0; k < 5; k++)
                        Raylib.DrawRectangle(px * P + 16 + k * 20, py * P + 100, 12, 4, C(0, 0, 0, 40));
                }
            }
        // seams
        for (int px = px0; px <= px1 + 1; px++) Raylib.DrawRectangle(px * P - 2, (int)view.Y, 4, (int)view.Height, Palette.BackSeam);
        for (int py = py0; py <= py1 + 1; py++) Raylib.DrawRectangle((int)view.X, py * P - 2, (int)view.Width, 4, Palette.BackSeam);

        // lamps with soft light shafts
        EnsureLamps(w);
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var l in lamps)
        {
            if (l.X < view.X - 300 || l.X > view.X + view.Width + 300) continue;
            float flick = 0.85f + 0.15f * MathF.Sin(Time * 1.3f + l.X);
            GradientQuad(l + new Vector2(-14, 4), l + new Vector2(14, 4), l + new Vector2(140, 330), l + new Vector2(-140, 330),
                C(255, 245, 225, (int)(26 * flick)), C(255, 245, 225, 0));
            Raylib.DrawCircleGradient(l + new Vector2(0, 6), 46, C(255, 240, 210, 40), C(255, 240, 210, 0));
        }
        Raylib.EndBlendMode();
        foreach (var l in lamps)
        {
            Raylib.DrawRectangleRounded(new Rectangle(l.X - 18, l.Y, 36, 6), 0.5f, 4, C(70, 78, 88));
            Raylib.DrawRectangle((int)l.X - 14, (int)l.Y + 5, 28, 2, C(255, 248, 230));
        }

        // drifting dust
        for (int i = 0; i < 70; i++)
        {
            float hx = Hash(i, 1, 3), hy = Hash(i, 2, 3);
            float x = hx * w.WidthPx + MathF.Sin(Time * 0.3f + i) * 30;
            float y = (hy * w.HeightPx - Time * (6 + hx * 10)) % w.HeightPx;
            if (y < 0) y += w.HeightPx;
            Raylib.DrawCircleV(new Vector2(x, y), 1 + hx, C(220, 230, 240, (int)(30 + 30 * hy)));
        }
    }

    void GradientQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color near, Color far)
    {
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(near.R, near.G, near.B, near.A); Rlgl.Vertex2f(a.X, a.Y);
        Rlgl.Color4ub(far.R, far.G, far.B, far.A); Rlgl.Vertex2f(d.X, d.Y);
        Rlgl.Color4ub(far.R, far.G, far.B, far.A); Rlgl.Vertex2f(c.X, c.Y);
        Rlgl.Color4ub(near.R, near.G, near.B, near.A); Rlgl.Vertex2f(b.X, b.Y);
        Rlgl.End();
    }

    // ================================================================ tiles

    static bool IsBlock(char c) => c is '#' or 'X' or '<' or '>' or '^' or 'v' or '4' or '5' or '6';

    void DrawTile(World w, int x, int y)
    {
        char c = w.Tiles[x, y];
        int px = x * T, py = y * T;
        switch (c)
        {
            case '#': DrawPanel(w, x, y, px, py); break;
            case 'X': DrawMetal(w, x, y, px, py); break;
            case '=': DrawGlass(w, x, y, px, py); break;
            case '<' or '>' or '^' or 'v': DrawMetal(w, x, y, px, py); DrawEmitter(w, x, y, c); break;
            case '4' or '5' or '6': DrawMetal(w, x, y, px, py); DrawReceiver(w, x, y); break;
            case 'a' or 'b' or 'c': DrawDoor(w, x, y, c - 'a'); break;
            case '~': DrawGoo(w, x, y, px, py); break;
            case '|': DrawFizzler(w, x, y, px, py); break;
        }
    }

    bool Open(World w, int x, int y) => !IsBlock(w.At(x, y)) && w.At(x, y) != '=';

    void DrawPanel(World w, int x, int y, int px, int py)
    {
        float h = Hash(x, y, 11);
        Raylib.DrawRectangle(px, py, T, T, Mix(Palette.Panel, C(206, 213, 219), h * 0.6f));
        if (x % 2 == 0) Raylib.DrawRectangle(px, py, 1, T, Palette.PanelSeam);
        if (y % 2 == 0) Raylib.DrawRectangle(px, py, T, 1, Palette.PanelSeam);
        if (h > 0.9f) Raylib.DrawRectangle(px + 10, py + 12, 12, 2, C(180, 188, 196));
        bool up = Open(w, x, y - 1), dn = Open(w, x, y + 1), lf = Open(w, x - 1, y), rt = Open(w, x + 1, y);
        if (up) { Raylib.DrawRectangle(px, py, T, 3, C(255, 255, 255)); Raylib.DrawRectangle(px, py + 3, T, 1, C(170, 180, 190)); }
        if (dn) Raylib.DrawRectangle(px, py + T - 3, T, 3, C(150, 160, 170));
        if (lf) Raylib.DrawRectangle(px, py, 2, T, Palette.PanelEdge);
        if (rt) Raylib.DrawRectangle(px + T - 2, py, 2, T, Palette.PanelEdge);
    }

    void DrawMetal(World w, int x, int y, int px, int py)
    {
        bool up = Open(w, x, y - 1), dn = Open(w, x, y + 1), lf = Open(w, x - 1, y), rt = Open(w, x + 1, y);
        bool interior = !up && !dn && !lf && !rt && !Open(w, x - 1, y - 1) && !Open(w, x + 1, y - 1) && !Open(w, x - 1, y + 1) && !Open(w, x + 1, y + 1);
        if (interior)
        {
            Raylib.DrawRectangle(px, py, T, T, C(28, 32, 38));
            if ((x + y) % 2 == 0) Raylib.DrawRectangle(px + 4, py + 4, T - 8, T - 8, C(32, 36, 43));
            return;
        }
        Raylib.DrawRectangle(px, py, T, T, Palette.Metal);
        Raylib.DrawRectangle(px + 3, py + 3, T - 6, T - 6, Palette.MetalInner);
        float h = Hash(x, y, 5);
        if (h < 0.3f)
            for (int k = 0; k < 3; k++) Raylib.DrawRectangle(px + 8, py + 9 + k * 6, 16, 2, C(52, 58, 66));
        Raylib.DrawCircle(px + 6, py + 6, 1.6f, Palette.MetalLight);
        Raylib.DrawCircle(px + T - 6, py + T - 6, 1.6f, C(44, 50, 58));
        if (up) { Raylib.DrawRectangle(px, py, T, 3, Palette.MetalLight); }
        if (dn) Raylib.DrawRectangle(px, py + T - 3, T, 3, Palette.MetalEdge);
        if (lf) Raylib.DrawRectangle(px, py, 2, T, Palette.MetalEdge);
        if (rt) Raylib.DrawRectangle(px + T - 2, py, 2, T, Palette.MetalEdge);
    }

    void DrawGlass(World w, int x, int y, int px, int py)
    {
        Raylib.DrawRectangle(px, py, T, T, C(150, 210, 255, 34));
        var edge = C(205, 236, 255, 150);
        if (w.At(x, y - 1) != '=') Raylib.DrawRectangle(px, py, T, 2, edge);
        if (w.At(x, y + 1) != '=') Raylib.DrawRectangle(px, py + T - 2, T, 2, edge);
        if (w.At(x - 1, y) != '=') Raylib.DrawRectangle(px, py, 2, T, edge);
        if (w.At(x + 1, y) != '=') Raylib.DrawRectangle(px + T - 2, py, 2, T, edge);
        if ((x * 7 + y * 3) % 5 == 0)
            Raylib.DrawLineEx(new Vector2(px + 6, py + 26), new Vector2(px + 20, py + 6), 2, C(255, 255, 255, 60));
    }

    void DrawEmitter(World w, int x, int y, char c)
    {
        var e = w.Emitters.First(em => em.X == x && em.Y == y);
        var center = new Vector2(x * T + 16, y * T + 16) + e.Dir * 13;
        Raylib.DrawCircleV(center, 9, C(30, 30, 34));
        Raylib.DrawCircleV(center, 6, C(200, 30, 30));
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(center, 18 + MathF.Sin(Time * 20) * 1.5f, C(255, 60, 60, 160), C(255, 40, 40, 0));
        Raylib.EndBlendMode();
        Raylib.DrawCircleV(center, 2.5f, C(255, 220, 220));
    }

    void DrawReceiver(World w, int x, int y)
    {
        var r = w.Receivers.First(rc => rc.X == x && rc.Y == y);
        var center = new Vector2(x * T + 16, y * T + 16);
        Raylib.DrawCircleV(center, 12, C(26, 28, 32));
        Raylib.DrawRing(center, 9, 12, 0, 360, 24, Mix(C(120, 60, 50), Palette.Orange, r.Glow));
        Raylib.DrawCircleV(center, 7, Mix(C(70, 30, 30), C(255, 120, 90), r.Glow));
        if (r.Glow > 0.01f)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(center, 30, C(255, 120, 60, (int)(150 * r.Glow)), C(255, 80, 40, 0));
            Raylib.EndBlendMode();
        }
    }

    void DrawDoor(World w, int x, int y, int g)
    {
        int px = x * T, py = y * T;
        float o = w.DoorOpen[g];
        char dc = (char)('a' + g);
        bool top = w.At(x, y - 1) != dc, bottom = w.At(x, y + 1) != dc;
        Raylib.DrawRectangle(px + 2, py, T - 4, T, C(18, 21, 26));
        Raylib.DrawRectangle(px + 2, py, 3, T, C(40, 46, 54));
        Raylib.DrawRectangle(px + T - 5, py, 3, T, C(40, 46, 54));
        float hgt = T * (1 - o);
        if (hgt > 0.5f)
        {
            Raylib.DrawRectangle(px + 5, py, T - 10, (int)hgt, C(88, 98, 110));
            Raylib.DrawRectangle(px + 5, py, 3, (int)hgt, C(120, 132, 146));
            if (bottom && hgt > 8)
            {
                int sy = py + (int)hgt - 8;
                Raylib.DrawRectangle(px + 5, sy, T - 10, 8, Palette.Hazard);
                for (int k = 0; k < 4; k++)
                    Raylib.DrawTriangle(new Vector2(px + 5 + k * 6, sy + 8), new Vector2(px + 9 + k * 6, sy + 8), new Vector2(px + 9 + k * 6, sy), C(30, 30, 30));
            }
            else Raylib.DrawRectangle(px + 5, py + (int)hgt - 2, T - 10, 2, C(60, 68, 78));
        }
        if (top)
        {
            var lamp = new Vector2(px + 16, py - 7);
            var col = w.GroupActive[g] ? Palette.Green : C(255, 70, 60);
            Raylib.DrawRectangleRounded(new Rectangle(px + 6, py - 12, 20, 9), 0.5f, 4, C(24, 26, 30));
            Raylib.DrawCircleV(lamp, 3, col);
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(lamp, 16, col.WithAlpha(110), col.WithAlpha(0));
            Raylib.EndBlendMode();
        }
    }

    void DrawGoo(World w, int x, int y, int px, int py)
    {
        bool surface = w.At(x, y - 1) != '~';
        if (!surface) { Raylib.DrawRectangleGradientV(px, py, T, T, C(52, 80, 22), Palette.GooDeep); return; }
        Raylib.DrawRectangleGradientV(px, py + 10, T, T - 10, Palette.Goo, C(52, 80, 22));
        const int seg = 8;
        for (int i = 0; i < seg; i++)
        {
            float xa = px + i * (T / (float)seg), xb = xa + T / (float)seg;
            float ya = py + 5 + MathF.Sin(Time * 2.2f + xa * 0.07f) * 2.5f + MathF.Sin(Time * 3.7f + xa * 0.13f);
            float yb = py + 5 + MathF.Sin(Time * 2.2f + xb * 0.07f) * 2.5f + MathF.Sin(Time * 3.7f + xb * 0.13f);
            Raylib.DrawTriangle(new Vector2(xa, ya), new Vector2(xa, py + 12), new Vector2(xb, yb), Palette.Goo);
            Raylib.DrawTriangle(new Vector2(xb, yb), new Vector2(xa, py + 12), new Vector2(xb, py + 12), Palette.Goo);
            Raylib.DrawLineEx(new Vector2(xa, ya), new Vector2(xb, yb), 2, Palette.GooLight);
        }
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawRectangleGradientV(px, py - 40, T, 44, C(120, 200, 40, 0), C(120, 200, 40, 34));
        Raylib.EndBlendMode();
    }

    void DrawFizzler(World w, int x, int y, int px, int py)
    {
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawRectangle(px + 6, py, T - 12, T, C(80, 170, 255, 26));
        for (int k = 0; k < 4; k++)
        {
            float a = 0.5f + 0.5f * MathF.Sin(Time * 7 + y * 1.7f + k * 2.1f + x);
            float lx = px + 8 + k * 5.5f + MathF.Sin(Time * 2.5f + y + k) * 1.5f;
            Raylib.DrawLineEx(new Vector2(lx, py), new Vector2(lx, py + T), 1.4f, C(150, 215, 255, (int)(30 + 70 * a)));
        }
        float scan = ((Time * 90 + x * 40) % (T * 6)) - y % 6 * T;
        if (scan >= 0 && scan < T) Raylib.DrawRectangle(px + 6, py + (int)scan, T - 12, 3, C(200, 240, 255, 90));
        Raylib.EndBlendMode();
        if (IsBlock(w.At(x, y - 1)) || w.At(x, y - 1) == '=') { Raylib.DrawRectangle(px + 4, py, T - 8, 5, C(90, 100, 112)); Raylib.DrawRectangle(px + 8, py + 4, T - 16, 2, Palette.Fizzler); }
        if (IsBlock(w.At(x, y + 1)) || w.At(x, y + 1) == '=') { Raylib.DrawRectangle(px + 4, py + T - 5, T - 8, 5, C(90, 100, 112)); Raylib.DrawRectangle(px + 8, py + T - 6, T - 16, 2, Palette.Fizzler); }
    }

    // ================================================================ objects

    void DrawExit(World w)
    {
        float x = w.Exit.X * T + T / 2f, yb = (w.Exit.Y + 1) * T;
        var frame = new Rectangle(x - 26, yb - 76, 52, 76);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(new Vector2(x, yb - 30), 90, C(60, 230, 140, 40), C(60, 230, 140, 0));
        Raylib.EndBlendMode();
        Raylib.DrawRectangleRounded(frame, 0.15f, 6, C(46, 52, 60));
        var inner = new Rectangle(x - 19, yb - 68, 38, 68);
        Raylib.DrawRectangleGradientV((int)inner.X, (int)inner.Y, (int)inner.Width, (int)inner.Height, C(20, 60, 40), C(70, 220, 140));
        for (int k = 0; k < 3; k++)
        {
            float t = ((Time * 0.8f + k / 3f) % 1f);
            float cy = yb - 10 - t * 55;
            var col = C(200, 255, 220, (int)(200 * MathF.Sin(t * MathF.PI)));
            Raylib.DrawLineEx(new Vector2(x - 8, cy + 6), new Vector2(x, cy), 3, col);
            Raylib.DrawLineEx(new Vector2(x, cy), new Vector2(x + 8, cy + 6), 3, col);
        }
        var sign = new Rectangle(x - 22, yb - 98, 44, 16);
        Raylib.DrawRectangleRounded(sign, 0.3f, 4, C(20, 24, 28));
        UI.Text("EXIT", new Vector2(x, yb - 90), 15, Palette.Green, UI.Align.Center, bold: true);
    }

    void DrawButton(FloorButton b)
    {
        float cx = b.X * T + T / 2f, fy = (b.Y + 1) * T;
        float h = 8 - 5 * b.Press;
        Raylib.DrawRectangleRounded(new Rectangle(cx - 26, fy - 6, 52, 7), 0.5f, 4, C(40, 44, 50));
        Raylib.DrawRectangle((int)cx - 24, (int)fy - 6, 48, 2, C(80, 88, 98));
        var dome = Mix(C(200, 50, 40), C(255, 120, 60), b.Press);
        Raylib.DrawRectangleRounded(new Rectangle(cx - 19, fy - 6 - h, 38, h + 2), 0.6f, 6, dome);
        Raylib.DrawRectangle((int)cx - 14, (int)(fy - 5 - h), 28, 2, C(255, 255, 255, 90));
        if (b.Press > 0.05f)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(new Vector2(cx, fy - 6), 40, C(255, 140, 60, (int)(90 * b.Press)), C(255, 140, 60, 0));
            Raylib.EndBlendMode();
        }
    }

    public void DrawCube(Cube c)
    {
        if (c.Gone) return;
        float a = c.Dissolving ? 1 - Clamp01(c.DissolveT / 0.8f) : 1;
        var pos = c.Pos;
        var r = new Rectangle(pos.X - 14, pos.Y - 14, 28, 28);
        Color Tint(Color col) => (c.Dissolving ? Mix(col, Palette.White, c.DissolveT) : col).WithAlpha(a * col.A / 255f);

        Raylib.DrawRectangleRounded(r, 0.28f, 6, Tint(C(128, 136, 145)));
        Raylib.DrawRectangle((int)r.X + 4, (int)r.Y + 4, 20, 20, Tint(C(176, 184, 192)));
        foreach (var (dx, dy) in new[] { (0, 0), (19, 0), (0, 19), (19, 19) })
            Raylib.DrawRectangleRounded(new Rectangle(r.X + dx, r.Y + dy, 9, 9), 0.4f, 4, Tint(C(96, 104, 114)));
        Raylib.DrawRectangle((int)r.X + 12, (int)r.Y, 4, 4, Tint(C(150, 158, 166)));
        Raylib.DrawRectangle((int)r.X + 12, (int)r.Y + 24, 4, 4, Tint(C(150, 158, 166)));
        Raylib.DrawRectangle((int)r.X, (int)r.Y + 12, 4, 4, Tint(C(150, 158, 166)));
        Raylib.DrawRectangle((int)r.X + 24, (int)r.Y + 12, 4, 4, Tint(C(150, 158, 166)));
        Raylib.DrawCircleV(pos, 7.5f, Tint(C(232, 236, 239)));
        var heart = Tint(C(236, 96, 158));
        Raylib.DrawCircleV(pos + new Vector2(-2.2f, -1.2f), 2.5f, heart);
        Raylib.DrawCircleV(pos + new Vector2(2.2f, -1.2f), 2.5f, heart);
        Raylib.DrawTriangle(pos + new Vector2(-4.6f, -0.4f), pos + new Vector2(0, 4.6f), pos + new Vector2(4.6f, -0.4f), heart);

        float glow = MathF.Max(c.Held ? 0.8f : 0, MathF.Max(c.OnButtonGlow, c.TeleportFlash));
        if (glow > 0.01f && !c.Dissolving)
        {
            var gc = c.Held ? Palette.Blue : Palette.Orange;
            Raylib.DrawRectangleRoundedLinesEx(new Rectangle(r.X - 2, r.Y - 2, 32, 32), 0.3f, 6, 2, gc.WithAlpha(glow * 0.8f));
        }
    }

    public void DrawPlayer(Player p)
    {
        if (p.Dead) return;
        int f = p.Facing;
        float sq = p.Squash;
        float sx = 1 + sq * 0.5f, sy = 1 - sq * 0.6f;

        Rlgl.PushMatrix();
        Rlgl.Translatef(p.Pos.X, p.Pos.Y + p.Half.Y, 0);
        Rlgl.Scalef(sx, sy, 1);

        bool moving = p.OnGround && MathF.Abs(p.Vel.X) > 10;
        float swing = moving ? MathF.Sin(p.WalkPhase) * 0.6f : 0;
        var hip = new Vector2(0, -21);

        void Leg(float ang, float bend, Color col, Color boot)
        {
            var knee = hip + new Vector2(MathF.Sin(ang) * 10 + f * bend, MathF.Cos(ang) * 10);
            var foot = knee + new Vector2(MathF.Sin(ang - bend * 0.08f * f) * 10, 10);
            foot.Y = MathF.Min(foot.Y, 0);
            Raylib.DrawLineEx(hip, knee, 6.5f, col);
            Raylib.DrawLineEx(knee, foot, 5.5f, col);
            // long-fall boot: shoe + spring blade behind the heel
            Raylib.DrawRectangleRounded(new Rectangle(foot.X - 4 + f * 1.5f, foot.Y - 4, 9, 4.5f), 0.5f, 4, boot);
            Raylib.DrawLineEx(foot + new Vector2(-f * 3, -2), foot + new Vector2(-f * 6, -10), 2f, C(170, 178, 186));
        }

        if (p.OnGround)
        {
            Leg(-swing, 1.5f, Palette.SuitDark, C(190, 196, 202));
            Leg(swing, 1.5f, Palette.Suit, C(232, 236, 240));
        }
        else
        {
            Leg(-0.25f * f, 3f, Palette.SuitDark, C(190, 196, 202));
            Leg(0.45f * f, 5f, Palette.Suit, C(232, 236, 240));
        }

        float bob = moving ? MathF.Abs(MathF.Cos(p.WalkPhase)) * 1.2f : 0;
        // torso
        Raylib.DrawRectangleRounded(new Rectangle(-7, -38 - bob, 14, 19), 0.45f, 6, Palette.Tank);
        Raylib.DrawRectangle(-7, -24 - (int)bob, 14, 4, Palette.Suit);
        Raylib.DrawTriangle(new Vector2(-f * 4, -21 - bob), new Vector2(-f * 11, -14 - bob), new Vector2(-f * 7, -22 - bob), Palette.SuitDark);

        // head
        var head = new Vector2(f * 1, -45 - bob);
        Raylib.DrawCircleV(head + new Vector2(-f * 6.5f, 1), 3.6f, Palette.Hair);
        Raylib.DrawLineEx(head + new Vector2(-f * 6.5f, 2), head + new Vector2(-f * 8, 8), 3f, Palette.Hair);
        Raylib.DrawCircleV(head, 7, Palette.Skin);
        Raylib.DrawCircleSector(head, 7.4f, f > 0 ? 150 : 210, f > 0 ? 380 : -20, 12, Palette.Hair);
        Raylib.DrawCircleV(head + new Vector2(f * 3.5f, 0.5f), 1.2f, C(40, 30, 30));

        // portal gun
        var aim = p.Aim;
        float ang = MathF.Atan2(aim.Y, aim.X) * 180 / MathF.PI;
        var shoulder = new Vector2(0, -34 - bob);
        var pivot = shoulder + aim * (9 - p.GunKick * 3);
        var gunCol = p.LastShot == PortalColor.Blue ? Palette.Blue : Palette.Orange;
        Raylib.DrawLineEx(shoulder + new Vector2(-f * 2, 0), pivot + aim * 4, 3.5f, Palette.Skin);
        Raylib.DrawRectanglePro(new Rectangle(pivot.X, pivot.Y, 22, 10), new Vector2(6, 5), ang, C(236, 239, 242));
        Raylib.DrawRectanglePro(new Rectangle(pivot.X, pivot.Y, 12, 4), new Vector2(-2, 2), ang, C(40, 44, 50));
        var tip = pivot + aim * 16;
        Raylib.DrawCircleV(tip, 3.4f, gunCol);
        var perp = new Vector2(-aim.Y, aim.X);
        Raylib.DrawLineEx(tip + perp * 4, tip + perp * 4 + aim * 5, 2, C(200, 205, 210));
        Raylib.DrawLineEx(tip - perp * 4, tip - perp * 4 + aim * 5, 2, C(200, 205, 210));
        Raylib.DrawLineEx(shoulder + new Vector2(f * 3, 1), pivot + aim * 1, 3.5f, Palette.Skin);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(tip, 10 + p.GunKick * 8, gunCol.WithAlpha(150), gunCol.WithAlpha(0));
        if (p.TeleportFlash > 0)
            Raylib.DrawCircleGradient(new Vector2(0, -24), 34, C(255, 255, 255, (int)(90 * p.TeleportFlash)), C(255, 255, 255, 0));
        Raylib.EndBlendMode();

        Rlgl.PopMatrix();
    }

    // ================================================================ lasers, particles

    void DrawLasers(World w)
    {
        if (w.Lasers.Count == 0) return;
        float flick = 1 + 0.12f * MathF.Sin(Time * 47);
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var s in w.Lasers)
        {
            Raylib.DrawLineEx(s.A, s.B, 12 * flick, C(255, 30, 30, 40));
            Raylib.DrawLineEx(s.A, s.B, 6 * flick, C(255, 60, 50, 110));
        }
        foreach (var imp in w.LaserImpacts)
            Raylib.DrawCircleGradient(imp, 16 * flick, C(255, 90, 70, 200), C(255, 40, 40, 0));
        Raylib.EndBlendMode();
        foreach (var s in w.Lasers) Raylib.DrawLineEx(s.A, s.B, 2.2f, C(255, 225, 220));
    }

    void DrawParticles(World w)
    {
        foreach (var p in w.Fx.Items) if (!p.Additive) DrawParticle(p);
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var p in w.Fx.Items) if (p.Additive) DrawParticle(p);
        foreach (var t in w.Fx.Trails)
        {
            float a = t.Life / 0.25f;
            Raylib.DrawLineEx(t.A, t.B, 6 * a, t.Color.WithAlpha(a * 0.35f));
            Raylib.DrawLineEx(t.A, t.B, 2 * a, C(255, 255, 255, (int)(200 * a)));
        }
        Raylib.EndBlendMode();
    }

    static void DrawParticle(Particle p)
    {
        float a = Clamp01(p.Life / p.MaxLife);
        var col = p.Color.WithAlpha(a * p.Color.A / 255f);
        switch (p.Kind)
        {
            case PKind.Dot: Raylib.DrawCircleV(p.Pos, p.Size * (0.4f + 0.6f * a), col); break;
            case PKind.Spark: Raylib.DrawLineEx(p.Pos, p.Pos - p.Vel * 0.035f, p.Size * 0.7f, col); break;
            case PKind.Square:
                Raylib.DrawRectanglePro(new Rectangle(p.Pos.X, p.Pos.Y, p.Size * 2, p.Size * 2), new Vector2(p.Size, p.Size), p.Life * 600, col);
                break;
            case PKind.Ring: Raylib.DrawCircleLinesV(p.Pos, p.Size * (1.5f - a * 0.5f), col); break;
        }
    }

    // ================================================================ portals

    void DrawCone(Portal x, Vector2 eye, Camera2D cam, Texture2D tex)
    {
        if (Vector2.Dot(eye - x.Center, x.Normal) < 2) return;
        var e1 = x.End1; var e2 = x.End2;
        var d1 = Vector2.Normalize(e1 - eye);
        var d2 = Vector2.Normalize(e2 - eye);
        const float D = 380;
        var m1 = e1 + d1 * D * 0.4f; var m2 = e2 + d2 * D * 0.4f;
        var f1 = e1 + d1 * D; var f2 = e2 + d2 * D;
        var col = x.Color == PortalColor.Blue ? Palette.Blue : Palette.Orange;
        var tint = Mix(Palette.White, col, 0.1f);

        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin((int)DrawMode.Quads);
        ConeVertex(e1, cam, tint, 255); ConeVertex(m1, cam, tint, 235); ConeVertex(m2, cam, tint, 235); ConeVertex(e2, cam, tint, 255);
        ConeVertex(m1, cam, tint, 235); ConeVertex(f1, cam, tint, 0); ConeVertex(f2, cam, tint, 0); ConeVertex(m2, cam, tint, 235);
        Rlgl.End();
        Rlgl.SetTexture(0);

        // faint light rays along the cone edges
        Raylib.BeginBlendMode(BlendMode.Additive);
        Rlgl.Begin((int)DrawMode.Lines);
        foreach (var (e, d) in new[] { (e1, d1), (e2, d2) })
        {
            Rlgl.Color4ub(col.R, col.G, col.B, 120); Rlgl.Vertex2f(e.X, e.Y);
            Rlgl.Color4ub(col.R, col.G, col.B, 0); var end = e + d * D * 0.7f; Rlgl.Vertex2f(end.X, end.Y);
        }
        Rlgl.End();
        Raylib.EndBlendMode();
    }

    static void ConeVertex(Vector2 world, Camera2D cam, Color tint, int alpha)
    {
        var s = Raylib.GetWorldToScreen2D(world, cam);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, (byte)alpha);
        Rlgl.TexCoord2f(s.X / K.CanvasW, 1 - s.Y / K.CanvasH);
        Rlgl.Vertex2f(world.X, world.Y);
    }

    public void DrawPortalRim(Portal p, bool linked)
    {
        var col = p.Color == PortalColor.Blue ? Palette.Blue : Palette.Orange;
        var deep = p.Color == PortalColor.Blue ? Palette.BlueDeep : Palette.OrangeDeep;
        float open = EaseOutBack(p.Open);
        float ra = 33 * open, rb = 7.5f;
        var c = p.Center + p.Normal * 1.5f;
        float rx = p.IsWall ? rb : ra, ry = p.IsWall ? ra : rb;
        float pulse = 1 + 0.06f * MathF.Sin(Time * 6 + (int)p.Color * 2);

        Raylib.BeginBlendMode(BlendMode.Additive);
        // light spill onto surrounding wall + floor
        Raylib.DrawCircleGradient(p.Center + p.Normal * 18, 70 * open, col.WithAlpha(55), col.WithAlpha(0));
        Raylib.DrawEllipseV(c, (rx + 10) * pulse, (ry + 10) * pulse, col.WithAlpha(28));
        Raylib.DrawEllipseV(c, (rx + 4) * pulse, (ry + 4) * pulse, col.WithAlpha(40));
        Raylib.EndBlendMode();

        if (!linked)
        {
            // closed portal: a swirling membrane
            Raylib.DrawEllipseV(c, rx, ry, deep);
            for (int k = 0; k < 4; k++)
            {
                float t = Time * (1.5f + k * 0.4f) + k;
                float s = 0.35f + 0.15f * MathF.Sin(t);
                var off = (p.IsWall ? new Vector2(0, 1) : new Vector2(1, 0)) * MathF.Sin(t * 1.3f) * ra * 0.3f;
                Raylib.DrawEllipseV(c + off, rx * s * 1.4f, ry * s * 1.4f, Mix(deep, col, 0.35f + k * 0.1f).WithAlpha(150));
            }
        }
        EllipseRing(c, rx, ry, 3.2f, col);
        EllipseRing(c, rx - 2.5f, ry - 2.5f, 1.2f, C(255, 255, 255, 170));

        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int k = 0; k < 6; k++)
        {
            float t = Time * 2.4f + k * MathF.Tau / 6;
            var pt = c + new Vector2(MathF.Cos(t) * rx, MathF.Sin(t) * ry);
            Raylib.DrawCircleGradient(pt, 5, C(255, 255, 255, 120), col.WithAlpha(0));
        }
        Raylib.EndBlendMode();
    }

    void EllipseRing(Vector2 c, float rx, float ry, float thick, Color col)
    {
        if (rx <= 0 || ry <= 0) return;
        const int seg = 48;
        int n = 0;
        for (int i = 0; i <= seg; i++)
        {
            float a = i * MathF.Tau / seg;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            ringBuf[n++] = c + new Vector2(dir.X * (rx + thick / 2), dir.Y * (ry + thick / 2));
            ringBuf[n++] = c + new Vector2(dir.X * MathF.Max(0, rx - thick / 2), dir.Y * MathF.Max(0, ry - thick / 2));
        }
        Rlgl.Begin((int)DrawMode.Triangles);
        Rlgl.Color4ub(col.R, col.G, col.B, col.A);
        for (int i = 0; i + 3 < n; i += 2)
        {
            Tri(ringBuf[i], ringBuf[i + 1], ringBuf[i + 2]);
            Tri(ringBuf[i + 2], ringBuf[i + 1], ringBuf[i + 3]);
        }
        Rlgl.End();
    }

    static void Tri(Vector2 a, Vector2 b, Vector2 c)
    {
        Rlgl.Vertex2f(a.X, a.Y); Rlgl.Vertex2f(b.X, b.Y); Rlgl.Vertex2f(c.X, c.Y);
    }

    // ================================================================ aim helpers

    public void DrawAimPreview(World w, Player p)
    {
        if (p.Dead || p.Won || w.Def.Gun == GunMode.None) return;
        var origin = World.GunOrigin(p);
        var (hitB, candB) = w.PreviewShot(origin, p.Aim, PortalColor.Blue);
        var cand = candB ?? (w.GunAllows(PortalColor.Orange) ? w.PreviewShot(origin, p.Aim, PortalColor.Orange).candidate : null);
        if (!hitB.Hit) return;
        float len = hitB.Dist;
        for (float d = 24; d < len - 6; d += 14)
        {
            var pt = origin + p.Aim * d;
            Raylib.DrawCircleV(pt, 1.4f, C(255, 255, 255, (int)(60 * (1 - d / (len + 1)) + 20)));
        }
        if (cand != null)
        {
            var c = cand.Center + cand.Normal * 2;
            float rx = cand.IsWall ? 4 : 30, ry = cand.IsWall ? 30 : 4;
            float a = 0.5f + 0.25f * MathF.Sin(Time * 6);
            Raylib.DrawEllipseLines((int)c.X, (int)c.Y, rx, ry, C(255, 255, 255, (int)(160 * a)));
            Raylib.DrawCircleV(hitB.Point, 2.5f, C(255, 255, 255, 200));
        }
        else
        {
            var pt = hitB.Point + hitB.Normal * 4;
            var red = C(255, 90, 80, 200);
            Raylib.DrawLineEx(pt + new Vector2(-5, -5), pt + new Vector2(5, 5), 2, red);
            Raylib.DrawLineEx(pt + new Vector2(-5, 5), pt + new Vector2(5, -5), 2, red);
        }
    }
}
