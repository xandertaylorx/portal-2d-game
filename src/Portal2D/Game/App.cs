using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using Portal2D.Render;
using static Portal2D.Core.MathX;

namespace Portal2D.Game;

public sealed class App
{
    enum Screen { Menu, Select, Options, Controls, Play, Complete, Ending }

    Screen screen = Screen.Menu;
    readonly SaveData save = SaveData.Load(Levels.All.Length);
    readonly Audio audio = new();
    Renderer renderer = null!;
    RenderTexture2D canvas;
    readonly World menuWorld = new(Levels.Menu);
    World? world;
    int level;

    Camera2D cam;
    Vector2 camPos;
    float shake;
    readonly Random rng = new();

    bool paused, quit, showIntro;
    float time, stateT, levelTime, wonT, hintT, accumulator;
    int deaths;
    bool hintsOn = true;
    bool newBest;
    float fade = 1;
    int fadeDir = -1;
    Action? fadeAction;
    Rectangle dest;
    bool cursorHidden;
    readonly string[] shotArgs;

    public App(string[] args) { shotArgs = args; }

    // ================================================================ lifecycle

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(K.ScreenW, K.ScreenH, "Portal 2D");
        Raylib.SetWindowMinSize(640, 360);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(144);
        Rlgl.DisableBackfaceCulling();

        UI.Load();
        audio.Load();
        audio.Master = save.Master; audio.SfxVolume = save.Sfx; audio.AmbienceVolume = save.Ambience;
        audio.ApplyVolumes();
        UI.OnHover = audio.Hover;
        UI.OnClick = audio.Click;
        renderer = new Renderer { ConesEnabled = save.Cones };
        canvas = Raylib.LoadRenderTexture(K.CanvasW, K.CanvasH);
        Raylib.SetTextureFilter(canvas.Texture, TextureFilter.Bilinear);
        if (save.Fullscreen) Raylib.ToggleBorderlessWindowed();

        camPos = new Vector2(menuWorld.WidthPx / 2, menuWorld.HeightPx / 2);
        if (shotArgs.Length > 0 && RunShot()) { Shutdown(); return; }

        while (!Raylib.WindowShouldClose() && !quit)
        {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
            Frame(dt);
        }
        Shutdown();
    }

    void Shutdown()
    {
        save.Store();
        renderer.Unload();
        Raylib.UnloadRenderTexture(canvas);
        UI.Unload();
        audio.Unload();
        Raylib.CloseWindow();
    }

    /// <summary>--shot &lt;menu|select|level:N&gt; &lt;seconds&gt; &lt;file.png&gt; [script] : render a screenshot (used for testing).</summary>
    bool RunShot()
    {
        int i = Array.IndexOf(shotArgs, "--shot");
        if (i < 0 || i + 3 >= shotArgs.Length) return false;
        string what = shotArgs[i + 1];
        float secs = float.Parse(shotArgs[i + 2], System.Globalization.CultureInfo.InvariantCulture);
        string file = shotArgs[i + 3];
        string? script = i + 4 < shotArgs.Length ? shotArgs[i + 4] : null;
        if (what.StartsWith("level:"))
        {
            level = int.Parse(what[6..]) - 1;
            LoadLevel(true);
            screen = Screen.Play;
        }
        else if (what == "select") screen = Screen.Select;
        else if (what == "options") screen = Screen.Options;
        else if (what == "controls") screen = Screen.Controls;
        else if (what == "complete") { level = 3; LoadLevel(true); screen = Screen.Complete; levelTime = 23.4f; }
        else if (what == "pause") { level = 0; LoadLevel(true); screen = Screen.Play; paused = true; }
        fade = 0; fadeDir = 0;
        IEnumerator<InputState>? bot = null;
        if (script == "solve" && world != null)
        {
            var b = new Testing.Bot(world);
            bot = Testing.Solutions.For(level, b).GetEnumerator();
        }
        int frames = (int)(secs * 60);
        for (int f = 0; f < frames; f++)
        {
            if (bot != null)
            {
                forced = true;
                forcedInput = bot.MoveNext() ? bot.Current : default;
                if (world?.Player is { } pp && forcedInput.AimWorld == default) forcedInput.AimWorld = pp.Pos + new Vector2(200, 0);
            }
            Frame(1 / 60f);
        }
        Raylib.BeginDrawing();
        Raylib.EndDrawing();
        var img = Raylib.LoadImageFromTexture(canvas.Texture);
        Raylib.ImageFlipVertical(ref img);
        Raylib.ImageResize(ref img, K.ScreenW, K.ScreenH);
        Raylib.ExportImage(img, file);
        Raylib.UnloadImage(img);
        return true;
    }

    InputState forcedInput;
    bool forced;

    void Frame(float dt)
    {
        UpdateLetterbox();
        UI.BeginFrame(dt, Raylib.GetMousePosition());
        if (fadeAction != null) UI.Click = false;
        time += dt;
        stateT += dt;
        renderer.Time = time;
        audio.Update();

        if (Raylib.IsKeyPressed(KeyboardKey.F11)) ToggleFullscreen();

        Update(dt);
        Draw();

        if (fadeDir > 0)
        {
            fade += dt * 5;
            if (fade >= 1) { fade = 1; var a = fadeAction; fadeAction = null; a?.Invoke(); fadeDir = -1; }
        }
        else if (fadeDir < 0)
        {
            fade -= dt * 3.5f;
            if (fade <= 0) { fade = 0; fadeDir = 0; }
        }
    }

    void UpdateLetterbox()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        float scale = MathF.Min(sw / (float)K.ScreenW, sh / (float)K.ScreenH);
        float dw = K.ScreenW * scale, dh = K.ScreenH * scale;
        float ox = (sw - dw) / 2, oy = (sh - dh) / 2;
        dest = new Rectangle(ox, oy, dw, dh);
        Raylib.SetMouseOffset(-(int)ox, -(int)oy);
        Raylib.SetMouseScale(1 / scale, 1 / scale);
    }

    void ToggleFullscreen()
    {
        Raylib.ToggleBorderlessWindowed();
        save.Fullscreen = !save.Fullscreen;
        save.Store();
    }

    void Go(Action a)
    {
        if (fadeAction != null) return;
        fadeAction = a;
        fadeDir = 1;
    }

    void SetScreen(Screen s) { screen = s; stateT = 0; paused = false; }

    // ================================================================ update

    void Update(float dt)
    {
        bool playing = screen is Screen.Play or Screen.Complete && world != null;
        bool wantHidden = screen == Screen.Play && !paused && world?.Player is { Dead: false };
        if (wantHidden != cursorHidden)
        {
            if (wantHidden) Raylib.HideCursor(); else Raylib.ShowCursor();
            cursorHidden = wantHidden;
        }

        if (!playing)
        {
            Step(menuWorld, dt, default, silent: true);
            camPos = new Vector2(menuWorld.WidthPx / 2 + MathF.Sin(time * 0.15f) * 10, menuWorld.HeightPx / 2);
            UpdateCam(dt);
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) && screen != Screen.Menu) Go(() => SetScreen(Screen.Menu));
            return;
        }

        var w = world!;
        if (screen == Screen.Play)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { paused = !paused; audio.Click(); }
            if (!paused)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.R)) { Go(() => LoadLevel(false)); }
                if (Raylib.IsKeyPressed(KeyboardKey.H)) { hintsOn = !hintsOn; hintT = 0; }
                var input = forced ? forcedInput : ReadInput();
                Step(w, dt, input, silent: false);
                hintT += dt;

                var p = w.Player;
                if (p != null)
                {
                    if (!p.Dead && !p.Won) levelTime += dt;
                    if (p.Dead && p.DeadTimer > 1.1f && fadeAction == null)
                    {
                        deaths++;
                        save.TotalDeaths++;
                        Go(() => LoadLevel(false));
                    }
                    if (p.Won)
                    {
                        wonT += dt;
                        if (wonT > 1.0f && fadeAction == null) CompleteLevel();
                    }
                }
            }
        }
        else
        {
            Step(w, dt, default, silent: true);
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) NextLevel();
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Go(() => SetScreen(Screen.Menu));
        }
        FollowPlayer(dt, false);
        UpdateCam(dt);
    }

    InputState ReadInput()
    {
        int mx = 0;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) mx -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) mx += 1;
        bool jp = Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.Space) || Raylib.IsKeyPressed(KeyboardKey.Up);
        bool jh = Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Space) || Raylib.IsKeyDown(KeyboardKey.Up);
        return new InputState
        {
            MoveX = mx, JumpPressed = jp, JumpHeld = jh,
            UsePressed = Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.F),
            FireBlue = Raylib.IsMouseButtonPressed(MouseButton.Left),
            FireOrange = Raylib.IsMouseButtonPressed(MouseButton.Right),
            AimWorld = Raylib.GetScreenToWorld2D(UI.Mouse * K.SS, cam)
        };
    }

    void Step(World w, float dt, InputState input, bool silent)
    {
        accumulator += dt;
        bool first = true;
        int guard = 0;
        while (accumulator >= K.SimDt && guard++ < 10)
        {
            var i = input;
            if (!first) { i.JumpPressed = false; i.UsePressed = false; i.FireBlue = false; i.FireOrange = false; }
            w.Update(K.SimDt, i);
            first = false;
            accumulator -= K.SimDt;
        }
        foreach (var e in w.Events)
        {
            if (!silent) audio.Play(e.Sound, e.Pos, camPos);
            if (!silent && save.Shake) shake = MathF.Max(shake, e.Shake);
        }
        w.Events.Clear();
    }

    void FollowPlayer(float dt, bool snap)
    {
        var w = world!;
        var p = w.Player;
        var target = p != null ? p.Pos + new Vector2(0, -24) : new Vector2(w.WidthPx / 2, w.HeightPx / 2);
        if (p is { Dead: false, Won: false } && screen == Screen.Play)
        {
            var aim = Raylib.GetScreenToWorld2D(UI.Mouse * K.SS, cam);
            var look = (aim - p.Pos) * 0.12f;
            if (look.Length() > 90) look = Vector2.Normalize(look) * 90;
            target += look;
        }
        target.X = w.WidthPx <= K.ScreenW ? w.WidthPx / 2 : Math.Clamp(target.X, K.ScreenW / 2f, w.WidthPx - K.ScreenW / 2f);
        target.Y = w.HeightPx <= K.ScreenH ? w.HeightPx / 2 : Math.Clamp(target.Y, K.ScreenH / 2f, w.HeightPx - K.ScreenH / 2f);
        camPos = snap ? target : Lerp(camPos, target, 1 - MathF.Exp(-dt * 7));
    }

    void UpdateCam(float dt)
    {
        shake = MathF.Max(0, shake - dt * 40);
        var off = shake > 0 ? new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * shake * 2 : Vector2.Zero;
        cam = new Camera2D
        {
            Offset = new Vector2(K.ScreenW / 2f, K.ScreenH / 2f) * K.SS + off * K.SS,
            Target = camPos,
            Rotation = 0,
            Zoom = K.SS
        };
    }

    // ================================================================ level flow

    void StartLevel(int idx)
    {
        Go(() => { level = idx; LoadLevel(true); SetScreen(Screen.Play); });
    }

    void LoadLevel(bool fresh)
    {
        world = new World(Levels.All[level]);
        levelTime = 0;
        wonT = 0;
        if (fresh) { deaths = 0; showIntro = true; hintT = 0; stateT = 0; }
        else showIntro = false;
        paused = false;
        accumulator = 0;
        FollowPlayer(0, true);
        UpdateCam(0);
    }

    void CompleteLevel()
    {
        float best = save.Best[level];
        newBest = best <= 0 || levelTime < best;
        if (newBest) save.Best[level] = levelTime;
        save.Unlocked = Math.Clamp(Math.Max(save.Unlocked, level + 2), 1, Levels.All.Length);
        save.Store();
        SetScreen(Screen.Complete);
    }

    void NextLevel()
    {
        if (level + 1 >= Levels.All.Length) Go(() => SetScreen(Screen.Ending));
        else StartLevel(level + 1);
    }

    int ContinueLevel => Math.Min(save.Unlocked, Levels.All.Length) - 1;

    // ================================================================ draw

    void Draw()
    {
        bool playing = screen is Screen.Play or Screen.Complete && world != null;
        var shown = playing ? world! : menuWorld;
        Vector2 eye = shown.Player is { Dead: false } p
            ? p.Pos + new Vector2(0, -10)
            : camPos + new Vector2(MathF.Sin(time * 0.35f) * 420, -60 + MathF.Cos(time * 0.27f) * 140);

        renderer.PrepareCones(shown, cam);

        Raylib.BeginTextureMode(canvas);
        Raylib.ClearBackground(Palette.Ink);
        Raylib.BeginMode2D(cam);
        renderer.DrawScene(shown, cam, eye);
        if (screen == Screen.Play && !paused && world?.Player is { } pl) renderer.DrawAimPreview(world, pl);
        Raylib.EndMode2D();

        Raylib.BeginMode2D(new Camera2D { Zoom = K.SS });
        Vignette();
        switch (screen)
        {
            case Screen.Menu: DrawMenu(); break;
            case Screen.Select: DrawSelect(); break;
            case Screen.Options: DrawOptions(); break;
            case Screen.Controls: DrawControls(); break;
            case Screen.Play:
                DrawHud();
                if (paused) DrawPause();
                else DrawCrosshair();
                break;
            case Screen.Complete: DrawHud(); DrawComplete(); break;
            case Screen.Ending: DrawEnding(); break;
        }
        if (fade > 0) Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 11, (int)(255 * SmoothStep(fade))));
        Raylib.EndMode2D();
        Raylib.EndTextureMode();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        Raylib.DrawTexturePro(canvas.Texture, new Rectangle(0, 0, K.CanvasW, -K.CanvasH), dest, Vector2.Zero, 0, Color.White);
        Raylib.EndDrawing();
    }

    static void Vignette()
    {
        Raylib.DrawRectangleGradientV(0, 0, K.ScreenW, 120, C(0, 0, 0, 120), C(0, 0, 0, 0));
        Raylib.DrawRectangleGradientV(0, K.ScreenH - 120, K.ScreenW, 120, C(0, 0, 0, 0), C(0, 0, 0, 130));
        Raylib.DrawRectangleGradientH(0, 0, 140, K.ScreenH, C(0, 0, 0, 100), C(0, 0, 0, 0));
        Raylib.DrawRectangleGradientH(K.ScreenW - 140, 0, 140, K.ScreenH, C(0, 0, 0, 0), C(0, 0, 0, 100));
    }

    // ---------------------------------------------------------------- menus

    void Title(Vector2 pos, float size)
    {
        float sp = size * 0.08f;
        var m = UI.Measure("PORTAL", size, true, sp);
        UI.TextShadow("PORTAL", pos, size, Palette.White, bold: true, spacing: sp);
        UI.TextShadow("2D", pos + new Vector2(m.X + size * 0.12f, 0), size, Palette.Orange, bold: true, spacing: sp);
        // a little portal pair underline
        float y = pos.Y + size * 0.98f;
        Raylib.DrawRectangleRounded(new Rectangle(pos.X + 4, y, m.X * 0.45f, 5), 1, 4, Palette.Blue);
        Raylib.DrawRectangleRounded(new Rectangle(pos.X + 4 + m.X * 0.5f, y, m.X * 0.45f, 5), 1, 4, Palette.Orange);
    }

    void DrawMenu()
    {
        Raylib.DrawRectangleGradientH(0, 0, 820, K.ScreenH, C(8, 10, 14, 240), C(8, 10, 14, 0));
        float intro = EaseOutCubic(stateT * 2);
        float x = 84 - (1 - intro) * 40;
        Title(new Vector2(x, 92), 118);
        UI.Text("A SIDE-SCROLLING TEST OF SPATIAL REASONING", new Vector2(x + 4, 238), 19, Palette.UiDim, bold: true, spacing: 3.5f);

        float y = 300, h = 58, gap = 12;
        int cont = ContinueLevel;
        bool started = save.Unlocked > 1 || save.Best[0] > 0;
        string sub = $"Chamber {cont + 1:00}  -  {Levels.All[cont].Name}";
        if (UI.Button("play", new Rectangle(x, y, 360, h), started ? "CONTINUE" : "BEGIN TESTING", accent: Palette.Orange, sub: sub)) StartLevel(cont);
        y += h + gap;
        if (UI.Button("select", new Rectangle(x, y, 360, h), "SELECT CHAMBER")) Go(() => SetScreen(Screen.Select));
        y += h + gap;
        if (UI.Button("options", new Rectangle(x, y, 360, h), "OPTIONS")) Go(() => SetScreen(Screen.Options));
        y += h + gap;
        if (UI.Button("controls", new Rectangle(x, y, 360, h), "CONTROLS")) Go(() => SetScreen(Screen.Controls));
        y += h + gap;
        if (UI.Button("quit", new Rectangle(x, y, 360, h), "QUIT", accent: C(255, 90, 80))) quit = true;

        int done = save.Best.Count(b => b > 0);
        UI.Text($"{done} / {Levels.All.Length} CHAMBERS COMPLETE", new Vector2(x + 4, K.ScreenH - 44), 16, Palette.UiDim, bold: true, spacing: 2);
        UI.Text("v1.0  |  C# + raylib", new Vector2(K.ScreenW - 24, K.ScreenH - 44), 16, Palette.UiDim.WithAlpha(0.6f), UI.Align.Right, bold: true, spacing: 1.5f);
    }

    void Header(string title, string sub)
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(8, 10, 14, 200));
        UI.Text(title, new Vector2(84, 44), 54, Palette.White, bold: true, spacing: 4);
        UI.Text(sub, new Vector2(88, 104), 18, Palette.UiDim, bold: true, spacing: 2.5f);
        if (UI.SmallButton("back", new Rectangle(K.ScreenW - 84 - 150, 56, 150, 44), "BACK")) Go(() => SetScreen(Screen.Menu));
    }

    void DrawSelect()
    {
        int done = save.Best.Count(b => b > 0);
        Header("SELECT CHAMBER", $"{done} OF {Levels.All.Length} COMPLETE  -  TOTAL DEATHS {save.TotalDeaths}");
        const float cw = 262, ch = 150, gx = 22, gy = 22;
        float x0 = (K.ScreenW - (4 * cw + 3 * gx)) / 2, y0 = 154;
        for (int i = 0; i < Levels.All.Length; i++)
        {
            int col = i % 4, row = i / 4;
            var r = new Rectangle(x0 + col * (cw + gx), y0 + row * (ch + gy), cw, ch);
            bool locked = i >= save.Unlocked;
            bool complete = save.Best[i] > 0;
            bool over = !locked && UI.Contains(r, UI.Mouse);
            float appear = EaseOutCubic(stateT * 3 - i * 0.05f);
            r.Y += (1 - appear) * 30 - (over ? 4 : 0);
            var bg = locked ? C(18, 21, 27, 220) : over ? C(40, 48, 60, 245) : C(26, 31, 40, 235);
            Raylib.DrawRectangleRounded(r, 0.1f, 8, bg.WithAlpha(bg.A / 255f * appear));
            var accent = complete ? Palette.Green : locked ? C(70, 76, 86) : Palette.Blue;
            Raylib.DrawRectangleRounded(new Rectangle(r.X, r.Y, 5, r.Height), 1, 4, accent.WithAlpha(appear));
            if (over) Raylib.DrawRectangleRoundedLinesEx(r, 0.1f, 8, 2, Palette.Blue.WithAlpha(0.8f));

            var textCol = locked ? C(90, 98, 110) : Palette.White;
            UI.Text($"{i + 1:00}", new Vector2(r.X + 22, r.Y + 8), 58, textCol.WithAlpha(appear), bold: true, spacing: 2);
            UI.Text(locked ? "LOCKED" : Levels.All[i].Name.ToUpperInvariant(), new Vector2(r.X + 22, r.Y + 76), 24, textCol.WithAlpha(appear), bold: true, spacing: 1.5f);
            string status = locked ? "Complete the previous chamber" : complete ? $"BEST  {UI.FormatTime(save.Best[i])}" : "NOT YET COMPLETED";
            UI.Text(status, new Vector2(r.X + 22, r.Y + 110), 17, (complete ? Palette.Green : Palette.UiDim).WithAlpha(appear), bold: true, spacing: 1.5f);
            if (locked) DrawLock(new Vector2(r.X + r.Width - 40, r.Y + 34));
            else
            {
                Icons.Draw(Levels.All[i], new Vector2(r.X + r.Width - 20, r.Y + 22), 22, Palette.UiDim.WithAlpha(appear), alignRight: true);
                if (complete) DrawCheck(new Vector2(r.X + r.Width - 30, r.Y + r.Height - 30));
            }
            if (over && UI.Click) { audio.Click(); StartLevel(i); }
        }
    }

    static void DrawLock(Vector2 c)
    {
        Raylib.DrawRing(c + new Vector2(0, -6), 7, 10, 180, 360, 12, C(90, 98, 110));
        Raylib.DrawRectangleRounded(new Rectangle(c.X - 12, c.Y - 6, 24, 18), 0.3f, 4, C(90, 98, 110));
    }

    static void DrawCheck(Vector2 c)
    {
        Raylib.DrawCircleV(c, 13, Palette.Green.WithAlpha(0.2f));
        Raylib.DrawLineEx(c + new Vector2(-6, 0), c + new Vector2(-1, 5), 3, Palette.Green);
        Raylib.DrawLineEx(c + new Vector2(-1, 5), c + new Vector2(7, -5), 3, Palette.Green);
    }

    void DrawOptions()
    {
        Header("OPTIONS", "AUDIO, VISUALS AND DISPLAY");
        var panel = new Rectangle(K.ScreenW / 2f - 300, 160, 600, 500);
        UI.Panel(panel);
        float x = panel.X + 40, w = panel.Width - 80, y = panel.Y + 70;
        float m = save.Master, s = save.Sfx, a = save.Ambience;
        bool changed = false;
        changed |= UI.Slider("vol_master", new Rectangle(x, y, w, 20), "MASTER VOLUME", ref m); y += 74;
        changed |= UI.Slider("vol_sfx", new Rectangle(x, y, w, 20), "EFFECTS", ref s); y += 74;
        changed |= UI.Slider("vol_amb", new Rectangle(x, y, w, 20), "AMBIENCE", ref a); y += 46;
        if (changed)
        {
            save.Master = m; save.Sfx = s; save.Ambience = a;
            audio.Master = m; audio.SfxVolume = s; audio.AmbienceVolume = a;
            audio.ApplyVolumes();
        }
        bool cones = save.Cones, sh = save.Shake, fs = save.Fullscreen;
        if (UI.Toggle("opt_cones", new Rectangle(x - 10, y, w + 20, 54), "SEE-THROUGH PORTALS", ref cones)) { save.Cones = cones; renderer.ConesEnabled = cones; changed = true; }
        y += 64;
        if (UI.Toggle("opt_shake", new Rectangle(x - 10, y, w + 20, 54), "SCREEN SHAKE", ref sh)) { save.Shake = sh; changed = true; }
        y += 64;
        if (UI.Toggle("opt_fs", new Rectangle(x - 10, y, w + 20, 54), "FULLSCREEN  (F11)", ref fs)) { ToggleFullscreen(); }
        if (changed && !UI.MouseDown) save.Store();
        if (changed && UI.Click) save.Store();
    }

    void DrawControls()
    {
        Header("CONTROLS", "EVERYTHING YOU NEED TO KNOW");
        var panel = new Rectangle(K.ScreenW / 2f - 380, 160, 760, 500);
        UI.Panel(panel);
        (string[] keys, string what)[] rows =
        [
            (["A", "D"], "Move left / right"),
            (["W", "SPACE"], "Jump (hold for higher)"),
            (["LEFT MOUSE"], "Fire the blue portal"),
            (["RIGHT MOUSE"], "Fire the orange portal"),
            (["E"], "Pick up / drop a cube"),
            (["R"], "Restart the chamber"),
            (["H"], "Show / hide hints"),
            (["ESC"], "Pause"),
            (["F11"], "Toggle fullscreen"),
        ];
        float y = panel.Y + 34;
        foreach (var (keys, what) in rows)
        {
            float x = panel.X + 50;
            foreach (var k in keys)
            {
                UI.KeyCap(k, new Vector2(x, y));
                x += UI.KeyCapWidth(k) + 8;
            }
            UI.Text(what, new Vector2(panel.X + 330, y + 3), 24, Palette.UiText, bold: true, spacing: 1);
            y += 50;
        }
    }

    void DrawEnding()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(8, 10, 14, 215));
        float a = EaseOutCubic(stateT * 0.8f);
        UI.TextShadow("ALL TESTS COMPLETE", new Vector2(K.ScreenW / 2f, 200 - (1 - a) * 30), 76, Palette.White.WithAlpha(a), UI.Align.Center, true, 6);
        float total = save.Best.Sum();
        UI.Text($"COMBINED BEST TIME  {UI.FormatTime(total)}", new Vector2(K.ScreenW / 2f, 280), 24, Palette.Orange.WithAlpha(a), UI.Align.Center, true, 3);
        string[] lines =
        [
            "You have demonstrated an exceptional grasp of non-Euclidean locomotion.",
            "Your results have been recorded. The facility thanks you for your participation.",
            "",
            "Can you beat your best times? Every chamber has a faster route."
        ];
        for (int i = 0; i < lines.Length; i++)
            UI.Text(lines[i], new Vector2(K.ScreenW / 2f, 350 + i * 34), 22, Palette.UiText.WithAlpha(a * 0.9f), UI.Align.Center, true, 1);
        if (UI.SmallButton("end_menu", new Rectangle(K.ScreenW / 2f - 120, 560, 240, 50), "MAIN MENU", Palette.Orange)) Go(() => SetScreen(Screen.Menu));
    }

    // ---------------------------------------------------------------- in-game UI

    void DrawHud()
    {
        var w = world!;
        var def = w.Def;
        // chamber tag
        var tag = new Rectangle(18, 16, 300, 66);
        UI.Panel(tag, 0.9f);
        Raylib.DrawRectangleRounded(new Rectangle(tag.X, tag.Y + 10, 4, tag.Height - 20), 1, 4, Palette.Orange);
        UI.Text($"{level + 1:00}", new Vector2(tag.X + 18, tag.Y + 8), 46, Palette.White, bold: true, spacing: 1);
        UI.Text($"/{Levels.All.Length}", new Vector2(tag.X + 70, tag.Y + 30), 20, Palette.UiDim, bold: true);
        UI.Text("TEST CHAMBER", new Vector2(tag.X + 112, tag.Y + 12), 14, Palette.UiDim, bold: true, spacing: 2.5f);
        UI.Text(def.Name.ToUpperInvariant(), new Vector2(tag.X + 112, tag.Y + 28), 26, Palette.UiText, bold: true, spacing: 1.5f);

        // timer + portal status
        var tr = new Rectangle(K.ScreenW - 228, 16, 210, 66);
        UI.Panel(tr, 0.9f);
        UI.Text(UI.FormatTime(MathF.Max(levelTime, 0.001f)), new Vector2(tr.X + tr.Width - 18, tr.Y + 8), 34, Palette.White, UI.Align.Right, true, 1);
        UI.Text($"DEATHS {deaths}", new Vector2(tr.X + tr.Width - 18, tr.Y + 42), 15, Palette.UiDim, UI.Align.Right, true, 2);
        for (int i = 0; i < 2; i++)
        {
            var col = i == 0 ? Palette.Blue : Palette.Orange;
            var c = new Vector2(tr.X + 24 + i * 22, tr.Y + 33);
            bool placed = w.Portals[i] != null;
            bool usable = w.GunAllows((PortalColor)i) || w.Portals[i]?.Fixed == true;
            if (!usable) { Raylib.DrawEllipseLines((int)c.X, (int)c.Y, 6, 14, C(80, 86, 96)); continue; }
            if (placed) Raylib.DrawEllipse((int)c.X, (int)c.Y, 6, 14, col);
            else Raylib.DrawEllipseLines((int)c.X, (int)c.Y, 6, 14, col);
        }

        // intro card
        if (showIntro && stateT < 3.2f)
        {
            float t = stateT;
            float slide = t < 0.5f ? EaseOutCubic(t / 0.5f) : t > 2.7f ? 1 - EaseOutCubic((t - 2.7f) / 0.5f) : 1;
            DrawChamberCard(new Vector2(K.ScreenW / 2f - 210, 210 - (1 - slide) * 30), slide);
        }

        // hints
        if (def.Hints.Length > 0)
        {
            float show = hintsOn ? Clamp01((hintT - (showIntro ? 2.6f : 0.2f)) * 3) * Clamp01((16 - hintT) / 1.5f) : 0;
            if (show > 0.01f)
            {
                float maxW = def.Hints.Max(hh => UI.Measure(hh, 22, true, 0.5f).X);
                var hr = new Rectangle(K.ScreenW / 2f - maxW / 2 - 28, K.ScreenH - 40 - def.Hints.Length * 30 - 24, maxW + 56, def.Hints.Length * 30 + 24);
                hr.Y += (1 - show) * 20;
                UI.Panel(hr, show);
                Raylib.DrawRectangleRounded(new Rectangle(hr.X + 10, hr.Y + 12, 4, hr.Height - 24), 1, 4, Palette.Blue.WithAlpha(show));
                for (int i = 0; i < def.Hints.Length; i++)
                    UI.Text(def.Hints[i], new Vector2(K.ScreenW / 2f + 6, hr.Y + 26 + i * 30), 22, Palette.UiText.WithAlpha(show), UI.Align.Center, true, 0.5f);
            }
            else if (!hintsOn || hintT > 16)
                UI.Text("H  HINTS     R  RESTART     ESC  PAUSE", new Vector2(24, K.ScreenH - 34), 15, Palette.UiDim.WithAlpha(0.7f), bold: true, spacing: 2);
        }

        if (w.Player is { Dead: true } pd)
        {
            float a = Clamp01(pd.DeadTimer * 2);
            UI.TextShadow("SUBJECT TERMINATED", new Vector2(K.ScreenW / 2f, K.ScreenH / 2f - 20), 44, C(255, 90, 80, (int)(255 * a)), UI.Align.Center, true, 5);
            UI.Text("RESTARTING CHAMBER", new Vector2(K.ScreenW / 2f, K.ScreenH / 2f + 22), 18, Palette.UiDim.WithAlpha(a), UI.Align.Center, true, 3);
        }
    }

    void DrawChamberCard(Vector2 pos, float alpha)
    {
        var def = world!.Def;
        var r = new Rectangle(pos.X, pos.Y, 420, 250);
        Raylib.DrawRectangleRounded(new Rectangle(r.X + 6, r.Y + 8, r.Width, r.Height), 0.05f, 6, C(0, 0, 0, (int)(90 * alpha)));
        Raylib.DrawRectangleRounded(r, 0.05f, 6, C(236, 240, 243, (int)(250 * alpha)));
        var ink = C(30, 34, 40, (int)(255 * alpha));
        UI.Text($"{level + 1:00}", new Vector2(r.X + 30, r.Y + 14), 120, ink, bold: true, spacing: 2);
        UI.Text($"/{Levels.All.Length}", new Vector2(r.X + 172, r.Y + 78), 36, C(120, 128, 138, (int)(255 * alpha)), bold: true);
        Raylib.DrawRectangle((int)r.X + 30, (int)r.Y + 142, (int)r.Width - 60, 3, ink);
        UI.Text(def.Name.ToUpperInvariant(), new Vector2(r.X + 30, r.Y + 154), 34, ink, bold: true, spacing: 2);
        Icons.Draw(def, new Vector2(r.X + 30, r.Y + 210), 26, ink, alignRight: false, filled: true);
        Raylib.DrawRectangle((int)(r.X + r.Width - 60), (int)r.Y + 30, 30, 30, Palette.Orange.WithAlpha(alpha));
        Raylib.DrawRectangle((int)(r.X + r.Width - 60), (int)r.Y + 66, 30, 30, Palette.Blue.WithAlpha(alpha));
    }

    void DrawCrosshair()
    {
        var w = world!;
        if (w.Player is not { Dead: false }) return;
        var c = UI.Mouse;
        Raylib.DrawCircleV(c, 2, Palette.White);
        if (w.Def.Gun == GunMode.None)
        {
            Raylib.DrawRing(c, 8, 9.5f, 0, 360, 24, C(255, 255, 255, 90));
            return;
        }
        for (int i = 0; i < 2; i++)
        {
            var col = i == 0 ? Palette.Blue : Palette.Orange;
            bool placed = w.Portals[i] != null;
            float a0 = i == 0 ? 105 : -75, a1 = i == 0 ? 255 : 75;
            bool usable = w.GunAllows((PortalColor)i);
            Raylib.DrawRing(c, 10, 13.5f, a0, a1, 16, usable ? col.WithAlpha(placed ? 1f : 0.35f) : C(120, 120, 120, 80));
            if (placed) Raylib.DrawRing(c, 15.5f, 17, a0 + 20, a1 - 20, 12, col.WithAlpha(0.8f));
        }
    }

    void DrawPause()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 170));
        var r = new Rectangle(K.ScreenW / 2f - 220, K.ScreenH / 2f - 210, 440, 420);
        UI.Panel(r);
        UI.Text("PAUSED", new Vector2(K.ScreenW / 2f, r.Y + 52), 48, Palette.White, UI.Align.Center, true, 6);
        float y = r.Y + 104, x = r.X + 40, bw = r.Width - 80;
        if (UI.Button("p_resume", new Rectangle(x, y, bw, 54), "RESUME", accent: Palette.Orange)) paused = false;
        y += 66;
        if (UI.Button("p_restart", new Rectangle(x, y, bw, 54), "RESTART CHAMBER")) Go(() => LoadLevel(false));
        y += 66;
        if (UI.Button("p_select", new Rectangle(x, y, bw, 54), "SELECT CHAMBER")) Go(() => SetScreen(Screen.Select));
        y += 66;
        if (UI.Button("p_menu", new Rectangle(x, y, bw, 54), "MAIN MENU")) Go(() => SetScreen(Screen.Menu));
    }

    void DrawComplete()
    {
        float a = EaseOutCubic(stateT * 2.5f);
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, (int)(160 * a)));
        var r = new Rectangle(K.ScreenW / 2f - 280, K.ScreenH / 2f - 230 + (1 - a) * 40, 560, 460);
        UI.Panel(r, a);
        Raylib.DrawRectangleRounded(new Rectangle(r.X, r.Y, r.Width, 6), 1, 4, Palette.Green.WithAlpha(a));
        UI.Text($"TEST CHAMBER {level + 1:00}", new Vector2(K.ScreenW / 2f, r.Y + 40), 20, Palette.UiDim.WithAlpha(a), UI.Align.Center, true, 4);
        UI.Text("COMPLETE", new Vector2(K.ScreenW / 2f, r.Y + 88), 64, Palette.Green.WithAlpha(a), UI.Align.Center, true, 8);

        float y = r.Y + 140;
        void Row(string k, string v, Color vc, string? tag = null)
        {
            UI.Text(k, new Vector2(r.X + 60, y), 22, Palette.UiDim.WithAlpha(a), bold: true, spacing: 2.5f);
            UI.Text(v, new Vector2(r.X + r.Width - 60, y - 2), 28, vc.WithAlpha(a), UI.Align.Right, true, 1);
            if (tag != null)
            {
                var m = UI.Measure(v, 28, true, 1);
                var tr = new Rectangle(r.X + r.Width - 60 - m.X - 104, y, 92, 24);
                Raylib.DrawRectangleRounded(tr, 0.5f, 6, Palette.Orange.WithAlpha(a));
                UI.Text(tag, new Vector2(tr.X + tr.Width / 2, tr.Y + 12), 15, Palette.Ink, UI.Align.Center, true, 1.5f);
            }
            Raylib.DrawRectangle((int)r.X + 60, (int)y + 34, (int)r.Width - 120, 1, C(255, 255, 255, (int)(20 * a)));
            y += 46;
        }
        Row("TIME", UI.FormatTime(levelTime), Palette.White, newBest ? "NEW BEST" : null);
        Row("BEST", UI.FormatTime(save.Best[level]), Palette.UiText);
        Row("PORTALS PLACED", world!.PortalsPlaced.ToString(), Palette.UiText);
        Row("DEATHS", deaths.ToString(), deaths == 0 ? Palette.Green : Palette.UiText);

        bool last = level + 1 >= Levels.All.Length;
        float by = r.Y + r.Height - 84;
        if (UI.SmallButton("c_next", new Rectangle(r.X + r.Width - 60 - 220, by, 220, 50), last ? "FINISH" : "NEXT  (ENTER)", Palette.Green)) NextLevel();
        if (UI.SmallButton("c_retry", new Rectangle(r.X + 60, by, 120, 50), "REPLAY")) StartLevel(level);
        if (UI.SmallButton("c_menu", new Rectangle(r.X + 192, by, 100, 50), "MENU")) Go(() => SetScreen(Screen.Menu));
    }
}
