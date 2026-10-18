using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using Portal2D.Game;
using static Portal2D.Core.MathX;

namespace Portal2D.Render;

/// <summary>
/// Pixel-art renderer. World units are 32 per tile; the camera zoom of 0.5 maps that to 16 px tiles on a
/// 640x360 canvas which is then scaled up with nearest-neighbour filtering.
///
/// Portal windows: for each linked portal X the world is rendered off-screen through the portal transform
/// (partner -> X), so the space in front of the partner lines up behind X. A shader then cuts a
/// half-ellipse "hole" into the wall behind X and shows that image through it - with a swirling
/// refraction near the rim, a dithered glowing edge and a hole that opens wider as you get closer.
/// </summary>
public sealed class Renderer
{
    const int T = K.Tile;
    readonly RenderTexture2D[] viewRt = new RenderTexture2D[2];
    readonly Dictionary<World, LevelArt> art = new();
    Shader windowShader;
    bool shaderOk;
    int locCenter, locNrm, locUp, locRa, locRb, locTime, locTint, locCanvas, locOpen;

    public bool WindowsEnabled = true;
    public float Time;

    const string WindowFs = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec2 canvas;
        uniform vec2 center;
        uniform vec2 nrm;
        uniform vec2 upv;
        uniform float ra;
        uniform float rb;
        uniform float time;
        uniform float open;
        uniform vec4 tint;
        out vec4 finalColor;
        void main() {
            vec2 p = floor(fragTexCoord) + 0.5;           // snap to the pixel grid
            vec2 d = p - center;
            float u = dot(d, upv);
            float v = -dot(d, nrm);                         // depth into the wall
            float e = (u * u) / (ra * ra) + (v * v) / (rb * rb);
            if (v < 0.0 || e > 1.0) discard;
            float k = smoothstep(0.25, 1.0, e);
            float ang = k * 0.9 * sin(time * 2.3 + e * 7.0) * open;
            vec2 q = vec2(u * cos(ang) - v * sin(ang), u * sin(ang) + v * cos(ang)) * (1.0 + 0.12 * k);
            vec2 sp = floor(center + upv * q.x - nrm * q.y) + 0.5;
            vec4 col = texture(texture0, vec2(sp.x / canvas.x, 1.0 - sp.y / canvas.y));
            float checker = mod(floor(p.x) + floor(p.y), 2.0);
            float band = smoothstep(0.55, 1.0, e);
            col.rgb = mix(col.rgb, tint.rgb, band * 0.55);
            if (e > 0.82 && checker > 0.5) col.rgb = mix(col.rgb, tint.rgb, 0.85);
            if (e > 0.93) col.rgb = mix(tint.rgb, vec3(1.0), 0.35);
            float scan = 0.94 + 0.06 * sin(p.y * 3.14159 + time * 6.0);
            col.rgb *= scan * (0.82 + 0.18 * (1.0 - e));
            finalColor = vec4(col.rgb, 1.0);
        }
        """;

    public Renderer()
    {
        for (int i = 0; i < 2; i++)
        {
            viewRt[i] = Raylib.LoadRenderTexture(K.CanvasW, K.CanvasH);
            Raylib.SetTextureFilter(viewRt[i].Texture, TextureFilter.Point);
        }
        windowShader = Raylib.LoadShaderFromMemory(null, WindowFs);
        shaderOk = windowShader.Id > 0 && windowShader.Id != Rlgl.GetShaderIdDefault();
        if (shaderOk)
        {
            locCenter = Raylib.GetShaderLocation(windowShader, "center");
            locNrm = Raylib.GetShaderLocation(windowShader, "nrm");
            locUp = Raylib.GetShaderLocation(windowShader, "upv");
            locRa = Raylib.GetShaderLocation(windowShader, "ra");
            locRb = Raylib.GetShaderLocation(windowShader, "rb");
            locTime = Raylib.GetShaderLocation(windowShader, "time");
            locTint = Raylib.GetShaderLocation(windowShader, "tint");
            locCanvas = Raylib.GetShaderLocation(windowShader, "canvas");
            locOpen = Raylib.GetShaderLocation(windowShader, "open");
        }
    }

    public void Unload()
    {
        foreach (var rt in viewRt) Raylib.UnloadRenderTexture(rt);
        foreach (var a in art.Values) a.Unload();
        if (shaderOk) Raylib.UnloadShader(windowShader);
    }

    LevelArt ArtFor(World w)
    {
        if (art.TryGetValue(w, out var a)) return a;
        // keep the cache tiny: menu world + current level
        foreach (var key in art.Keys.ToList())
            if (key.Def.Name != "Menu") { art[key].Unload(); art.Remove(key); }
        a = new LevelArt(w);
        a.Upload();
        art[w] = a;
        return a;
    }

    static float Snap(float v) => MathF.Round(v * K.Px) / K.Px;
    static Vector2 Snap(Vector2 v) => new(Snap(v.X), Snap(v.Y));

    // ================================================================ frame entry points

    public void PrepareWindows(World w, Camera2D cam)
    {
        if (!WindowsEnabled || !w.Linked) return;
        var screen = ViewRect(cam);
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 2; i++)
        {
            var x = w.Portals[i]!;
            var y = w.Portals[1 - i]!;
            var view = MappedBounds(x, y, screen);
            Raylib.BeginTextureMode(viewRt[i]);
            Raylib.ClearBackground(PP.Bg);
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
        DrawWorld(w, ViewRect(cam), drawPortals: false);
        if (WindowsEnabled && w.Linked)
            for (int i = 0; i < 2; i++)
                DrawWindow(w.Portals[i]!, eye, cam, viewRt[i].Texture);
        foreach (var p in w.Portals)
            if (p != null) DrawPortal(p, w.Linked);
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

    // ================================================================ world

    public void DrawWorld(World w, Rectangle view, bool drawPortals)
    {
        var a = ArtFor(w);
        var full = new Rectangle(0, 0, w.WidthPx, w.HeightPx);
        Raylib.DrawRectangleRec(view, PP.Bg);
        Raylib.DrawTexturePro(a.BackTex, new Rectangle(0, 0, a.Back.W, a.Back.H), full, Vector2.Zero, 0, Color.White);
        DrawLamps(w, view);
        if (w.HasExit) DrawExit(w);
        foreach (var b in w.Buttons) DrawButton(b);
        foreach (var c in w.Cubes) DrawWithClones(w, c, () => DrawCube(c));
        if (w.Player is { } pl) DrawWithClones(w, pl, () => DrawPlayer(pl));
        Raylib.DrawTexturePro(a.FrontTex, new Rectangle(0, 0, a.Front.W, a.Front.H), full, Vector2.Zero, 0, Color.White);

        int x0 = Math.Max(0, (int)(view.X / T)), y0 = Math.Max(0, (int)(view.Y / T));
        int x1 = Math.Min(w.W - 1, (int)((view.X + view.Width) / T)), y1 = Math.Min(w.H - 1, (int)((view.Y + view.Height) / T));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                char c = w.Tiles[x, y];
                if (c == '~') DrawGoo(w, x, y);
                else if (c == '|') DrawFizzler(w, x, y);
                else if (World.IsDoor(c)) DrawDoor(w, x, y, c - 'a');
            }
        if (w.HasExit) DrawExitSign(w);
        foreach (var r in w.Receivers) DrawReceiverGlow(r);
        foreach (var e in w.Emitters) DrawEmitterGlow(e);
        DrawLasers(w);
        DrawParticles(w);
        if (drawPortals)
            foreach (var p in w.Portals)
                if (p != null) DrawPortal(p, w.Linked);
    }

    void DrawWithClones(World w, Body b, Action draw)
    {
        draw();
        if (!w.Linked || b is Cube { Held: true }) return;
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 2; i++)
        {
            var p = w.Portals[i]!;
            float u = p.U(b.Pos), v = p.V(b.Pos);
            float extN = b.ExtentAlong(p.Normal) + 4, extT = b.ExtentAlong(p.Up);
            if (MathF.Abs(u) > Portal.HalfLen + extT || v > extN || v < -extN) continue;
            Rlgl.PushMatrix();
            Portal.Matrix(p, w.Portals[1 - i]!, m);
            unsafe { fixed (float* f = m) Rlgl.MultMatrixf(f); }
            draw();
            Rlgl.PopMatrix();
        }
    }

    void DrawLamps(World w, Rectangle view)
    {
        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int x = 3; x < w.W - 3; x += 7)
        {
            int ly = -1;
            for (int y = 1; y < w.H - 4; y++)
                if (!w.IsSolid(x, y) && w.IsSolid(x, y - 1) && !w.IsSolid(x, y + 1) && !w.IsSolid(x, y + 2)) { ly = y; break; }
            if (ly < 0) continue;
            var l = new Vector2(x * T + T / 2f, ly * T);
            if (l.X < view.X - 300 || l.X > view.X + view.Width + 300) continue;
            float flick = 0.85f + 0.15f * MathF.Sin(Time * 1.3f + l.X);
            GradientQuad(l + new Vector2(-12, 8), l + new Vector2(12, 8), l + new Vector2(130, 300), l + new Vector2(-130, 300),
                C(255, 236, 200, (int)(30 * flick)), C(255, 236, 200, 0));
            Raylib.DrawCircleGradient(l + new Vector2(0, 8), 40, C(255, 230, 190, 50), C(255, 230, 190, 0));
        }
        Raylib.EndBlendMode();
        for (int x = 3; x < w.W - 3; x += 7)
            for (int y = 1; y < w.H - 4; y++)
                if (!w.IsSolid(x, y) && w.IsSolid(x, y - 1) && !w.IsSolid(x, y + 1) && !w.IsSolid(x, y + 2))
                {
                    Raylib.DrawTexturePro(Sprites.LampTex, new Rectangle(0, 0, 14, 4), new Rectangle(x * T + 2, y * T, 28, 8), Vector2.Zero, 0, Color.White);
                    break;
                }
    }

    static void GradientQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color near, Color far)
    {
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(near.R, near.G, near.B, near.A); Rlgl.Vertex2f(a.X, a.Y);
        Rlgl.Color4ub(far.R, far.G, far.B, far.A); Rlgl.Vertex2f(d.X, d.Y);
        Rlgl.Color4ub(far.R, far.G, far.B, far.A); Rlgl.Vertex2f(c.X, c.Y);
        Rlgl.Color4ub(near.R, near.G, near.B, near.A); Rlgl.Vertex2f(b.X, b.Y);
        Rlgl.End();
    }

    // pixel rectangle in world units, snapped to the pixel grid (1 px = 2 units)
    static void Px(float x, float y, float w, float h, Color c) =>
        Raylib.DrawRectangle((int)Snap(x), (int)Snap(y), (int)MathF.Max(2, Snap(w)), (int)MathF.Max(2, Snap(h)), c);

    // ================================================================ dynamic tiles

    void DrawGoo(World w, int tx, int ty)
    {
        int ox = tx * T, oy = ty * T;
        bool surf = w.At(tx, ty - 1) != '~';
        if (!surf)
        {
            Raylib.DrawRectangle(ox, oy, T, T, PP.Goo4);
            for (int i = 0; i < 16; i += 4) Px(ox + i * 2 + (ty % 2) * 4, oy + ((i * 3 + tx * 5) % 16) * 2, 2, 2, PP.Goo3);
            return;
        }
        for (int i = 0; i < 16; i++)
        {
            float wx = tx * 16 + i;
            int top = 3 + (int)MathF.Round(MathF.Sin(Time * 2.4f + wx * 0.45f) * 1.2f + MathF.Sin(Time * 1.3f + wx * 0.17f) * 0.7f);
            int x = ox + i * 2;
            Raylib.DrawRectangle(x, oy + top * 2, 2, 2, PP.GooTop);
            Raylib.DrawRectangle(x, oy + top * 2 + 2, 2, 2, PP.Goo1);
            Raylib.DrawRectangle(x, oy + top * 2 + 4, 2, (9 - top) * 2, PP.Goo2);
            Raylib.DrawRectangle(x, oy + 18 + 4, 2, T - 22, PP.Goo3);
            if ((i + tx) % 3 == 0) Raylib.DrawRectangle(x, oy + 20, 2, 2, PP.Goo3);
        }
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawRectangleGradientV(ox, oy - 40, T, 46, C(140, 230, 70, 0), C(140, 230, 70, 30));
        Raylib.EndBlendMode();
    }

    void DrawFizzler(World w, int tx, int ty)
    {
        int ox = tx * T, oy = ty * T;
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawRectangle(ox + 6, oy, 20, T, C(42, 111, 191, 34));
        for (int k = 0; k < 3; k++)
        {
            float a = 0.5f + 0.5f * MathF.Sin(Time * 8 + ty * 1.7f + k * 2.1f + tx);
            Raylib.DrawRectangle(ox + 8 + k * 8, oy, 2, T, C(143, 211, 255, (int)(40 + 120 * a)));
        }
        int step = (int)(Time * 20);
        for (int s = 0; s < 3; s++)
            if (Hash(tx * 31 + s, ty * 17 + step, 4) < 0.35f)
                Raylib.DrawRectangle(ox + 6 + (int)(Hash(tx, ty + s, step) * 10) * 2, oy + (int)(Hash(ty, tx + s, step) * 16) * 2, 2, 2, C(230, 248, 255, 220));
        Raylib.EndBlendMode();
        bool solidAbove = w.IsSolid(tx, ty - 1) || w.At(tx, ty - 1) == '=';
        bool solidBelow = w.IsSolid(tx, ty + 1) || w.At(tx, ty + 1) == '=';
        if (solidAbove) { Raylib.DrawRectangle(ox + 2, oy, 28, 6, C(93, 102, 124)); Raylib.DrawRectangle(ox + 6, oy + 6, 20, 2, PP.Fizz); }
        if (solidBelow) { Raylib.DrawRectangle(ox + 2, oy + T - 6, 28, 6, C(93, 102, 124)); Raylib.DrawRectangle(ox + 6, oy + T - 8, 20, 2, PP.Fizz); }
    }

    void DrawDoor(World w, int tx, int ty, int g)
    {
        int ox = tx * T, oy = ty * T;
        float o = w.DoorOpen[g];
        char dc = (char)('a' + g);
        bool top = w.At(tx, ty - 1) != dc, bottom = w.At(tx, ty + 1) != dc;
        Raylib.DrawRectangle(ox + 2, oy, 28, T, C(22, 24, 30));
        Raylib.DrawRectangle(ox + 2, oy, 2, T, C(58, 65, 80));
        Raylib.DrawRectangle(ox + 28, oy, 2, T, C(58, 65, 80));
        int hgt = (int)(Snap(T * (1 - o)));
        if (hgt >= 2)
        {
            Raylib.DrawRectangle(ox + 6, oy, 20, hgt, C(93, 102, 124));
            Raylib.DrawRectangle(ox + 8, oy, 2, hgt, C(130, 139, 160));
            for (int y = 6; y < hgt; y += 8) Raylib.DrawRectangle(ox + 6, oy + y, 20, 2, C(70, 76, 92));
            if (bottom && hgt >= 8)
                for (int i = 0; i < 10; i++)
                    Raylib.DrawRectangle(ox + 6 + i * 2, oy + hgt - 6, 2, 6, ((i / 2) % 2 == 0) ? PP.Hazard : C(30, 30, 34));
        }
        if (top)
        {
            var col = w.GroupActive[g] ? PP.Exit : C(255, 80, 70);
            Raylib.DrawRectangle(ox + 8, oy - 10, 16, 8, C(22, 24, 30));
            Raylib.DrawRectangle(ox + 12, oy - 8, 8, 4, col);
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(new Vector2(ox + 16, oy - 6), 18, col.WithAlpha(90), col.WithAlpha(0));
            Raylib.EndBlendMode();
        }
    }

    void DrawReceiverGlow(Receiver r)
    {
        if (r.Glow < 0.01f) return;
        var c = new Vector2(r.X * T + 16, r.Y * T + 16);
        var col = Mix(C(255, 120, 70), C(255, 220, 160), r.Glow);
        Raylib.DrawRectangle((int)c.X - 6, (int)c.Y - 6, 12, 12, col.WithAlpha(r.Glow));
        Raylib.DrawRectangle((int)c.X - 8, (int)c.Y - 4, 16, 8, col.WithAlpha(r.Glow));
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(c, 40, C(255, 120, 60, (int)(150 * r.Glow)), C(255, 80, 40, 0));
        Raylib.EndBlendMode();
    }

    void DrawEmitterGlow(Emitter e)
    {
        var c = new Vector2(e.X * T + 16, e.Y * T + 16) + e.Dir * 14;
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(c, 18 + MathF.Sin(Time * 20) * 2, C(255, 60, 60, 120), C(255, 40, 40, 0));
        Raylib.EndBlendMode();
    }

    // ================================================================ props & characters

    void DrawExit(World w)
    {
        float x = w.Exit.X * T + T / 2f, yb = (w.Exit.Y + 1) * T;
        var dest = new Rectangle(x - 28, yb - 88, 56, 88);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(new Vector2(x, yb - 36), 90, C(60, 230, 140, 45), C(60, 230, 140, 0));
        Raylib.EndBlendMode();
        Raylib.DrawTexturePro(Sprites.ExitTex, new Rectangle(0, 0, 28, 44), dest, Vector2.Zero, 0, Color.White);
        for (int k = 0; k < 3; k++)
        {
            float t = (Time * 0.8f + k / 3f) % 1f;
            float cy = Snap(yb - 12 - t * 56);
            var col = C(210, 255, 225, (int)(220 * MathF.Sin(t * MathF.PI)));
            for (int i = 0; i < 4; i++)
            {
                Raylib.DrawRectangle((int)x - 8 + i * 2, (int)cy + 6 - i * 2, 2, 2, col);
                Raylib.DrawRectangle((int)x + 6 - i * 2, (int)cy + 6 - i * 2, 2, 2, col);
            }
        }
    }

    static void DrawExitSign(World w)
    {
        float x = w.Exit.X * T + T / 2f, yb = (w.Exit.Y + 1) * T;
        Raylib.DrawRectangle((int)x - 26, (int)yb - 112, 52, 20, C(20, 24, 28));
        Raylib.DrawRectangle((int)x - 26, (int)yb - 112, 52, 2, C(58, 65, 80));
        var m = PixelFont.Measure("EXIT", 1);
        PixelFont.Draw("EXIT", new Vector2(x - m.X / 2, yb - 108), 1, PP.Exit);
    }

    void DrawButton(FloorButton b)
    {
        float cx = b.X * T + T / 2f, fy = (b.Y + 1) * T;
        int h = b.Press > 0.5f ? 4 : 8;
        Raylib.DrawRectangle((int)cx - 28, (int)fy - 6, 56, 6, C(43, 47, 58));
        Raylib.DrawRectangle((int)cx - 28, (int)fy - 6, 56, 2, C(74, 81, 99));
        var dome = b.Press > 0.5f ? C(255, 120, 70) : C(217, 69, 58);
        Raylib.DrawRectangle((int)cx - 18, (int)fy - 6 - h, 36, h, dome);
        Raylib.DrawRectangle((int)cx - 16, (int)fy - 6 - h, 28, 2, C(255, 160, 140));
        if (b.Press > 0.05f)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(new Vector2(cx, fy - 6), 44, C(255, 140, 60, (int)(80 * b.Press)), C(255, 140, 60, 0));
            Raylib.EndBlendMode();
        }
    }

    public void DrawCube(Cube c)
    {
        if (c.Gone) return;
        var pos = Snap(c.Pos);
        var tint = Color.White;
        if (c.Dissolving)
        {
            float t = Clamp01(c.DissolveT / 0.8f);
            tint = C(255, 255, 255, (int)(255 * (1 - t)));
        }
        var dest = new Rectangle(pos.X - 16, pos.Y - 16, 32, 32);
        Raylib.DrawTexturePro(Sprites.CubeTex, new Rectangle(0, 0, 16, 16), dest, Vector2.Zero, 0, tint);
        if (c.Dissolving)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawTexturePro(Sprites.CubeTex, new Rectangle(0, 0, 16, 16), dest, Vector2.Zero, 0, C(255, 255, 255, (int)(200 * Clamp01(c.DissolveT))));
            Raylib.EndBlendMode();
            return;
        }
        float glow = MathF.Max(c.Held ? 1 : 0, MathF.Max(c.OnButtonGlow, c.TeleportFlash));
        if (glow > 0.05f)
        {
            var gc = (c.Held ? PP.BlueRamp[2] : PP.OrangeRamp[2]).WithAlpha(glow * 0.9f);
            Raylib.DrawRectangleLinesEx(new Rectangle(pos.X - 18, pos.Y - 18, 36, 36), 2, gc);
        }
    }

    public void DrawPlayer(Player p)
    {
        if (p.Dead) return;
        int frame;
        if (!p.OnGround) frame = p.Vel.Y < 0 ? 6 : 7;
        else if (MathF.Abs(p.Vel.X) > 10) frame = 2 + (int)(p.WalkPhase * 1.3f) % 4;
        else frame = (Time % 3.2f) < 0.12f ? 1 : 0;

        var feet = Snap(new Vector2(p.Pos.X, p.Pos.Y + p.Half.Y));
        int f = p.Facing;
        var src = new Rectangle(frame * 16, 0, f > 0 ? 16 : -16, 16);
        var dest = new Rectangle(feet.X - 16, feet.Y - 32 + 2, 32, 32);
        Raylib.DrawTexturePro(Sprites.PlayerTex, src, dest, Vector2.Zero, 0, Color.White);
        if (p.TeleportFlash > 0.05f)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawTexturePro(Sprites.PlayerTex, src, dest, Vector2.Zero, 0, C(255, 255, 255, (int)(180 * p.TeleportFlash)));
            Raylib.EndBlendMode();
        }

        // pixel portal gun held in the front claw, pointing at the cursor
        var bodyTop = frame == 6 ? 2 : (frame is 3 or 5 ? 4 : 3);
        int armY = frame is 6 or 7 ? bodyTop + 1 : bodyTop + 4;
        var hand = new Vector2(feet.X + f * 12, feet.Y - 32 + 2 + armY * 2 + 2);
        var aim = p.Aim;
        var gunCol = p.LastShot == PortalColor.Blue ? PP.BlueRamp[2] : PP.OrangeRamp[2];
        float kick = p.GunKick * 2;
        for (int k = 0; k <= 5; k++)
        {
            var pt = hand + aim * (k * 2.2f * 2 - kick);
            var c = k == 5 ? gunCol : k == 1 || k == 2 ? C(58, 65, 80) : C(232, 228, 220);
            Raylib.DrawRectangle((int)Snap(pt.X) - 2, (int)Snap(pt.Y) - 2, 4, 4, PP.BodyOut);
            Raylib.DrawRectangle((int)Snap(pt.X), (int)Snap(pt.Y), 2, 2, c);
        }
        var tip = hand + aim * (5 * 4.4f - kick);
        Raylib.DrawRectangle((int)Snap(tip.X), (int)Snap(tip.Y), 2, 2, gunCol);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(tip, 10 + p.GunKick * 10, gunCol.WithAlpha(150), gunCol.WithAlpha(0));
        Raylib.EndBlendMode();
    }

    // ================================================================ lasers & particles

    void DrawLasers(World w)
    {
        if (w.Lasers.Count == 0) return;
        bool flick = (int)(Time * 30) % 7 == 0;
        foreach (var s in w.Lasers)
        {
            float x0 = MathF.Min(s.A.X, s.B.X), x1 = MathF.Max(s.A.X, s.B.X);
            float y0 = MathF.Min(s.A.Y, s.B.Y), y1 = MathF.Max(s.A.Y, s.B.Y);
            bool horiz = y1 - y0 < x1 - x0;
            Raylib.BeginBlendMode(BlendMode.Additive);
            if (horiz) Raylib.DrawRectangle((int)x0, (int)Snap(y0) - 8, (int)(x1 - x0), 16, C(255, 40, 40, flick ? 20 : 40));
            else Raylib.DrawRectangle((int)Snap(x0) - 8, (int)y0, 16, (int)(y1 - y0), C(255, 40, 40, flick ? 20 : 40));
            Raylib.EndBlendMode();
            if (horiz)
            {
                Raylib.DrawRectangle((int)x0, (int)Snap(y0) - 2, (int)(x1 - x0), 6, PP.Laser);
                Raylib.DrawRectangle((int)x0, (int)Snap(y0), (int)(x1 - x0), 2, PP.LaserCore);
            }
            else
            {
                Raylib.DrawRectangle((int)Snap(x0) - 2, (int)y0, 6, (int)(y1 - y0), PP.Laser);
                Raylib.DrawRectangle((int)Snap(x0), (int)y0, 2, (int)(y1 - y0), PP.LaserCore);
            }
        }
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var imp in w.LaserImpacts)
            Raylib.DrawCircleGradient(imp, flick ? 12 : 16, C(255, 90, 70, 200), C(255, 40, 40, 0));
        Raylib.EndBlendMode();
    }

    void DrawParticles(World w)
    {
        foreach (var p in w.Fx.Items) if (!p.Additive) DrawParticle(p);
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var p in w.Fx.Items) if (p.Additive) DrawParticle(p);
        foreach (var t in w.Fx.Trails)
        {
            float a = t.Life / 0.25f;
            // dotted pixel tracer
            var d = t.B - t.A;
            float len = d.Length();
            if (len < 1) continue;
            var dir = d / len;
            for (float s = 0; s < len; s += 6)
            {
                var pt = t.A + dir * s;
                Raylib.DrawRectangle((int)Snap(pt.X), (int)Snap(pt.Y), 2, 2, t.Color.WithAlpha(a));
            }
        }
        Raylib.EndBlendMode();
    }

    static void DrawParticle(Particle p)
    {
        float a = Clamp01(p.Life / p.MaxLife);
        var col = p.Color.WithAlpha(a * p.Color.A / 255f);
        int size = p.Size > 3 ? 4 : 2;
        if (p.Kind == PKind.Spark)
        {
            var tail = p.Pos - p.Vel * 0.03f;
            Raylib.DrawRectangle((int)Snap(tail.X), (int)Snap(tail.Y), 2, 2, col.WithAlpha(a * 0.5f));
        }
        if (p.Kind == PKind.Ring)
        {
            Raylib.DrawRectangle((int)Snap(p.Pos.X) - 2, (int)Snap(p.Pos.Y), 2, 2, col);
            Raylib.DrawRectangle((int)Snap(p.Pos.X) + 2, (int)Snap(p.Pos.Y), 2, 2, col);
            Raylib.DrawRectangle((int)Snap(p.Pos.X), (int)Snap(p.Pos.Y) - 2, 2, 2, col);
            return;
        }
        Raylib.DrawRectangle((int)Snap(p.Pos.X), (int)Snap(p.Pos.Y), size, size, col);
    }

    // ================================================================ portals

    void DrawWindow(Portal x, Vector2 eye, Camera2D cam, Texture2D tex)
    {
        float open = EaseOutCubic(x.Open);
        float dist = Vector2.Distance(eye, x.Center);
        float near = Clamp01(1 - dist / 700f);
        bool inFront = Vector2.Dot(eye - x.Center, x.Normal) > 0;
        float ra = (24 + 10 * near) * open;
        float rb = (26 + 54 * near * near * (inFront ? 1 : 0.4f)) * open;
        if (ra < 1 || rb < 1) return;
        var col = x.Color == PortalColor.Blue ? PP.BlueRamp[2] : PP.OrangeRamp[2];

        // quad covering the half-ellipse behind the portal (world units)
        float raW = ra / K.Px + 4, rbW = rb / K.Px + 4;
        var c = x.Center;
        var p0 = c + x.Up * raW + x.Normal * 2;
        var p1 = c - x.Up * raW + x.Normal * 2;
        var p2 = c - x.Up * raW - x.Normal * rbW;
        var p3 = c + x.Up * raW - x.Normal * rbW;

        if (shaderOk)
        {
            var sc = Raylib.GetWorldToScreen2D(c, cam);
            Raylib.SetShaderValue(windowShader, locCenter, sc, ShaderUniformDataType.Vec2);
            Raylib.SetShaderValue(windowShader, locNrm, x.Normal, ShaderUniformDataType.Vec2);
            Raylib.SetShaderValue(windowShader, locUp, x.Up, ShaderUniformDataType.Vec2);
            Raylib.SetShaderValue(windowShader, locRa, ra, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(windowShader, locRb, rb, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(windowShader, locTime, Time, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(windowShader, locOpen, open, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(windowShader, locCanvas, new Vector2(K.CanvasW, K.CanvasH), ShaderUniformDataType.Vec2);
            Raylib.SetShaderValue(windowShader, locTint, new Vector4(col.R / 255f, col.G / 255f, col.B / 255f, 1), ShaderUniformDataType.Vec4);
            Raylib.BeginShaderMode(windowShader);
            Rlgl.SetTexture(tex.Id);
            Rlgl.Begin((int)DrawMode.Quads);
            foreach (var v in new[] { p0, p3, p2, p1 })
            {
                var s = Raylib.GetWorldToScreen2D(v, cam);
                Rlgl.Color4ub(255, 255, 255, 255);
                Rlgl.TexCoord2f(s.X, s.Y);
                Rlgl.Vertex2f(v.X, v.Y);
            }
            Rlgl.End();
            Rlgl.SetTexture(0);
            Raylib.EndShaderMode();
        }
        else
        {
            // fallback without shaders: a plain rectangular window
            Rlgl.SetTexture(tex.Id);
            Rlgl.Begin((int)DrawMode.Quads);
            foreach (var v in new[] { p0, p3, p2, p1 })
            {
                var s = Raylib.GetWorldToScreen2D(v, cam);
                Rlgl.Color4ub(255, 255, 255, 255);
                Rlgl.TexCoord2f(s.X / K.CanvasW, 1 - s.Y / K.CanvasH);
                Rlgl.Vertex2f(v.X, v.Y);
            }
            Rlgl.End();
            Rlgl.SetTexture(0);
        }
    }

    public void DrawPortal(Portal p, bool linked)
    {
        int ci = (int)p.Color, li = linked ? 1 : 0;
        int frame = (int)(Time * 10 + ci * 3) % Sprites.PortalFrames;
        float open = EaseOutBack(p.Open);
        var col = ci == 0 ? PP.BlueRamp[2] : PP.OrangeRamp[2];

        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(p.Center + p.Normal * 20, 70 * open, col.WithAlpha(60), col.WithAlpha(0));
        Raylib.EndBlendMode();

        var c = Snap(p.Center);
        if (p.IsWall)
        {
            float hgt = 80 * open;
            var dest = new Rectangle(c.X - 12, c.Y - Snap(hgt / 2), 24, Snap(hgt));
            Raylib.DrawTexturePro(Sprites.PortalVTex[ci, li], new Rectangle(frame * 12, 0, 12, 40), dest, Vector2.Zero, 0, Color.White);
        }
        else
        {
            float wid = 80 * open;
            var dest = new Rectangle(c.X - Snap(wid / 2), c.Y - 12, Snap(wid), 24);
            Raylib.DrawTexturePro(Sprites.PortalHTex[ci, li], new Rectangle(frame * 40, 0, 40, 12), dest, Vector2.Zero, 0, Color.White);
        }
    }

    public void DrawAimPreview(World w, Player p)
    {
        if (p.Dead || p.Won || w.Def.Gun == GunMode.None) return;
        var origin = World.GunOrigin(p);
        var (hit, candB) = w.PreviewShot(origin, p.Aim, PortalColor.Blue);
        var cand = candB ?? (w.GunAllows(PortalColor.Orange) ? w.PreviewShot(origin, p.Aim, PortalColor.Orange).candidate : null);
        if (!hit.Hit) return;
        for (float d = 30; d < hit.Dist - 8; d += 12)
        {
            var pt = origin + p.Aim * d;
            Raylib.DrawRectangle((int)Snap(pt.X), (int)Snap(pt.Y), 2, 2, C(255, 255, 255, (int)(90 * (1 - d / (hit.Dist + 1)) + 30)));
        }
        if (cand != null)
        {
            bool blink = (int)(Time * 4) % 2 == 0;
            var c = Snap(cand.Center);
            var col = C(255, 255, 255, blink ? 220 : 120);
            for (int k = -16; k <= 16; k += 4)
            {
                var pt = c + cand.Up * k * 2;
                Raylib.DrawRectangle((int)pt.X, (int)pt.Y, 2, 2, col);
            }
            foreach (int s in new[] { -1, 1 })
            {
                var end = c + cand.Up * 34 * s;
                Raylib.DrawRectangle((int)Snap(end.X) - 2, (int)Snap(end.Y) - 2, 6, 6, col);
            }
        }
        else
        {
            var pt = Snap(hit.Point + hit.Normal * 6);
            var red = C(255, 90, 80, 230);
            for (int k = -2; k <= 2; k++)
            {
                Raylib.DrawRectangle((int)pt.X + k * 2, (int)pt.Y + k * 2, 2, 2, red);
                Raylib.DrawRectangle((int)pt.X + k * 2, (int)pt.Y - k * 2, 2, 2, red);
            }
        }
    }
}
