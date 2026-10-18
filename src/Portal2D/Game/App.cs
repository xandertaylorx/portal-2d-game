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
    readonly SaveData save = SaveData.Load(Levels.All.Select(l => l.Name).ToArray());
    readonly Audio audio = new();
    readonly Updater updater = new();
    Renderer renderer = null!;
    RenderTexture2D canvas;
    readonly World menuWorld = new(Levels.Menu);
    World? world;
    int level;
    bool randomMode;
    Procgen.Result? random;
    int randomSeed, streak;
    LevelDef CurrentDef => randomMode ? random!.Def : Levels.All[level];

    Camera2D cam;
    Vector2 camPos;
    float shake;
    readonly Random rng = new();

    bool paused, quit, showIntro;
    float time, stateT, levelTime, wonT, hintT, hintEnd = 10, accumulator;
    Vector2 startPos;
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
        Sprites.Upload();
        audio.Load();
        audio.Master = save.Master; audio.SfxVolume = save.Sfx; audio.AmbienceVolume = save.Ambience;
        audio.ApplyVolumes();
        UI.OnHover = audio.Hover;
        UI.OnClick = audio.Click;
        renderer = new Renderer { WindowsEnabled = save.Cones };
        canvas = Raylib.LoadRenderTexture(K.CanvasW, K.CanvasH);
        Raylib.SetTextureFilter(canvas.Texture, TextureFilter.Point);
        if (save.Fullscreen) Raylib.ToggleBorderlessWindowed();

        camPos = new Vector2(menuWorld.WidthPx / 2, menuWorld.HeightPx / 2);
        if (save.CheckUpdates && shotArgs.Length == 0) updater.CheckAsync();
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
        Sprites.Unload();
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
        string? script = i + 4 < shotArgs.Length && !shotArgs[i + 4].StartsWith("--") ? shotArgs[i + 4] : null;
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
        else if (what.StartsWith("random:")) { randomMode = true; randomSeed = int.Parse(what[7..]); random = Procgen.Generate(randomSeed, 4); LoadLevel(true); screen = Screen.Play; }
        fade = 0; fadeDir = 0;
        IEnumerator<InputState>? bot = null;
        if (script == "solve" && world != null)
        {
            var b = new Testing.Bot(world);
            bot = (randomMode ? random!.Solution(b) : Testing.Solutions.For(level, b)).GetEnumerator();
        }
        int ri = Array.IndexOf(shotArgs, "--rec");
        string? recDir = ri >= 0 ? shotArgs[ri + 1] : null;
        float recStart = ri >= 0 ? float.Parse(shotArgs[ri + 2], System.Globalization.CultureInfo.InvariantCulture) : 0;
        int recCount = 0;
        if (recDir != null) { Directory.CreateDirectory(recDir); showIntro = false; hintsOn = false; }
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
            if (recDir != null && f >= recStart * 60 && f % 2 == 0)
            {
                var fimg = Raylib.LoadImageFromTexture(canvas.Texture);
                Raylib.ImageFlipVertical(ref fimg);
                Raylib.ExportImage(fimg, Path.Combine(recDir, $"f{recCount++:0000}.png"));
                Raylib.UnloadImage(fimg);
            }
        }
        Raylib.BeginDrawing();
        Raylib.EndDrawing();
        var img = Raylib.LoadImageFromTexture(canvas.Texture);
        Raylib.ImageFlipVertical(ref img);
        Raylib.ImageResizeNN(ref img, K.ScreenW, K.ScreenH);
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
        float fit = MathF.Min(sw / (float)K.CanvasW, sh / (float)K.CanvasH);
        float px = fit >= 1 ? MathF.Floor(fit) : fit;           // integer pixel scale when possible
        float dw = K.CanvasW * px, dh = K.CanvasH * px;
        float scale = dw / K.ScreenW;
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
                if (Raylib.IsKeyPressed(KeyboardKey.H))
                {
                    bool visible = hintsOn && hintT < hintEnd;
                    hintsOn = !visible;
                    if (hintsOn) { hintT = showIntro ? 3.3f : 0; hintEnd = hintT + 8; }
                }
                // once the player gets going, let the hints get out of the way
                if (w.Player is { } hp && hintT > 4 && hintEnd > hintT + 1.2f && Vector2.Distance(hp.Pos, startPos) > 220)
                    hintEnd = hintT + 1.2f;
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
            AimWorld = Raylib.GetScreenToWorld2D(UI.Mouse * K.Px, cam)
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
            var aim = Raylib.GetScreenToWorld2D(UI.Mouse * K.Px, cam);
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
            Offset = new Vector2(K.CanvasW / 2, K.CanvasH / 2) + new Vector2(MathF.Round(off.X * K.Px), MathF.Round(off.Y * K.Px)),
            Target = new Vector2(MathF.Round(camPos.X * K.Px), MathF.Round(camPos.Y * K.Px)) / K.Px,
            Rotation = 0,
            Zoom = K.Px
        };
    }

    // ================================================================ level flow

    void StartLevel(int idx)
    {
        Go(() => { randomMode = false; level = idx; LoadLevel(true); SetScreen(Screen.Play); });
    }

    void StartRandom(int seed, bool newRun)
    {
        Go(() =>
        {
            if (newRun) streak = 0;
            randomMode = true;
            randomSeed = seed;
            random = Procgen.Generate(seed, streak);
            LoadLevel(true);
            SetScreen(Screen.Play);
        });
    }

    void LoadLevel(bool fresh)
    {
        world = new World(CurrentDef);
        levelTime = 0;
        wonT = 0;
        startPos = world.Player?.Pos ?? Vector2.Zero;
        hintEnd = fresh ? 11f : 0;
        if (fresh) { deaths = 0; showIntro = true; hintT = 0; stateT = 0; }
        else showIntro = false;
        paused = false;
        accumulator = 0;
        FollowPlayer(0, true);
        UpdateCam(0);
    }

    void CompleteLevel()
    {
        if (randomMode)
        {
            streak++;
            save.RandomCleared++;
            save.RandomBestStreak = Math.Max(save.RandomBestStreak, streak);
            newBest = false;
        }
        else
        {
            float best = save.Best[level];
            newBest = best <= 0 || levelTime < best;
            if (newBest) save.Best[level] = levelTime;
        }
        save.Store();
        SetScreen(Screen.Complete);
    }

    void NextLevel()
    {
        if (randomMode) StartRandom(randomSeed + 7919, newRun: false);
        else if (level + 1 >= Levels.All.Length) Go(() => SetScreen(Screen.Ending));
        else StartLevel(level + 1);
    }

    /// <summary>The first chamber you haven't finished yet (or the last one).</summary>
    int ContinueLevel
    {
        get
        {
            for (int i = 0; i < Levels.All.Length; i++) if (save.Best[i] <= 0) return i;
            return Levels.All.Length - 1;
        }
    }

    bool Unlocked(int i) => i == 0 || save.Best[i - 1] > 0 || save.Best[i] > 0;

    // ================================================================ draw

    void Draw()
    {
        bool playing = screen is Screen.Play or Screen.Complete && world != null;
        var shown = playing ? world! : menuWorld;
        Vector2 eye = shown.Player is { Dead: false } p
            ? p.Pos + new Vector2(0, -10)
            : camPos + new Vector2(MathF.Sin(time * 0.35f) * 420, -60 + MathF.Cos(time * 0.27f) * 140);

        renderer.PrepareWindows(shown, cam);

        Raylib.BeginTextureMode(canvas);
        Raylib.ClearBackground(PP.Ink);
        Raylib.BeginMode2D(cam);
        renderer.DrawScene(shown, cam, eye);
        if (screen == Screen.Play && !paused && world?.Player is { } pl) renderer.DrawAimPreview(world, pl);
        Raylib.EndMode2D();

        Raylib.BeginMode2D(new Camera2D { Zoom = K.Px });
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
        // force the canvas alpha to opaque (translucent overlays reduce destination alpha)
        Rlgl.SetBlendFactorsSeparate(0, 1, 1, 0, 0x8006, 0x8006);
        Raylib.BeginBlendMode(BlendMode.CustomSeparate);
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, Color.Black);
        Raylib.EndBlendMode();
        Raylib.EndMode2D();
        Raylib.EndTextureMode();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        Raylib.DrawTexturePro(canvas.Texture, new Rectangle(0, 0, K.CanvasW, -K.CanvasH), dest, Vector2.Zero, 0, Color.White);
        Raylib.EndDrawing();
    }

    static void Vignette()
    {
        Raylib.DrawRectangleGradientV(0, 0, K.ScreenW, 100, C(0, 0, 0, 90), C(0, 0, 0, 0));
        Raylib.DrawRectangleGradientV(0, K.ScreenH - 100, K.ScreenW, 100, C(0, 0, 0, 0), C(0, 0, 0, 110));
    }

    static readonly Color Dim = C(150, 158, 172);

    // ---------------------------------------------------------------- menus

    void Title(Vector2 pos)
    {
        UI.TextShadow("PORTAL", pos, 130, PP.CreamHi);
        var m = UI.Measure("PORTAL", 130);
        UI.TextShadow("2D", pos + new Vector2(m.X + 36, 0), 130, PP.OrangeRamp[2]);
        float y = pos.Y + m.Y + 18;
        Raylib.DrawRectangle((int)pos.X, (int)y, (int)(m.X * 0.48f), 6, PP.BlueRamp[2]);
        Raylib.DrawRectangle((int)(pos.X + m.X * 0.52f), (int)y, (int)(m.X * 0.48f), 6, PP.OrangeRamp[2]);
    }

    /// <summary>The hero, big, for menus.</summary>
    void Mascot(Vector2 feet, int scale, bool facingLeft = false)
    {
        int frame = (time % 3.4f) < 0.14f ? 1 : 0;
        float bob = MathF.Round(MathF.Sin(time * 3) * 1) * scale * 2;
        var src = new Rectangle(frame * 16, 0, facingLeft ? -16 : 16, 16);
        float size = 16 * scale * 2;
        Raylib.DrawTexturePro(Sprites.PlayerTex, src, new Rectangle(feet.X - size / 2, feet.Y - size + bob, size, size), Vector2.Zero, 0, Color.White);
        Raylib.DrawRectangle((int)(feet.X - size * 0.4f), (int)feet.Y + 4, (int)(size * 0.8f), 6, C(0, 0, 0, 90));
    }

    void DrawMenu()
    {
        Raylib.DrawRectangleGradientH(0, 0, 900, K.ScreenH, C(10, 11, 16, 245), C(10, 11, 16, 0));
        float intro = EaseOutCubic(stateT * 2);
        float x = MathF.Round(80 - (1 - intro) * 40);
        Title(new Vector2(x, 60));
        UI.Text("A SIDE-SCROLLING TEST OF SPATIAL REASONING", new Vector2(x + 2, 214), 16, Dim);
        Mascot(new Vector2(x + 990, 196), 4, facingLeft: true);

        float y = 262, h = 60, gap = 10, bw = 440;
        int cont = ContinueLevel;
        bool started = save.Best.Any(b => b > 0);
        string sub = $"CHAMBER {cont + 1:00} - {Levels.All[cont].Name}";
        if (UI.Button("play", new Rectangle(x, y, bw, h), started ? "CONTINUE" : "BEGIN TESTING", accent: PP.OrangeRamp[2], sub: sub)) StartLevel(cont);
        y += h + gap;
        string rsub = save.RandomBestStreak > 0 ? $"ENDLESS GENERATED CHAMBERS - BEST STREAK {save.RandomBestStreak}" : "ENDLESS GENERATED CHAMBERS";
        if (UI.Button("random", new Rectangle(x, y, bw, h), "RANDOM TEST", accent: PP.Exit, sub: rsub))
            StartRandom(Environment.TickCount & 0xFFFFF, newRun: true);
        y += h + gap;
        if (UI.Button("select", new Rectangle(x, y, bw, h), "SELECT CHAMBER")) Go(() => SetScreen(Screen.Select));
        y += h + gap;
        if (UI.Button("options", new Rectangle(x, y, bw, h), "OPTIONS")) Go(() => SetScreen(Screen.Options));
        y += h + gap;
        if (UI.Button("controls", new Rectangle(x, y, bw, h), "CONTROLS")) Go(() => SetScreen(Screen.Controls));
        y += h + gap;
        if (UI.Button("quit", new Rectangle(x, y, bw, h), "QUIT", accent: C(255, 90, 80))) quit = true;

        int done = save.Best.Count(b => b > 0);
        UI.Text($"{done}/{Levels.All.Length} CHAMBERS COMPLETE", new Vector2(x + 2, K.ScreenH - 30), 16, Dim);
        UI.Text($"V{Updater.CurrentVersion}  C# + RAYLIB", new Vector2(K.ScreenW - 24, K.ScreenH - 30), 16, Dim.WithAlpha(0.6f), UI.Align.Right);
        DrawUpdateBanner();
    }

    void DrawUpdateBanner()
    {
        var st = updater.Status;
        if (st is not (Updater.State.Available or Updater.State.Downloading or Updater.State.Ready)
            && !(st == Updater.State.Failed && updater.LatestVersion.Length > 0)) return;
        var r = new Rectangle(K.ScreenW - 24 - 440, K.ScreenH - 160, 440, 112);
        UI.Panel(r, 1, PP.Exit);
        UI.Text($"UPDATE AVAILABLE  V{updater.LatestVersion}", new Vector2(r.X + 20, r.Y + 18), 16, PP.Exit);
        UI.Text($"YOU HAVE V{Updater.CurrentVersion}", new Vector2(r.X + 20, r.Y + 40), 16, Dim);
        var br = new Rectangle(r.X + 20, r.Y + 62, r.Width - 40, 36);
        switch (st)
        {
            case Updater.State.Available:
                if (UI.SmallButton("upd_go", br, "DOWNLOAD UPDATE", PP.Exit)) updater.DownloadAsync();
                break;
            case Updater.State.Downloading:
                Raylib.DrawRectangleRec(br, C(30, 34, 44));
                Raylib.DrawRectangle((int)br.X, (int)br.Y, (int)(br.Width * updater.Progress), (int)br.Height, PP.Exit);
                UI.Text($"DOWNLOADING {(int)(updater.Progress * 100)}%", new Vector2(br.X + br.Width / 2, br.Y + br.Height / 2), 16, PP.CreamHi, UI.Align.Center, bold: true);
                break;
            case Updater.State.Ready:
                if (UI.SmallButton("upd_restart", br, "RESTART & UPDATE", PP.Exit))
                {
                    save.Store();
                    if (updater.LaunchSwapAndQuit()) quit = true;
                }
                break;
            default:
                UI.Text("UPDATE FAILED - " + updater.Error.ToUpperInvariant()[..Math.Min(34, updater.Error.Length)], new Vector2(r.X + 20, r.Y + 70), 16, C(255, 110, 100));
                break;
        }
    }

    void Header(string title, string sub)
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(10, 11, 16, 215));
        UI.TextShadow(title, new Vector2(66, 40), 46, PP.CreamHi);
        UI.Text(sub, new Vector2(68, 98), 16, Dim);
        if (UI.SmallButton("back", new Rectangle(K.ScreenW - 66 - 150, 44, 150, 44), "BACK")) Go(() => SetScreen(Screen.Menu));
    }

    void DrawSelect()
    {
        int done = save.Best.Count(b => b > 0);
        Header("SELECT CHAMBER", $"{done} OF {Levels.All.Length} COMPLETE   TOTAL DEATHS {save.TotalDeaths}");
        const int cols = 6;
        const float cw = 180, ch = 132, gx = 14, gy = 14;
        float x0 = MathF.Round((K.ScreenW - (cols * cw + (cols - 1) * gx)) / 2), y0 = 136;
        for (int i = 0; i < Levels.All.Length; i++)
        {
            int col = i % cols, row = i / cols;
            var r = new Rectangle(x0 + col * (cw + gx), y0 + row * (ch + gy), cw, ch);
            bool locked = !Unlocked(i);
            bool complete = save.Best[i] > 0;
            bool over = !locked && UI.Contains(r, UI.Mouse);
            if (stateT * 4 - i * 0.12f < 0) continue;
            if (over) r.Y -= 4;
            var accent = complete ? PP.Exit : locked ? C(70, 76, 86) : PP.BlueRamp[2];
            UI.Panel(r, 1, over ? PP.BlueRamp[2] : null);
            Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 2, 4, (int)r.Height - 4, accent);
            var textCol = locked ? C(90, 98, 110) : PP.CreamHi;
            UI.Text($"{i + 1:00}", new Vector2(r.X + 18, r.Y + 14), 46, textCol);
            string name = locked ? "LOCKED" : Levels.All[i].Name;
            var words = name.ToUpperInvariant().Split(' ');
            string l1 = "", l2 = "";
            foreach (var wd in words)
            {
                if (l2.Length == 0 && UI.Measure((l1 + " " + wd).Trim(), 16).X < cw - 30) l1 = (l1 + " " + wd).Trim();
                else l2 = (l2 + " " + wd).Trim();
            }
            UI.Text(l1, new Vector2(r.X + 18, r.Y + 68), 16, textCol);
            if (l2.Length > 0) UI.Text(l2, new Vector2(r.X + 18, r.Y + 86), 16, textCol);
            string status = locked ? "" : complete ? UI.FormatTime(save.Best[i]) : "NEW";
            UI.Text(status, new Vector2(r.X + 18, r.Y + ch - 24), 16, complete ? PP.Exit : PP.OrangeRamp[2]);
            if (locked) DrawLock(new Vector2(r.X + cw - 34, r.Y + 30));
            else if (complete) DrawCheck(new Vector2(r.X + cw - 30, r.Y + 30));
            if (over && UI.Click) { audio.Click(); StartLevel(i); }
        }
        float by = y0 + 3 * (ch + gy) + 6;
        string rs = save.RandomBestStreak > 0 ? $"BEST STREAK {save.RandomBestStreak}   CLEARED {save.RandomCleared}" : "PROCEDURALLY GENERATED - NEVER THE SAME TWICE";
        if (UI.Button("sel_random", new Rectangle(x0, by, cols * cw + (cols - 1) * gx, 60), "RANDOM TEST", accent: PP.Exit, sub: rs))
            StartRandom(Environment.TickCount & 0xFFFFF, newRun: true);
    }

    static void DrawLock(Vector2 c)
    {
        var col = C(90, 98, 110);
        Raylib.DrawRectangle((int)c.X - 8, (int)c.Y - 14, 4, 10, col);
        Raylib.DrawRectangle((int)c.X + 4, (int)c.Y - 14, 4, 10, col);
        Raylib.DrawRectangle((int)c.X - 6, (int)c.Y - 18, 12, 4, col);
        Raylib.DrawRectangle((int)c.X - 12, (int)c.Y - 4, 24, 18, col);
        Raylib.DrawRectangle((int)c.X - 2, (int)c.Y + 2, 4, 6, C(30, 34, 44));
    }

    static void DrawCheck(Vector2 c)
    {
        for (int i = 0; i < 4; i++) Raylib.DrawRectangle((int)c.X - 10 + i * 4, (int)c.Y + i * 4 - 4, 4, 4, PP.Exit);
        for (int i = 0; i < 5; i++) Raylib.DrawRectangle((int)c.X + 2 + i * 4, (int)c.Y + 4 - i * 4, 4, 4, PP.Exit);
    }

    void DrawOptions()
    {
        Header("OPTIONS", "AUDIO, VISUALS AND DISPLAY");
        var panel = new Rectangle(K.ScreenW / 2f - 300, 140, 600, 560);
        UI.Panel(panel);
        float x = panel.X + 40, w = panel.Width - 80, y = panel.Y + 60;
        float m = save.Master, s = save.Sfx, a = save.Ambience;
        bool changed = false;
        changed |= UI.Slider("vol_master", new Rectangle(x, y, w, 20), "MASTER VOLUME", ref m); y += 74;
        changed |= UI.Slider("vol_sfx", new Rectangle(x, y, w, 20), "EFFECTS", ref s); y += 74;
        changed |= UI.Slider("vol_amb", new Rectangle(x, y, w, 20), "AMBIENCE", ref a); y += 50;
        if (changed)
        {
            save.Master = m; save.Sfx = s; save.Ambience = a;
            audio.Master = m; audio.SfxVolume = s; audio.AmbienceVolume = a;
            audio.ApplyVolumes();
        }
        bool cones = save.Cones, sh = save.Shake, fs = save.Fullscreen;
        if (UI.Toggle("opt_cones", new Rectangle(x - 10, y, w + 20, 54), "PORTAL WINDOWS", ref cones)) { save.Cones = cones; renderer.WindowsEnabled = cones; changed = true; }
        y += 66;
        if (UI.Toggle("opt_shake", new Rectangle(x - 10, y, w + 20, 54), "SCREEN SHAKE", ref sh)) { save.Shake = sh; changed = true; }
        y += 66;
        if (UI.Toggle("opt_fs", new Rectangle(x - 10, y, w + 20, 54), "FULLSCREEN (F11)", ref fs)) ToggleFullscreen();
        y += 66;
        bool cu = save.CheckUpdates;
        if (UI.Toggle("opt_upd", new Rectangle(x - 10, y, w - 170, 54), "CHECK FOR UPDATES", ref cu)) { save.CheckUpdates = cu; changed = true; }
        if (UI.SmallButton("opt_updnow", new Rectangle(x + w - 150, y + 6, 160, 42), "CHECK NOW")) updater.CheckAsync();
        string us = updater.Status switch
        {
            Updater.State.Checking => "CHECKING...",
            Updater.State.UpToDate => "UP TO DATE",
            Updater.State.Available => $"V{updater.LatestVersion} AVAILABLE - SEE MAIN MENU",
            Updater.State.Disabled => "DEVELOPMENT BUILD - UPDATES OFF",
            Updater.State.Failed => "COULD NOT REACH UPDATE SERVER",
            _ => ""
        };
        UI.Text($"VERSION {Updater.CurrentVersion}   {us}", new Vector2(x, y + 64), 16, Dim);
        if (changed && !UI.MouseDown) save.Store();
    }

    void DrawControls()
    {
        Header("CONTROLS", "EVERYTHING YOU NEED TO KNOW");
        var panel = new Rectangle(K.ScreenW / 2f - 380, 150, 760, 500);
        UI.Panel(panel);
        (string[] keys, string what)[] rows =
        [
            (["A", "D"], "MOVE LEFT / RIGHT"),
            (["W", "SPACE"], "JUMP (HOLD FOR HIGHER)"),
            (["LEFT MOUSE"], "FIRE THE BLUE PORTAL"),
            (["RIGHT MOUSE"], "FIRE THE ORANGE PORTAL"),
            (["E"], "PICK UP / DROP A CUBE"),
            (["R"], "RESTART THE CHAMBER"),
            (["H"], "SHOW / HIDE HINTS"),
            (["ESC"], "PAUSE"),
            (["F11"], "TOGGLE FULLSCREEN"),
        ];
        float y = panel.Y + 30;
        foreach (var (keys, what) in rows)
        {
            float x = panel.X + 46;
            foreach (var k in keys)
            {
                UI.KeyCap(k, new Vector2(x, y));
                x += UI.KeyCapWidth(k) + 8;
            }
            UI.Text(what, new Vector2(panel.X + 330, y + 9), 16, PP.Cream);
            y += 50;
        }
    }

    void DrawEnding()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(10, 11, 16, 220));
        float a = Clamp01(stateT * 1.5f);
        UI.TextShadow("ALL TESTS COMPLETE", new Vector2(K.ScreenW / 2f, 170), 60, PP.CreamHi.WithAlpha(a), UI.Align.Center);
        UI.Text($"COMBINED BEST TIME {UI.FormatTime(save.Best.Sum())}", new Vector2(K.ScreenW / 2f, 250), 30, PP.OrangeRamp[2].WithAlpha(a), UI.Align.Center);
        string[] lines =
        [
            "YOU HAVE DEMONSTRATED AN EXCEPTIONAL GRASP",
            "OF NON-EUCLIDEAN LOCOMOTION.",
            "",
            "THE RANDOM TEST NEVER ENDS. HOW LONG CAN YOUR STREAK GO?"
        ];
        for (int i = 0; i < lines.Length; i++)
            UI.Text(lines[i], new Vector2(K.ScreenW / 2f, 320 + i * 30), 16, PP.Cream.WithAlpha(a), UI.Align.Center);
        Mascot(new Vector2(K.ScreenW / 2f, 540), 3);
        if (UI.SmallButton("end_random", new Rectangle(K.ScreenW / 2f - 250, 590, 240, 50), "RANDOM TEST", PP.Exit))
            StartRandom(Environment.TickCount & 0xFFFFF, newRun: true);
        if (UI.SmallButton("end_menu", new Rectangle(K.ScreenW / 2f + 10, 590, 240, 50), "MAIN MENU", PP.OrangeRamp[2])) Go(() => SetScreen(Screen.Menu));
    }

    // ---------------------------------------------------------------- in-game UI

    void DrawHud()
    {
        var w = world!;
        var def = w.Def;
        string num = randomMode ? $"{streak + 1:00}" : $"{level + 1:00}";
        string label = randomMode ? $"RANDOM TEST  SEED {randomSeed}" : "TEST CHAMBER";
        string name = randomMode ? "STREAK" : def.Name.ToUpperInvariant();
        var numM = UI.Measure(num, 46);
        float nameX = 34 + numM.X + 24;
        float tagW = MathF.Max(UI.Measure(name, 30).X, UI.Measure(label, 16).X) + nameX + 24 - 16;
        var tag = new Rectangle(16, 14, tagW, 74);
        UI.Panel(tag, 0.92f);
        Raylib.DrawRectangle((int)tag.X + 2, (int)tag.Y + 2, 4, (int)tag.Height - 4, randomMode ? PP.Exit : PP.OrangeRamp[2]);
        UI.Text(num, new Vector2(34, 30), 46, PP.CreamHi, bold: true);
        UI.Text(label, new Vector2(nameX, 26), 16, Dim);
        UI.Text(name, new Vector2(nameX, 48), 30, PP.CreamHi, bold: true);

        var tr = new Rectangle(K.ScreenW - 16 - 250, 14, 250, 74);
        UI.Panel(tr, 0.92f);
        UI.Text(UI.FormatTime(MathF.Max(levelTime, 0.001f)), new Vector2(tr.X + tr.Width - 18, tr.Y + 14), 30, PP.CreamHi, UI.Align.Right, true);
        UI.Text($"DEATHS {deaths}", new Vector2(tr.X + tr.Width - 18, tr.Y + 50), 16, Dim, UI.Align.Right);
        for (int i = 0; i < 2; i++)
        {
            var col = i == 0 ? PP.BlueRamp[2] : PP.OrangeRamp[2];
            int cx = (int)tr.X + 18 + i * 16, cy = (int)tr.Y + 52;
            bool placed = w.Portals[i] != null;
            bool usable = w.GunAllows((PortalColor)i) || w.Portals[i]?.Fixed == true;
            var c = usable ? col : C(70, 76, 86);
            Raylib.DrawRectangle(cx, cy - 10, 10, 20, c);
            if (!placed) Raylib.DrawRectangle(cx + 2, cy - 8, 6, 16, C(18, 20, 27));
        }

        if (showIntro && stateT < 3.2f)
        {
            float t = stateT;
            float slide = t < 0.4f ? EaseOutCubic(t / 0.4f) : t > 2.8f ? 1 - EaseOutCubic((t - 2.8f) / 0.4f) : 1;
            DrawChamberCard(new Vector2(K.ScreenW / 2f - 220, MathF.Round(200 - (1 - slide) * 24)), slide);
        }

        if (def.Hints.Length > 0)
        {
            float show = hintsOn ? Clamp01((hintT - (showIntro ? 3.3f : 0.2f)) * 3) * Clamp01((hintEnd - hintT) / 0.8f) : 0;
            if (show > 0.01f)
            {
                float maxW = def.Hints.Max(hh => UI.Measure(hh, 16).X);
                var hr = new Rectangle(K.ScreenW / 2f - maxW / 2 - 30, 104, maxW + 60, def.Hints.Length * 28 + 26);
                UI.Panel(hr, show);
                Raylib.DrawRectangle((int)hr.X + 2, (int)hr.Y + 2, 4, (int)hr.Height - 4, PP.BlueRamp[2].WithAlpha(show));
                for (int i = 0; i < def.Hints.Length; i++)
                    UI.Text(def.Hints[i], new Vector2(K.ScreenW / 2f + 4, hr.Y + 27 + i * 28), 16, PP.Cream.WithAlpha(show), UI.Align.Center);
            }
            else if (!hintsOn || hintT > hintEnd)
                UI.Text("H HINTS   R RESTART   ESC PAUSE", new Vector2(20, K.ScreenH - 30), 16, Dim.WithAlpha(0.6f));
        }

        if (w.Player is { Dead: true } pd)
        {
            float a = Clamp01(pd.DeadTimer * 2);
            UI.TextShadow("SUBJECT TERMINATED", new Vector2(K.ScreenW / 2f, K.ScreenH / 2f - 20), 46, C(255, 90, 80, (int)(255 * a)), UI.Align.Center);
            UI.Text("RESTARTING CHAMBER", new Vector2(K.ScreenW / 2f, K.ScreenH / 2f + 30), 16, Dim.WithAlpha(a), UI.Align.Center);
        }
    }

    void DrawChamberCard(Vector2 pos, float alpha)
    {
        var def = world!.Def;
        var r = new Rectangle(MathF.Round(pos.X / 2) * 2, pos.Y, 440, 250);
        int A(float v) => (int)(v * alpha);
        Raylib.DrawRectangle((int)r.X + 8, (int)r.Y + 8, (int)r.Width, (int)r.Height, C(0, 0, 0, A(110)));
        Raylib.DrawRectangleRec(r, C(234, 229, 218, A(250)));
        Raylib.DrawRectangleLinesEx(r, 2, C(30, 32, 40, A(255)));
        Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 2, (int)r.Width - 4, 2, C(255, 255, 255, A(200)));
        var ink = C(30, 32, 40, A(255));
        string num = randomMode ? $"{streak + 1:00}" : $"{level + 1:00}";
        UI.Text(num, new Vector2(r.X + 28, r.Y + 22), 120, ink);
        var nm = UI.Measure(num, 120);
        UI.Text(randomMode ? "RANDOM" : $"/{Levels.All.Length}", new Vector2(r.X + 40 + nm.X, r.Y + 22 + nm.Y - 28), 30, C(120, 122, 130, A(255)));
        Raylib.DrawRectangle((int)r.X + 28, (int)r.Y + 150, (int)r.Width - 56, 4, ink);
        UI.Text(randomMode ? "GENERATED CHAMBER" : def.Name.ToUpperInvariant(), new Vector2(r.X + 28, r.Y + 166), 30, ink);
        Icons.Draw(def, new Vector2(r.X + 28, r.Y + 206), 28, ink, alignRight: false, filled: true);
        Raylib.DrawRectangle((int)(r.X + r.Width - 64), (int)r.Y + 28, 32, 32, PP.OrangeRamp[2].WithAlpha(alpha));
        Raylib.DrawRectangle((int)(r.X + r.Width - 64), (int)r.Y + 68, 32, 32, PP.BlueRamp[2].WithAlpha(alpha));
    }

    void DrawCrosshair()
    {
        var w = world!;
        if (w.Player is not { Dead: false }) return;
        var c = new Vector2(MathF.Round(UI.Mouse.X / 2) * 2, MathF.Round(UI.Mouse.Y / 2) * 2);
        Raylib.DrawRectangle((int)c.X - 2, (int)c.Y - 2, 4, 4, Color.White);
        if (w.Def.Gun == GunMode.None)
        {
            foreach (var (dx, dy) in new[] { (-12, 0), (12, 0), (0, -12), (0, 12) })
                Raylib.DrawRectangle((int)c.X + dx - 2, (int)c.Y + dy - 2, 4, 4, C(255, 255, 255, 110));
            return;
        }
        for (int i = 0; i < 2; i++)
        {
            var col = i == 0 ? PP.BlueRamp[2] : PP.OrangeRamp[2];
            bool placed = w.Portals[i] != null;
            bool usable = w.GunAllows((PortalColor)i);
            var cc = usable ? col.WithAlpha(placed ? 1f : 0.45f) : C(120, 120, 120, 90);
            int sx = i == 0 ? -1 : 1;
            // pixel bracket: [  ] in blue / orange
            Raylib.DrawRectangle((int)c.X + sx * 14 - (sx < 0 ? 4 : 0), (int)c.Y - 12, 4, 24, cc);
            Raylib.DrawRectangle((int)c.X + sx * 14 - (sx < 0 ? 0 : 4) - (sx < 0 ? 0 : 0) + (sx < 0 ? 0 : -4), (int)c.Y - 12, 8, 4, cc);
            Raylib.DrawRectangle((int)c.X + sx * 14 - (sx < 0 ? 0 : 4) + (sx < 0 ? 0 : -4), (int)c.Y + 8, 8, 4, cc);
            if (placed) Raylib.DrawRectangle((int)c.X + sx * 22 - 2, (int)c.Y - 2, 4, 4, col);
        }
    }

    void DrawPause()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 170));
        var r = new Rectangle(K.ScreenW / 2f - 230, K.ScreenH / 2f - 214, 460, 428);
        UI.Panel(r);
        UI.TextShadow("PAUSED", new Vector2(K.ScreenW / 2f, r.Y + 50), 46, PP.CreamHi, UI.Align.Center);
        float y = r.Y + 100, x = r.X + 36, bw = r.Width - 72;
        if (UI.Button("p_resume", new Rectangle(x, y, bw, 56), "RESUME", accent: PP.OrangeRamp[2])) paused = false;
        y += 68;
        if (UI.Button("p_restart", new Rectangle(x, y, bw, 56), "RESTART")) Go(() => LoadLevel(false));
        y += 68;
        if (UI.Button("p_select", new Rectangle(x, y, bw, 56), "CHAMBERS")) Go(() => SetScreen(Screen.Select));
        y += 68;
        if (UI.Button("p_menu", new Rectangle(x, y, bw, 56), "MAIN MENU")) Go(() => SetScreen(Screen.Menu));
    }

    void DrawComplete()
    {
        float a = Clamp01(stateT * 4);
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, (int)(160 * a)));
        var r = new Rectangle(K.ScreenW / 2f - 290, MathF.Round(K.ScreenH / 2f - 236 + (1 - EaseOutCubic(stateT * 3)) * 30), 580, 472);
        UI.Panel(r, a, PP.Exit);
        UI.Text(randomMode ? $"RANDOM TEST  SEED {randomSeed}" : $"TEST CHAMBER {level + 1:00}", new Vector2(K.ScreenW / 2f, r.Y + 40), 16, Dim.WithAlpha(a), UI.Align.Center);
        UI.TextShadow(randomMode ? "PASSED" : "COMPLETE", new Vector2(K.ScreenW / 2f, r.Y + 90), 60, PP.Exit.WithAlpha(a), UI.Align.Center);

        float y = r.Y + 150;
        void Row(string k, string v, Color vc, string? tag = null)
        {
            UI.Text(k, new Vector2(r.X + 50, y + 4), 16, Dim.WithAlpha(a));
            UI.Text(v, new Vector2(r.X + r.Width - 50, y), 30, vc.WithAlpha(a), UI.Align.Right);
            if (tag != null)
            {
                var m = UI.Measure(v, 30);
                var tr = new Rectangle(r.X + r.Width - 50 - m.X - 120, y, 106, 26);
                Raylib.DrawRectangleRec(tr, PP.OrangeRamp[2].WithAlpha(a));
                UI.Text(tag, new Vector2(tr.X + tr.Width / 2, tr.Y + 13), 16, PP.Ink, UI.Align.Center);
            }
            Raylib.DrawRectangle((int)r.X + 50, (int)y + 38, (int)r.Width - 100, 2, C(255, 255, 255, (int)(20 * a)));
            y += 50;
        }
        Row("TIME", UI.FormatTime(levelTime), PP.CreamHi, newBest ? "NEW BEST" : null);
        if (randomMode) Row("STREAK", streak.ToString(), PP.Exit, streak >= save.RandomBestStreak && streak > 1 ? "RECORD" : null);
        else Row("BEST", UI.FormatTime(save.Best[level]), PP.Cream);
        Row("PORTALS PLACED", world!.PortalsPlaced.ToString(), PP.Cream);
        Row("DEATHS", deaths.ToString(), deaths == 0 ? PP.Exit : PP.Cream);

        bool last = !randomMode && level + 1 >= Levels.All.Length;
        float by = r.Y + r.Height - 80;
        string next = randomMode ? "NEXT TEST" : last ? "FINISH" : "NEXT (ENTER)";
        if (UI.SmallButton("c_next", new Rectangle(r.X + r.Width - 50 - 210, by, 210, 50), next, PP.Exit)) NextLevel();
        if (!randomMode && UI.SmallButton("c_retry", new Rectangle(r.X + 50, by, 124, 50), "REPLAY")) StartLevel(level);
        if (UI.SmallButton("c_menu", new Rectangle(r.X + (randomMode ? 50 : 186), by, 100, 50), "MENU")) Go(() => SetScreen(Screen.Menu));
    }
}
