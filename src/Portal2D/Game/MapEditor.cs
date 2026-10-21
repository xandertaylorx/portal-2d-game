using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using Portal2D.Render;
using static Portal2D.Core.MathX;
using Tool = Portal2D.Game.EditorTool;

namespace Portal2D.Game;

sealed record EditorTool(string Id, string Name, string Help, char Ch, int Section, int Wire = -1, bool Fillable = false, bool Unique = false);

/// <summary>
/// The map builder. The map on screen is a real <see cref="World"/> rebuilt after every edit, so what you
/// see is exactly what you will play. Left click paints, right click erases, and everything autosaves.
/// </summary>
public sealed class MapEditor
{
    const int T = K.Tile;

    // ---------------------------------------------------------------- tools

    static readonly string[] Sections = ["BLOCKS", "GEL AND PLATES", "OBJECTS", "WIRING", "LASERS"];

    // Wire: 0 button, 1 timed button, 2 door, 3 laser target (these take the number picked below the tools)
    static readonly Tool[] Tools =
    [
        new("panel", "WHITE PANEL", "A WALL PORTALS CAN STICK TO.", '#', 0, Fillable: true),
        new("metal", "METAL WALL", "A WALL PORTALS CAN'T STICK TO.", 'X', 0, Fillable: true),
        new("glass", "GLASS", "YOU CAN SEE AND SHOOT LASERS THROUGH IT, BUT NOT WALK.", '=', 0, Fillable: true),
        new("goo", "TOXIC GOO", "DEADLY. CROSS IT WITH PORTALS.", '~', 0, Fillable: true),
        new("fizz", "EMANCIPATION GRID", "FIZZLES YOUR PORTALS AND ANY CUBE THAT TOUCHES IT.", '|', 0, Fillable: true),
        new("erase", "ERASER", "CLEARS A TILE. RIGHT CLICK ALSO ERASES.", ' ', 0, Fillable: true),

        new("bluegel", "BLUE GEL", "BOUNCY. JUMP ONTO IT TO FLY HIGH.", 'J', 1, Fillable: true),
        new("speedgel", "ORANGE GEL", "RUN FAST ACROSS IT TO JUMP HUGE GAPS.", 'G', 1, Fillable: true),
        new("plateR", "LAUNCH PLATE RIGHT", "THROWS YOU UP AND TO THE RIGHT.", 'Q', 1),
        new("plateL", "LAUNCH PLATE LEFT", "THROWS YOU UP AND TO THE LEFT.", 'q', 1),

        new("start", "START", "WHERE THE PLAYER BEGINS. ONLY ONE.", 'P', 2, Unique: true),
        new("exit", "EXIT", "REACH THIS TO WIN. ONLY ONE.", 'E', 2, Unique: true),
        new("cube", "CUBE", "PICK UP WITH E. WEIGHS DOWN BUTTONS.", 'C', 2),
        new("mirrorR", "MIRROR CUBE (/)", "BENDS A LASER BEAM.", 'M', 2),
        new("mirrorL", "MIRROR CUBE (BACKSLASH)", "BENDS A LASER BEAM THE OTHER WAY.", 'N', 2),

        new("button", "FLOOR BUTTON", "OPENS DOORS WITH THE SAME NUMBER WHILE PRESSED.", '1', 3, Wire: 0),
        new("timed", "TIMED BUTTON", "HOLDS DOORS OPEN FOR A FEW SECONDS.", 'T', 3, Wire: 1),
        new("door", "DOOR", "OPENS WHEN ITS NUMBER IS TRIGGERED.", 'a', 3, Wire: 2, Fillable: true),
        new("target", "LASER TARGET", "OPENS DOORS WITH THE SAME NUMBER WHEN HIT BY A LASER.", '4', 3, Wire: 3),

        new("laserR", "LASER RIGHT", "SHOOTS A LASER TO THE RIGHT.", '>', 4),
        new("laserL", "LASER LEFT", "SHOOTS A LASER TO THE LEFT.", '<', 4),
        new("laserU", "LASER UP", "SHOOTS A LASER UP.", '^', 4),
        new("laserD", "LASER DOWN", "SHOOTS A LASER DOWN.", 'v', 4),
    ];

    public static readonly Color[] GroupCol =
    [
        C(255, 99, 99), C(255, 214, 90), C(94, 224, 138), C(90, 200, 255), C(190, 130, 255), C(255, 150, 205)
    ];

    enum Brush { Draw, Box, Fill }

    // ---------------------------------------------------------------- state

    public readonly CustomMap Map;
    char[][] g;
    public World World { get; private set; } = null!;
    bool worldDirty = true;

    Tool tool = Tools[0];
    Brush brush = Brush.Draw;
    int group;

    readonly Stack<string[]> undo = new(), redo = new();

    // stroke
    bool stroking;
    MouseButton strokeBtn;
    Tool strokeTool = Tools[0];
    (int x, int y) strokeStart, strokeLast, hover = (-1, -1);
    string[]? strokeSnap;
    bool boxFilled;

    // view
    Vector2 pan;
    Vector2 lastMouse;
    bool panning;

    // ui
    public bool WantExit, WantTest;
    public bool ShowHelp;
    bool showSettings, naming;
    int modalSkip;
    string nameBuf = "";
    string? toast;
    float toastT, savedT;

    static readonly Rectangle MapArea = new(244, 64, K.ScreenW - 244, K.ScreenH - 64 - 34);

    /// <summary>False in self-tests so nothing is written to the player's maps folder.</summary>
    public bool Persist = true;

    public MapEditor(CustomMap map)
    {
        Map = map;
        g = map.Map.Select(r => r.ToCharArray()).ToArray();
        // start at the bottom-left, where the player usually begins (small maps simply centre)
        pan = new Vector2(MapArea.Width / 2, H * T - MapArea.Height / 2);
        ClampPan();
        RebuildWorld();
    }

    int W => g[0].Length;
    int H => g.Length;

    string[] Rows() => g.Select(r => new string(r)).ToArray();

    void RebuildWorld()
    {
        var def = new LevelDef { Name = "Editor", Map = Rows(), Gun = (GunMode)Map.Gun, Theme = Map.Theme };
        World = new World(def);
        World.ComputeLasers();
        worldDirty = false;
        ComputeIssues();
    }

    // ---------------------------------------------------------------- validation

    public bool HasStart { get; private set; }
    public bool HasExit { get; private set; }
    public bool Ready => HasStart && HasExit;
    readonly List<(string text, bool blocking)> issues = new();

    void ComputeIssues()
    {
        issues.Clear();
        HasStart = HasExit = false;
        var doors = new bool[6]; var triggers = new bool[6]; var targets = new bool[6];
        bool emitter = false;
        foreach (var row in g)
            foreach (char c in row)
            {
                if (c == 'P') HasStart = true;
                else if (c == 'E') HasExit = true;
                else if (World.IsDoor(c)) doors[c - 'a'] = true;
                else if (World.ButtonGroup(c) >= 0) triggers[World.ButtonGroup(c)] = true;
                else if (World.ReceiverGroup(c) >= 0) { triggers[World.ReceiverGroup(c)] = true; targets[World.ReceiverGroup(c)] = true; }
                else if (c is '<' or '>' or '^' or 'v') emitter = true;
            }
        if (!HasStart) issues.Add(("PLACE A START - THE PLAYER NEEDS SOMEWHERE TO BEGIN", true));
        if (!HasExit) issues.Add(("PLACE AN EXIT - THE PLAYER NEEDS SOMEWHERE TO GO", true));
        for (int i = 0; i < 6; i++)
        {
            if (doors[i] && !triggers[i]) issues.Add(($"DOOR {i + 1} HAS NO BUTTON OR LASER TARGET, SO IT STAYS SHUT", false));
            if (!doors[i] && triggers[i]) issues.Add(($"NUMBER {i + 1} HAS A BUTTON OR TARGET BUT NO DOOR", false));
            if (targets[i] && !emitter) issues.Add(($"LASER TARGET {i + 1} NEEDS A LASER", false));
        }
    }

    // ---------------------------------------------------------------- editing

    static bool Wallish(char c) => c is '#' or 'X' or '=' or '~';

    void PushUndo(string[] snap)
    {
        undo.Push(snap);
        if (undo.Count > 120) { var keep = undo.Reverse().Skip(1).ToArray(); undo.Clear(); foreach (var s in keep) undo.Push(s); }
        redo.Clear();
    }

    void Restore(string[] rows)
    {
        g = rows.Select(r => r.ToCharArray()).ToArray();
        Changed();
        ClampPan();
    }

    /// <summary>The map content changed: it needs testing again, and the file on disk is refreshed.</summary>
    void Changed()
    {
        worldDirty = true;
        Map.Verified = false;
        Map.Best = 0;
        Save();
    }

    public void Save()
    {
        Map.Map = Rows();
        if (!Persist) return;
        if (CustomMaps.Save(Map)) savedT = 1.6f;
        else Toast("COULD NOT SAVE - IS THE DISK FULL?");
    }

    /// <summary>Back from a test play: say so if the map was just beaten.</summary>
    public void AfterTest()
    {
        stroking = false;
        if (Map.Verified) Toast("YOU BEAT YOUR MAP! IT'S READY TO SHARE");
    }

    void Toast(string s) { toast = s; toastT = 3f; }

    void DoUndo()
    {
        if (undo.Count == 0) { Toast("NOTHING TO UNDO"); return; }
        redo.Push(Rows());
        Restore(undo.Pop());
    }

    void DoRedo()
    {
        if (redo.Count == 0) { Toast("NOTHING TO REDO"); return; }
        undo.Push(Rows());
        Restore(redo.Pop());
    }

    char CharFor(Tool t, int grp) => t.Wire switch
    {
        0 => World.ButtonChar(grp),
        1 => World.TimedButtonChar(grp),
        2 => World.DoorChar(grp),
        3 => World.ReceiverChar(grp),
        _ => t.Ch
    };

    bool InMap(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    void Put(int x, int y, Tool t)
    {
        if (!InMap(x, y)) return;
        if (t.Unique)
            foreach (var row in g)
                for (int i = 0; i < row.Length; i++)
                    if (row[i] == t.Ch) row[i] = ' ';
        g[y][x] = CharFor(t, group);
    }

    void PutRect((int x, int y) a, (int x, int y) b, Tool t)
    {
        for (int y = Math.Min(a.y, b.y); y <= Math.Max(a.y, b.y); y++)
            for (int x = Math.Min(a.x, b.x); x <= Math.Max(a.x, b.x); x++)
                Put(x, y, t);
    }

    void PutLine((int x, int y) a, (int x, int y) b, Tool t)
    {
        int dx = Math.Abs(b.x - a.x), dy = -Math.Abs(b.y - a.y), sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
        int err = dx + dy, x = a.x, y = a.y;
        for (int guard = 0; guard < 4096; guard++)
        {
            Put(x, y, t);
            if (x == b.x && y == b.y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    void FloodFill(int sx, int sy, Tool t)
    {
        char target = g[sy][sx], with = CharFor(t, group);
        if (target == with) return;
        var q = new Queue<(int, int)>();
        q.Enqueue((sx, sy));
        g[sy][sx] = with;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (InMap(nx, ny) && g[ny][nx] == target) { g[ny][nx] = with; q.Enqueue((nx, ny)); }
        }
    }

    // ---- size, outline, clear

    void Resize(int dw, int dh)
    {
        int nw = Math.Clamp(W + dw, CustomMap.MinW, CustomMap.MaxW), nh = Math.Clamp(H + dh, CustomMap.MinH, CustomMap.MaxH);
        if (nw == W && nh == H) return;
        var snap = Rows();
        // columns go in / come out just inside the right wall, rows just above the floor
        while (W < nw)
            for (int y = 0; y < H; y++)
            {
                char src = g[y][W - 2];
                var row = g[y].ToList();
                row.Insert(W - 1, Wallish(src) ? src : ' ');
                g[y] = row.ToArray();
            }
        while (W > nw)
            for (int y = 0; y < H; y++)
            {
                var row = g[y].ToList();
                row.RemoveAt(W - 2);
                g[y] = row.ToArray();
            }
        while (H < nh)
        {
            var list = g.ToList();
            var copy = new char[W];
            for (int x = 0; x < W; x++) copy[x] = Wallish(g[H - 3][x]) ? g[H - 3][x] : ' ';
            list.Insert(H - 2, copy);
            g = list.ToArray();
        }
        while (H > nh)
        {
            var list = g.ToList();
            list.RemoveAt(H - 3);
            g = list.ToArray();
        }
        PushUndo(snap);
        Changed();
        ClampPan();
    }

    void Ring()
    {
        var snap = Rows();
        for (int x = 0; x < W; x++) { g[0][x] = 'X'; g[H - 1][x] = 'X'; g[H - 2][x] = 'X'; }
        for (int y = 0; y < H; y++) { g[y][0] = 'X'; g[y][W - 1] = 'X'; }
        PushUndo(snap);
        Changed();
        Toast("WALLED IN. CTRL+Z TO UNDO");
    }

    void ClearAll()
    {
        var snap = Rows();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                g[y][x] = x == 0 || y == 0 || x == W - 1 || y >= H - 2 ? 'X' : ' ';
        g[H - 3][Math.Min(4, W - 2)] = 'P';
        g[H - 3][Math.Max(W - 5, 1)] = 'E';
        PushUndo(snap);
        Changed();
        Toast("MAP CLEARED. CTRL+Z TO UNDO");
    }

    // ---------------------------------------------------------------- self-test (run by --test)

    /// <summary>Exercises the editing operations without a window. Returns a description of every failure.</summary>
    public List<string> RunSelfTest()
    {
        var bad = new List<string>();
        void Check(bool ok, string what) { if (!ok) bad.Add(what); }
        int Count(char c) => g.Sum(r => r.Count(x => x == c));

        Persist = false;
        int w0 = W, h0 = H;
        Check(Count('P') == 1 && Count('E') == 1, "a new map has one start and one exit");

        var before = Rows();
        PutRect((5, 5), (9, 5), By("panel"));
        Check(Enumerable.Range(5, 5).All(x => g[5][x] == '#') && g[5][4] == ' ', "box paints exactly the dragged tiles");
        PushUndo(before); Changed(); DoUndo();
        Check(Rows().SequenceEqual(before), "undo restores the previous map");
        DoRedo();
        Check(g[5][5] == '#', "redo repeats the edit");

        Put(10, 10, By("start"));
        Check(Count('P') == 1 && g[10][10] == 'P', "placing a second start moves the first");
        Put(11, 10, By("exit"));
        Check(Count('E') == 1 && g[10][11] == 'E', "placing a second exit moves the first");

        group = 2;
        Put(12, 10, By("button")); Put(13, 10, By("timed")); Put(14, 10, By("door")); Put(15, 10, By("target"));
        Check(g[10][12] == '3' && g[10][13] == 'V' && g[10][14] == 'c' && g[10][15] == '6', "wired tools use the picked number");
        group = 0;

        FloodFill(20, 8, By("goo"));
        Check(g[8][20] == '~' && g[8][21] == '~' && g[1][1] == '~' && g[0][0] == 'X', "fill stays inside its region");
        FloodFill(20, 8, By("erase"));
        Check(g[8][20] == ' ', "fill with the eraser empties the region again");

        // resizing keeps the border and every object
        Resize(6, 4);
        Check(W == w0 + 6 && H == h0 + 4, "map grows when asked");
        Check(g.All(r => r[0] == 'X' && r[^1] == 'X') && g[0].All(c => c == 'X') && g[H - 1].All(c => c == 'X') && g[H - 2].All(c => c == 'X'), "grown map keeps its walls and floor");
        Check(Count('P') == 1 && Count('E') == 1, "growing keeps start and exit");
        Resize(-6, -4);
        Check(W == w0 && H == h0, "map shrinks back");
        Resize(-500, -500);
        Check(W == CustomMap.MinW && H == CustomMap.MinH, "map never gets smaller than the minimum");
        Resize(500, 500);
        Check(W == CustomMap.MaxW && H == CustomMap.MaxH, "map never gets bigger than the maximum");

        ClearAll();
        Check(Count('P') == 1 && Count('E') == 1 && g[H - 3][1] == ' ', "clear gives back an empty room with a start and exit");
        Ring();
        Check(g.All(r => r[0] == 'X' && r[^1] == 'X'), "wall-it-in rebuilds the border");

        Map.Map = Rows();
        var world = new World(new LevelDef { Name = "t", Map = Map.Map });
        Check(world.Player != null && world.HasExit, "an edited map loads as a playable world");
        return bad;
    }

    /// <summary>Fills the map with a wired-up example. Only used to take screenshots of the editor.</summary>
    public void Demo()
    {
        group = 0;
        PutRect((12, 10), (14, 10), By("panel"));
        Put(6, 15, By("button")); group = 0; Put(16, 15, By("door")); Put(16, 14, By("door")); Put(16, 13, By("door"));
        group = 1; Put(20, 8, By("laserR")); Put(26, 12, By("target")); Put(24, 15, By("door")); Put(24, 14, By("door"));
        Put(9, 15, By("cube")); Put(19, 15, By("goo")); Put(21, 15, By("bluegel"));
        group = 1; tool = By("door");
        worldDirty = true;
    }

    static Tool By(string id) => Tools.First(t => t.Id == id);

    // ---------------------------------------------------------------- view

    public Vector2 CamTarget => pan - new Vector2(MapArea.X + MapArea.Width / 2 - K.ScreenW / 2f, MapArea.Y + MapArea.Height / 2 - K.ScreenH / 2f);

    void ClampPan()
    {
        const float margin = 96;
        float hw = MapArea.Width / 2, hh = MapArea.Height / 2;
        pan.X = W * T <= MapArea.Width ? W * T / 2f : Math.Clamp(pan.X, hw - margin, W * T - hw + margin);
        pan.Y = H * T <= MapArea.Height ? H * T / 2f : Math.Clamp(pan.Y, hh - margin, H * T - hh + margin);
    }

    // ---------------------------------------------------------------- update

    static bool Ctrl => Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl)
        || Raylib.IsKeyDown(KeyboardKey.LeftSuper) || Raylib.IsKeyDown(KeyboardKey.RightSuper);
    static bool Shift => Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

    bool Modal => showSettings || ShowHelp;

    public void Update(float dt, Camera2D cam, bool inputOn)
    {
        toastT = MathF.Max(0, toastT - dt);
        savedT = MathF.Max(0, savedT - dt);
        var mouse = UI.Mouse;
        var delta = mouse - lastMouse;
        lastMouse = mouse;

        if (!inputOn) { stroking = false; return; }

        if (naming) UpdateNaming();
        else
        {
            while (Raylib.GetCharPressed() > 0) { } // typed keys must not pile up and flood the name field later
            UpdateKeys();
        }

        // view: arrows, trackpad scroll, middle mouse or space + drag
        var move = Vector2.Zero;
        if (!naming && !Modal)
        {
            if (Raylib.IsKeyDown(KeyboardKey.Left)) move.X -= 1;
            if (Raylib.IsKeyDown(KeyboardKey.Right)) move.X += 1;
            if (Raylib.IsKeyDown(KeyboardKey.Up)) move.Y -= 1;
            if (Raylib.IsKeyDown(KeyboardKey.Down)) move.Y += 1;
            pan += move * 700 * dt;
            var wheel = Raylib.GetMouseWheelMoveV();
            if (UI.Contains(MapArea, mouse)) pan -= new Vector2(wheel.X, wheel.Y) * 48;
        }
        bool wantPan = !Modal && !stroking && UI.Contains(MapArea, mouse)
            && (Raylib.IsMouseButtonDown(MouseButton.Middle) || (Raylib.IsKeyDown(KeyboardKey.Space) && Raylib.IsMouseButtonDown(MouseButton.Left)));
        if (wantPan) pan -= delta;
        panning = wantPan;
        ClampPan();

        // hover cell
        var wp = Raylib.GetScreenToWorld2D(mouse * K.Px, cam);
        int cx = (int)MathF.Floor(wp.X / T), cy = (int)MathF.Floor(wp.Y / T);
        bool inArea = UI.Contains(MapArea, mouse) && !Modal;
        hover = inArea && InMap(cx, cy) ? (cx, cy) : (-1, -1);
        var clamped = (Math.Clamp(cx, 0, W - 1), Math.Clamp(cy, 0, H - 1));

        if (!stroking && !panning && hover.x >= 0 && !naming)
        {
            bool lp = Raylib.IsMouseButtonPressed(MouseButton.Left), rp = Raylib.IsMouseButtonPressed(MouseButton.Right);
            if ((lp || rp) && !Raylib.IsKeyDown(KeyboardKey.Space)) BeginStroke(lp ? MouseButton.Left : MouseButton.Right, hover);
        }
        else if (stroking)
        {
            if (Raylib.IsMouseButtonDown(strokeBtn) && !panning) ContinueStroke(clamped);
            else EndStroke(clamped);
        }

        if (worldDirty) RebuildWorld();
    }

    void UpdateNaming()
    {
        int ch;
        while ((ch = Raylib.GetCharPressed()) > 0)
        {
            char c = char.ToUpperInvariant((char)ch);
            if (nameBuf.Length < CustomMap.MaxName && (char.IsAsciiLetterOrDigit(c) || " -'!.,&".Contains(c))) nameBuf += c;
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && nameBuf.Length > 0)
            nameBuf = nameBuf[..^1];
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) CommitName();
        else if (Raylib.IsKeyPressed(KeyboardKey.Escape)) naming = false;
    }

    void CommitName()
    {
        naming = false;
        var n = CustomMap.CleanName(nameBuf);
        if (nameBuf.Trim().Length > 0 && n != Map.Name)
        {
            Map.Name = n;
            Save();
        }
    }

    void UpdateKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (showSettings) showSettings = false;
            else if (ShowHelp) ShowHelp = false;
            else if (stroking) { stroking = false; if (strokeSnap != null) { g = strokeSnap.Select(r => r.ToCharArray()).ToArray(); worldDirty = true; } }
            else { Save(); WantExit = true; }
            return;
        }
        if (Modal) return;
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.Z)) { if (Shift) DoRedo(); else DoUndo(); return; }
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) { DoRedo(); return; }
        if (Ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) { Save(); Toast("SAVED ON THIS DEVICE"); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.F5)) { RequestTest(); return; }
        if (Ctrl) return;
        if (Raylib.IsKeyPressed(KeyboardKey.B)) brush = Brush.Draw;
        if (Raylib.IsKeyPressed(KeyboardKey.R)) brush = Brush.Box;
        if (Raylib.IsKeyPressed(KeyboardKey.F)) brush = Brush.Fill;
        if (Raylib.IsKeyPressed(KeyboardKey.E)) tool = Tools.First(t => t.Id == "erase");
        for (int i = 0; i < 6; i++)
            if (Raylib.IsKeyPressed(KeyboardKey.One + i)) group = i;
    }

    void RequestTest()
    {
        if (!Ready) { Toast(HasStart ? "ADD AN EXIT FIRST" : "ADD A START FIRST"); return; }
        Save();
        WantTest = true;
    }

    // ---- strokes

    void BeginStroke(MouseButton btn, (int x, int y) cell)
    {
        stroking = true;
        strokeBtn = btn;
        strokeTool = btn == MouseButton.Right ? Tools.First(t => t.Id == "erase") : tool;
        strokeSnap = Rows();
        strokeStart = strokeLast = cell;
        boxFilled = strokeTool.Fillable || strokeTool.Id == "erase";
        if (brush == Brush.Fill && strokeTool.Fillable) FloodFill(cell.x, cell.y, strokeTool);
        else if (brush == Brush.Box && boxFilled) { /* applied on release */ }
        else Put(cell.x, cell.y, strokeTool);
        worldDirty = true;
    }

    void ContinueStroke((int x, int y) cell)
    {
        if (brush == Brush.Draw || (brush == Brush.Box && !boxFilled) || (brush == Brush.Fill && !strokeTool.Fillable))
        {
            if (cell != strokeLast)
            {
                PutLine(strokeLast, cell, strokeTool);
                strokeLast = cell;
                worldDirty = true;
            }
        }
        strokeLast = brush == Brush.Box && boxFilled ? cell : strokeLast;
    }

    void EndStroke((int x, int y) cell)
    {
        stroking = false;
        if (brush == Brush.Box && boxFilled) PutRect(strokeStart, cell, strokeTool);
        var snap = strokeSnap!;
        var now = Rows();
        if (!now.SequenceEqual(snap))
        {
            PushUndo(snap);
            Changed();
        }
        worldDirty = true;
    }

    // ---------------------------------------------------------------- drawing: in the world

    public void DrawOverlay()
    {
        var gridCol = C(255, 255, 255, 16);
        for (int x = 0; x <= W; x++) Raylib.DrawRectangle(x * T - 1, 0, 2, H * T, gridCol);
        for (int y = 0; y <= H; y++) Raylib.DrawRectangle(0, y * T - 1, W * T, 2, gridCol);
        Raylib.DrawRectangleLinesEx(new Rectangle(-2, -2, W * T + 4, H * T + 4), 4, PP.BlueRamp[2].WithAlpha(0.5f));

        // wiring: a faint line from each button / laser target to the doors with the same number
        for (int gi = 0; gi < 6; gi++)
        {
            var doors = new List<Vector2>(); var trig = new List<Vector2>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    char c = g[y][x];
                    var ctr = new Vector2(x * T + T / 2f, y * T + T / 2f);
                    if (c == World.DoorChar(gi)) doors.Add(ctr);
                    else if (World.ButtonGroup(c) == gi || World.ReceiverGroup(c) == gi) trig.Add(ctr);
                }
            bool lit = tool.Wire >= 0 && group == gi;
            foreach (var a in trig)
            {
                if (doors.Count == 0) continue;
                var d = doors.OrderBy(v => Vector2.DistanceSquared(a, v)).First();
                Raylib.DrawLineEx(a, d, lit ? 4 : 2, GroupCol[gi].WithAlpha(lit ? 0.75f : 0.28f));
            }
        }
        // number badges on every wired tile
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char c = g[y][x];
                int gi = World.IsDoor(c) ? c - 'a' : Math.Max(World.ButtonGroup(c), World.ReceiverGroup(c));
                if (gi < 0) continue;
                var r = new Rectangle(x * T + 3, y * T + 3, 18, 20);
                Raylib.DrawRectangleRec(r, C(10, 12, 16, 220));
                Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 2, (int)r.Width - 4, (int)r.Height - 4, GroupCol[gi]);
                UI.Text((gi + 1).ToString(), new Vector2(r.X + 4, r.Y + 3), 16, PP.Ink);
            }

        // start label (the game already draws its own EXIT sign)
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (g[y][x] == 'P') Tag("START", x, y, PP.BlueRamp[2]);

        // brush preview
        if (stroking && brush == Brush.Box && boxFilled)
        {
            var (a, b) = (strokeStart, strokeLast);
            var r = new Rectangle(Math.Min(a.x, b.x) * T, Math.Min(a.y, b.y) * T, (Math.Abs(a.x - b.x) + 1) * T, (Math.Abs(a.y - b.y) + 1) * T);
            bool er = strokeTool.Id == "erase";
            Raylib.DrawRectangleRec(r, (er ? C(255, 90, 80) : PP.OrangeRamp[2]).WithAlpha(0.28f));
            Raylib.DrawRectangleLinesEx(r, 4, er ? C(255, 90, 80) : PP.OrangeRamp[2]);
        }
        else if (hover.x >= 0)
        {
            var cell = new Rectangle(hover.x * T, hover.y * T, T, T);
            bool er = tool.Id == "erase";
            if (!er && !(brush == Brush.Fill && tool.Fillable)) ToolIcons.Draw(tool, group, cell, 0.6f);
            Raylib.DrawRectangleLinesEx(cell, 4, (er ? C(255, 90, 80) : Color.White).WithAlpha(0.9f));
        }
    }

    static void Tag(string s, int x, int y, Color c)
    {
        var m = UI.Measure(s, 16);
        var pos = new Vector2(x * T + T / 2f - m.X / 2, y * T - 22);
        Raylib.DrawRectangle((int)pos.X - 4, (int)pos.Y - 2, (int)m.X + 8, 18, C(10, 12, 16, 210));
        UI.Text(s, pos, 16, c);
    }

    // ---------------------------------------------------------------- drawing: the interface

    bool drawingModal;
    bool Live => !Modal || drawingModal;

    bool Chip(Rectangle r, string label, bool selected, Color? accent = null, bool enabled = true, Color? textCol = null)
    {
        var acc = accent ?? PP.BlueRamp[2];
        bool over = enabled && Live && UI.Contains(r, UI.Mouse);
        Color fill = selected ? acc : over ? C(46, 52, 66) : C(28, 31, 40);
        Color edge = selected ? Color.White.WithAlpha(0.85f) : over ? acc : C(58, 65, 80);
        Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 3, (int)r.Width, (int)r.Height, C(0, 0, 0, 100));
        Raylib.DrawRectangleRec(r, fill);
        Raylib.DrawRectangleLinesEx(r, 2, edge);
        var tc = textCol ?? (!enabled ? C(90, 96, 108) : selected ? PP.Ink : PP.Cream);
        UI.Text(label, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), 16, tc, UI.Align.Center);
        if (over && UI.Click) { UI.OnClick?.Invoke(); return true; }
        return false;
    }

    public void DrawUi()
    {
        string? hoverHelp = null;
        DrawTopBar();
        DrawPalette(ref hoverHelp);
        DrawStatusStrip(hoverHelp);

        if (toastT > 0)
        {
            float a = Clamp01(toastT * 2);
            var m = UI.Measure(toast!, 16);
            var r = new Rectangle(MapArea.X + MapArea.Width / 2 - m.X / 2 - 24, MapArea.Y + MapArea.Height - 64, m.X + 48, 44);
            UI.Panel(r, a, PP.OrangeRamp[2]);
            UI.Text(toast!, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), 16, PP.CreamHi.WithAlpha(a), UI.Align.Center);
        }

        if (showSettings) DrawSettings();
        if (ShowHelp) DrawHelp();
    }

    void DrawTopBar()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, 62, C(14, 16, 22, 245));
        Raylib.DrawRectangle(0, 62, K.ScreenW, 2, UI.PanelEdge);

        if (Chip(new Rectangle(14, 10, 112, 44), "< BACK", false)) { Save(); WantExit = true; }

        // name field
        var nr = new Rectangle(140, 10, 330, 44);
        bool over = Live && UI.Contains(nr, UI.Mouse);
        if (!naming && over && UI.Click) { naming = true; nameBuf = Map.Name; }
        else if (naming && UI.Click && !over) CommitName();
        Raylib.DrawRectangleRec(nr, naming ? C(36, 42, 56) : C(24, 27, 36));
        Raylib.DrawRectangleLinesEx(nr, 2, naming ? PP.OrangeRamp[2] : over ? PP.BlueRamp[2] : UI.PanelEdge);
        string shown = naming ? nameBuf : Map.Name;
        if (naming && (int)(UI.Time * 2) % 2 == 0) shown += "_";
        else if (naming) shown += " ";
        UI.Text(shown, new Vector2(nr.X + 14, nr.Y + 15), 16, PP.CreamHi);
        if (!naming) UI.Text("RENAME", new Vector2(nr.X + nr.Width - 12, nr.Y + 15), 16, C(110, 118, 132), UI.Align.Right);

        float x = 484;
        if (Chip(new Rectangle(x, 10, 150, 44), "MAP OPTIONS", showSettings, PP.BlueRamp[2])) { showSettings = !showSettings; modalSkip = 1; }
        x += 164;
        if (Chip(new Rectangle(x, 10, 64, 44), "UNDO", false, enabled: undo.Count > 0)) DoUndo();
        x += 72;
        if (Chip(new Rectangle(x, 10, 64, 44), "REDO", false, enabled: redo.Count > 0)) DoRedo();
        x += 72;
        if (Chip(new Rectangle(x, 10, 44, 44), "?", ShowHelp, PP.OrangeRamp[2])) ShowHelp = !ShowHelp;

        var tr = new Rectangle(K.ScreenW - 14 - 200, 10, 200, 44);
        if (Ready)
        {
            if (Chip(tr, "TEST PLAY (F5)", true, PP.Exit)) RequestTest();
        }
        else if (Chip(tr, "TEST PLAY", false, enabled: true, textCol: C(110, 116, 128))) RequestTest();

        // status next to the play button
        string st; Color sc;
        if (savedT > 0) { st = "SAVED"; sc = PP.Exit; }
        else if (!Ready) { st = "NOT READY"; sc = C(255, 110, 100); }
        else if (Map.Verified) { st = "BEATEN - READY TO SHARE"; sc = PP.Exit; }
        else { st = "NOT TESTED YET"; sc = PP.OrangeRamp[2]; }
        UI.Text(st, new Vector2(tr.X - 16, 24), 16, sc, UI.Align.Right);
    }

    void DrawPalette(ref string? hoverHelp)
    {
        var panel = new Rectangle(0, 64, 238, K.ScreenH - 64 - 34);
        Raylib.DrawRectangleRec(panel, C(14, 16, 22, 245));
        Raylib.DrawRectangle(236, 64, 2, (int)panel.Height, UI.PanelEdge);

        float y = 76;
        UI.Text("BRUSH", new Vector2(14, y), 16, C(130, 138, 152));
        y += 22;
        (Brush b, string label, string help)[] brushes =
        [
            (Brush.Draw, "DRAW", "PAINT TILE BY TILE. HOLD AND DRAG. (B)"),
            (Brush.Box, "BOX", "DRAG OUT A RECTANGLE OF TILES. (R)"),
            (Brush.Fill, "FILL", "FLOOD-FILL AN AREA OF THE SAME TILE. (F)")
        ];
        for (int i = 0; i < 3; i++)
        {
            var r = new Rectangle(14 + i * 70, y, 66, 34);
            if (Chip(r, brushes[i].label, brush == brushes[i].b, PP.OrangeRamp[2])) brush = brushes[i].b;
            if (Live && UI.Contains(r, UI.Mouse)) hoverHelp = brushes[i].help;
        }
        y += 44;

        for (int s = 0; s < Sections.Length; s++)
        {
            UI.Text(Sections[s], new Vector2(14, y), 16, C(130, 138, 152));
            y += 20;
            var inSec = Tools.Where(t => t.Section == s).ToList();
            for (int i = 0; i < inSec.Count; i++)
            {
                var t = inSec[i];
                var r = new Rectangle(14 + (i % 4) * 54, y + (i / 4) * 52, 46, 46);
                bool sel = tool == t;
                bool over = Live && UI.Contains(r, UI.Mouse);
                Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 3, 46, 46, C(0, 0, 0, 100));
                Raylib.DrawRectangleRec(r, sel ? C(60, 50, 40) : over ? C(46, 52, 66) : C(28, 31, 40));
                Raylib.DrawRectangleLinesEx(r, 2, sel ? PP.OrangeRamp[2] : over ? PP.BlueRamp[2] : C(58, 65, 80));
                ToolIcons.Draw(t, group, new Rectangle(r.X + 5, r.Y + 5, 36, 36), 1f);
                if (over)
                {
                    hoverHelp = $"{t.Name}: {t.Help}";
                    if (UI.Click) { UI.OnClick?.Invoke(); tool = t; }
                }
            }
            y += (inSec.Count + 3) / 4 * 52;
            if (s == 3)
            {
                // the number that buttons, doors and laser targets share
                bool active = tool.Wire >= 0;
                for (int i = 0; i < 6; i++)
                {
                    var r = new Rectangle(14 + i * 35, y, 31, 31);
                    bool sel = group == i;
                    var col = GroupCol[i];
                    Raylib.DrawRectangleRec(r, active ? col.WithAlpha(sel ? 1f : 0.45f) : col.WithAlpha(0.22f));
                    Raylib.DrawRectangleLinesEx(r, sel ? 4 : 2, sel && active ? Color.White : C(10, 12, 16));
                    UI.Text((i + 1).ToString(), new Vector2(r.X + 11, r.Y + 9), 16, active ? PP.Ink : C(120, 124, 134));
                    if (Live && UI.Contains(r, UI.Mouse))
                    {
                        hoverHelp = $"NUMBER {i + 1}: BUTTONS, TARGETS AND DOORS WITH THE SAME NUMBER ARE WIRED TOGETHER. (KEY {i + 1})";
                        if (UI.Click) { UI.OnClick?.Invoke(); group = i; if (!active) tool = Tools.First(t => t.Id == "button"); }
                    }
                }
                y += 40;
            }
            else y += 2;
        }
    }

    void DrawStatusStrip(string? hoverHelp)
    {
        var r = new Rectangle(0, K.ScreenH - 34, K.ScreenW, 34);
        Raylib.DrawRectangleRec(r, C(14, 16, 22, 250));
        Raylib.DrawRectangle(0, (int)r.Y, K.ScreenW, 2, UI.PanelEdge);
        string text; Color col = C(150, 158, 172);
        var blocking = issues.Where(i => i.blocking).ToList();
        if (hoverHelp != null) text = hoverHelp;
        else if (issues.Count > 0)
        {
            var first = blocking.Count > 0 ? blocking[0] : issues[0];
            text = first.text + (issues.Count > 1 ? $"  (+{issues.Count - 1} MORE)" : "");
            col = first.blocking ? C(255, 130, 120) : PP.Hazard;
        }
        else text = "LEFT CLICK PLACES   RIGHT CLICK ERASES   SPACE + DRAG MOVES AROUND   CTRL+Z UNDOES";
        UI.Text(text, new Vector2(16, r.Y + 11), 16, col);
        if (hover.x >= 0) UI.Text($"{hover.x + 1},{hover.y + 1}   {W}X{H}", new Vector2(K.ScreenW - 16, r.Y + 11), 16, C(110, 118, 132), UI.Align.Right);
        else UI.Text($"{W}X{H}", new Vector2(K.ScreenW - 16, r.Y + 11), 16, C(110, 118, 132), UI.Align.Right);
    }

    void DrawModalBackdrop()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 170));
    }

    void DrawSettings()
    {
        DrawModalBackdrop();
        drawingModal = true;
        var p = new Rectangle(K.ScreenW / 2f - 290, 110, 580, 480);
        UI.Panel(p, 1, PP.BlueRamp[2]);
        UI.TextShadow("MAP OPTIONS", new Vector2(p.X + 40, p.Y + 28), 30, PP.CreamHi);
        float x = p.X + 40, w = p.Width - 80, y = p.Y + 84;

        void Stepper(string label, int value, Action<int> change)
        {
            UI.Text(label, new Vector2(x, y + 15), 16, C(150, 158, 172));
            float sx = x + w - (4 * 52 + 80 + 4 * 6);
            if (Chip(new Rectangle(sx, y, 52, 44), "-5", false)) change(-5);
            if (Chip(new Rectangle(sx + 58, y, 52, 44), "-1", false)) change(-1);
            UI.Text(value.ToString(), new Vector2(sx + 116 + 40, y + 22), 16, PP.CreamHi, UI.Align.Center);
            if (Chip(new Rectangle(sx + 116 + 86, y, 52, 44), "+1", false)) change(1);
            if (Chip(new Rectangle(sx + 116 + 144, y, 52, 44), "+5", false)) change(5);
            y += 58;
        }
        Stepper("WIDTH (TILES)", W, d => Resize(d, 0));
        Stepper("HEIGHT (TILES)", H, d => Resize(0, d));

        UI.Text("LOOK", new Vector2(x, y + 15), 16, C(150, 158, 172));
        float lx = x + w - 230;
        if (Chip(new Rectangle(lx, y, 52, 44), "<", false)) { Map.Theme = (Map.Theme + LevelArt.ThemeCount - 1) % LevelArt.ThemeCount; Changed(); }
        UI.Text($"STYLE {Map.Theme + 1}", new Vector2(lx + 115, y + 22), 16, PP.CreamHi, UI.Align.Center);
        if (Chip(new Rectangle(lx + 178, y, 52, 44), ">", false)) { Map.Theme = (Map.Theme + 1) % LevelArt.ThemeCount; Changed(); }
        y += 58;

        UI.Text("PORTAL GUN", new Vector2(x, y), 16, C(150, 158, 172));
        y += 26;
        (int v, string label)[] guns = [(0, "NO GUN"), (1, "BLUE ONLY"), (2, "BOTH PORTALS")];
        for (int i = 0; i < 3; i++)
            if (Chip(new Rectangle(x + i * (w / 3 + 2), y, w / 3 - 6, 44), guns[i].label, Map.Gun == guns[i].v, PP.OrangeRamp[2]))
            {
                Map.Gun = guns[i].v;
                Changed();
            }
        y += 62;

        if (Chip(new Rectangle(x, y, w / 2 - 6, 44), "WALL IT IN", false)) Ring();
        if (Chip(new Rectangle(x + w / 2 + 6, y, w / 2 - 6, 44), "CLEAR MAP", false, C(255, 110, 100))) ClearAll();
        y += 62;
        if (Chip(new Rectangle(x, y, w, 48), "DONE", true, PP.Exit)) showSettings = false;
        drawingModal = false;
        if (modalSkip > 0) modalSkip--;
        else if (UI.Click && !UI.Contains(p, UI.Mouse)) showSettings = false;
    }

    void DrawHelp()
    {
        DrawModalBackdrop();
        drawingModal = true;
        var p = new Rectangle(K.ScreenW / 2f - 440, 50, 880, 620);
        UI.Panel(p, 1, PP.OrangeRamp[2]);
        UI.TextShadow("BUILD YOUR OWN CHAMBER", new Vector2(p.X + 40, p.Y + 28), 30, PP.CreamHi);
        string[] steps =
        [
            "1  PICK A TOOL ON THE LEFT, THEN CLICK THE MAP TO PLACE IT.",
            "2  RIGHT CLICK ERASES. HOLD AND DRAG TO PAINT. TRY BOX AND FILL.",
            "3  EVERY MAP NEEDS A START AND AN EXIT.",
            "4  DOORS: PICK A NUMBER, THEN PLACE A BUTTON AND A DOOR\n    WITH THE SAME NUMBER. COLOURED BADGES SHOW THE LINKS.",
            "5  PRESS TEST PLAY (F5) AND BEAT YOUR OWN MAP TO MARK IT BEATEN.",
            "6  EVERYTHING SAVES BY ITSELF. FIND YOUR MAPS IN MAP BUILDER."
        ];
        float y = p.Y + 84;
        foreach (var st in steps)
        {
            foreach (var line in st.Split('\n'))
            {
                UI.Text(line, new Vector2(p.X + 40, y), 16, PP.Cream);
                y += 26;
            }
            y += 12;
        }
        y += 8;
        UI.Text("SHORTCUTS", new Vector2(p.X + 40, y), 16, C(130, 138, 152));
        y += 28;
        (string key, string what)[] keys =
        [
            ("CTRL+Z", "UNDO"), ("CTRL+Y", "REDO"), ("CTRL+S", "SAVE"), ("SPACE+DRAG", "MOVE AROUND"),
            ("B R F", "DRAW, BOX, FILL"), ("E", "ERASER"), ("1-6", "WIRING NUMBER"), ("ESC", "BACK TO MY MAPS")
        ];
        for (int i = 0; i < keys.Length; i++)
        {
            float cx = p.X + 40 + (i % 2) * 340, cy = y + (i / 2) * 36;
            UI.KeyCap(keys[i].key, new Vector2(cx, cy));
            UI.Text(keys[i].what, new Vector2(cx + UI.KeyCapWidth(keys[i].key) + 12, cy + 9), 16, PP.Cream);
        }
        if (Chip(new Rectangle(p.X + p.Width - 40 - 220, p.Y + p.Height - 72, 220, 48), "LET'S BUILD", true, PP.Exit)) ShowHelp = false;
        drawingModal = false;
    }
}

/// <summary>Pixel pictures of every tool, drawn on a 16x16 grid so the palette and the map ghost agree.</summary>
static class ToolIcons
{
    public static void Draw(EditorTool tool, int group, Rectangle r, float a)
    {
        string id = tool.Id;
        int wire = tool.Wire;
        float u = r.Width / 16f;

        void P(int x, int y, int w, int h, Color c)
        {
            int x0 = (int)MathF.Round(r.X + x * u), y0 = (int)MathF.Round(r.Y + y * u);
            int x1 = (int)MathF.Round(r.X + (x + w) * u), y1 = (int)MathF.Round(r.Y + (y + h) * u);
            Raylib.DrawRectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0), c.WithAlpha(c.A / 255f * a));
        }
        void Mirror(int x, int y, int w, int h, Color c, bool flip) => P(flip ? 16 - x - w : x, y, w, h, c);

        var ink = C(16, 17, 22);
        switch (id)
        {
            case "panel":
                P(0, 0, 16, 16, PP.Cream); P(0, 0, 16, 1, PP.CreamHi); P(0, 0, 1, 16, PP.CreamHi);
                P(0, 15, 16, 1, PP.CreamSeam); P(15, 0, 1, 16, PP.CreamSeam); P(7, 1, 2, 14, PP.CreamNoise); P(1, 7, 14, 2, PP.CreamNoise);
                break;
            case "metal":
                P(0, 0, 16, 16, PP.Metal); P(0, 0, 16, 1, PP.MetalHi); P(0, 15, 16, 1, PP.MetalLo); P(7, 1, 2, 14, PP.MetalSeam); P(1, 7, 14, 2, PP.MetalSeam);
                foreach (var (x, y) in new[] { (2, 2), (12, 2), (2, 12), (12, 12) }) P(x, y, 2, 2, PP.MetalHi);
                break;
            case "glass":
                P(0, 0, 16, 16, C(143, 211, 255, 90)); P(0, 0, 16, 1, PP.Fizz); P(0, 15, 16, 1, PP.Fizz); P(0, 0, 1, 16, PP.Fizz); P(15, 0, 1, 16, PP.Fizz);
                for (int i = 0; i < 4; i++) P(3 + i * 2, 10 - i * 2, 2, 2, C(255, 255, 255, 190));
                break;
            case "goo":
                P(0, 5, 16, 11, PP.Goo2); P(0, 4, 16, 2, PP.Goo1); P(0, 3, 4, 1, PP.GooTop); P(6, 3, 5, 1, PP.GooTop); P(12, 4, 4, 1, PP.GooTop);
                P(4, 9, 2, 2, PP.GooTop); P(10, 12, 2, 2, PP.Goo1); P(0, 13, 16, 3, PP.Goo3);
                break;
            case "fizz":
                P(3, 0, 10, 3, PP.MetalMid); P(3, 13, 10, 3, PP.MetalMid); P(3, 0, 10, 1, PP.MetalHi);
                foreach (int x in new[] { 4, 7, 10 }) P(x, 3, 2, 10, PP.Fizz.WithAlpha(0.85f));
                break;
            case "erase":
                P(2, 5, 12, 8, C(90, 100, 120)); P(7, 5, 7, 8, C(240, 150, 160)); P(2, 5, 12, 1, C(150, 160, 185)); P(2, 13, 12, 1, C(40, 46, 60));
                break;
            case "bluegel":
            case "speedgel":
            {
                var ramp = id == "bluegel" ? PP.BlueRamp : PP.OrangeRamp;
                P(0, 0, 16, 16, ramp[1]); P(0, 0, 16, 3, ramp[2]); P(0, 13, 16, 3, ramp[0]); P(2, 4, 4, 2, ramp[3]); P(10, 9, 3, 2, ramp[3]);
                if (id == "bluegel") { P(7, 5, 2, 2, ramp[4]); P(5, 7, 2, 2, ramp[4]); P(9, 7, 2, 2, ramp[4]); }
                else { P(5, 6, 2, 2, ramp[4]); P(7, 8, 2, 2, ramp[4]); P(5, 10, 2, 2, ramp[4]); P(9, 6, 2, 2, ramp[4]); P(11, 8, 2, 2, ramp[4]); P(9, 10, 2, 2, ramp[4]); }
                break;
            }
            case "plateR":
            case "plateL":
            {
                bool left = id == "plateL";
                P(0, 0, 16, 16, PP.MetalLo); P(0, 0, 16, 1, PP.MetalHi);
                for (int i = 0; i < 7; i++) Mirror(3 + i, 12 - i, 3, 2, PP.OrangeRamp[2], left);
                Mirror(8, 3, 6, 2, PP.OrangeRamp[2], left); Mirror(12, 3, 2, 6, PP.OrangeRamp[2], left);
                break;
            }
            case "start":
                Raylib.DrawTexturePro(Sprites.PlayerTex, new Rectangle(0, 0, 16, 16), r, Vector2.Zero, 0, Color.White.WithAlpha(a));
                break;
            case "exit":
                P(3, 0, 10, 16, PP.Exit); P(5, 2, 6, 14, C(18, 52, 36)); P(9, 9, 1, 2, PP.Exit); P(3, 0, 10, 1, Color.White.WithAlpha(0.6f));
                break;
            case "cube":
            case "mirrorR":
            case "mirrorL":
                P(1, 1, 14, 14, PP.MetalMid); P(1, 1, 14, 1, PP.MetalHi); P(1, 1, 1, 14, PP.MetalHi); P(14, 1, 1, 14, PP.MetalLo); P(1, 14, 14, 1, PP.MetalLo);
                P(4, 4, 8, 8, PP.MetalLo); P(5, 5, 6, 6, C(108, 116, 140));
                if (id != "cube")
                    for (int i = 0; i < 9; i++) P(id == "mirrorR" ? 3 + i : 12 - i, id == "mirrorR" ? 11 - i : 11 - i, 2, 2, C(200, 240, 255));
                break;
            case "button":
            case "timed":
            {
                bool timed = id == "timed";
                P(1, 12, 14, 4, C(43, 47, 58)); P(1, 12, 14, 1, C(74, 81, 99));
                P(3, 8, 10, 4, timed ? C(230, 170, 40) : C(217, 69, 58)); P(4, 8, 6, 1, timed ? C(255, 230, 150) : C(255, 160, 140));
                if (timed) { P(2, 3, 12, 3, C(24, 27, 34)); P(3, 4, 7, 1, C(255, 214, 90)); }
                break;
            }
            case "door":
                P(2, 0, 12, 16, C(22, 24, 30)); P(4, 0, 8, 16, C(93, 102, 124)); P(5, 0, 1, 16, C(130, 139, 160));
                P(4, 4, 8, 1, C(70, 76, 92)); P(4, 8, 8, 1, C(70, 76, 92));
                for (int i = 0; i < 4; i++) P(4 + i * 2, 12, 2, 4, i % 2 == 0 ? PP.Hazard : C(30, 30, 34));
                break;
            case "target":
                P(1, 1, 14, 14, PP.Metal); P(1, 1, 14, 1, PP.MetalHi); P(1, 14, 14, 1, PP.MetalLo);
                P(4, 4, 8, 8, C(60, 20, 20)); P(5, 5, 6, 6, C(150, 40, 40)); P(6, 6, 4, 4, PP.Laser); P(7, 7, 2, 2, PP.LaserCore);
                break;
            case "laserR": case "laserL": case "laserU": case "laserD":
            {
                P(1, 1, 14, 14, PP.Metal); P(1, 1, 14, 1, PP.MetalHi); P(1, 14, 14, 1, PP.MetalLo); P(3, 3, 10, 10, PP.MetalMid);
                switch (id)
                {
                    case "laserR": P(10, 6, 6, 4, PP.Laser); P(14, 7, 2, 2, PP.LaserCore); break;
                    case "laserL": P(0, 6, 6, 4, PP.Laser); P(0, 7, 2, 2, PP.LaserCore); break;
                    case "laserU": P(6, 0, 4, 6, PP.Laser); P(7, 0, 2, 2, PP.LaserCore); break;
                    default: P(6, 10, 4, 6, PP.Laser); P(7, 14, 2, 2, PP.LaserCore); break;
                }
                break;
            }
        }
        if (wire >= 0 && group >= 0 && group < 6)
        {
            var col = MapEditor.GroupCol[group];
            P(10, 0, 6, 6, ink); P(11, 1, 4, 4, col);
        }
    }
}
