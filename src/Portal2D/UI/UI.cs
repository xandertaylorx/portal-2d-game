using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using Portal2D.Render;
using static Portal2D.Core.MathX;

namespace Portal2D;

/// <summary>Pixel-art immediate-mode UI. Coordinates are virtual 1280x720 units (2 units = 1 pixel).</summary>
public static class UI
{
    public enum Align { Left, Center, Right }

    static readonly Dictionary<string, float> hover = new();
    static float dt;
    public static Vector2 Mouse;
    public static bool Click, MouseDown;
    public static Action? OnHover, OnClick;
    static string? lastHovered;
    public static float Time;

    public static readonly Color PanelFill = C(18, 20, 27, 240);
    public static readonly Color PanelEdge = C(58, 65, 80);
    public static readonly Color PanelHi = C(84, 93, 114);

    public static void Load() => PixelFont.Load();
    public static void Unload() => PixelFont.Unload();

    public static void BeginFrame(float frameDt, Vector2 mouse)
    {
        dt = frameDt;
        Time += frameDt;
        Mouse = mouse;
        Click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        MouseDown = Raylib.IsMouseButtonDown(MouseButton.Left);
    }

    /// <summary>Map a "font size" in virtual units to an integer pixel scale.</summary>
    public static int Scale(float size) => size < 26 ? 1 : size < 40 ? 2 : size < 56 ? 3 : size < 80 ? 4 : Math.Max(5, (int)(size * K.Px / 7.5f));

    public static Vector2 Measure(string s, float size, bool bold = false, float spacing = -1) => PixelFont.Measure(s, Scale(size));

    public static void Text(string s, Vector2 pos, float size, Color col, Align align = Align.Left, bool bold = false, float spacing = -1)
    {
        int sc = Scale(size);
        var m = PixelFont.Measure(s, sc);
        float x = align switch { Align.Center => pos.X - m.X / 2, Align.Right => pos.X - m.X, _ => pos.X };
        float y = align == Align.Center ? pos.Y - m.Y / 2 : pos.Y;
        if (bold) PixelFont.Draw(s, new Vector2(x, y + sc / K.Px), sc, C(0, 0, 0, (int)(col.A * 0.6f)));
        PixelFont.Draw(s, new Vector2(x, y), sc, col);
    }

    public static void TextShadow(string s, Vector2 pos, float size, Color col, Align align = Align.Left, bool bold = false, float spacing = -1)
    {
        int sc = Scale(size);
        var m = PixelFont.Measure(s, sc);
        float x = align switch { Align.Center => pos.X - m.X / 2, Align.Right => pos.X - m.X, _ => pos.X };
        float y = align == Align.Center ? pos.Y - m.Y / 2 : pos.Y;
        float u = sc / K.Px;
        var shadow = C(10, 10, 14, col.A);
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (1, 1), (0, 2), (1, 2) })
            PixelFont.Draw(s, new Vector2(x + dx * u, y + dy * u), sc, shadow);
        PixelFont.Draw(s, new Vector2(x, y), sc, col);
    }

    static Rectangle SnapR(Rectangle r) => new(MathF.Round(r.X / 2) * 2, MathF.Round(r.Y / 2) * 2, MathF.Round(r.Width / 2) * 2, MathF.Round(r.Height / 2) * 2);

    /// <summary>Pixel panel: dark fill, 1 px border, highlight line, clipped corners.</summary>
    public static void Panel(Rectangle r, float alpha = 1f, Color? accent = null)
    {
        r = SnapR(r);
        int x = (int)r.X, y = (int)r.Y, w = (int)r.Width, h = (int)r.Height;
        Raylib.DrawRectangle(x + 2, y, w - 4, h, PanelFill.WithAlpha(alpha * PanelFill.A / 255f));
        Raylib.DrawRectangle(x, y + 2, w, h - 4, PanelFill.WithAlpha(alpha * PanelFill.A / 255f));
        var e = (accent ?? PanelEdge).WithAlpha(alpha);
        Raylib.DrawRectangle(x + 2, y, w - 4, 2, e);
        Raylib.DrawRectangle(x + 2, y + h - 2, w - 4, 2, e);
        Raylib.DrawRectangle(x, y + 2, 2, h - 4, e);
        Raylib.DrawRectangle(x + w - 2, y + 2, 2, h - 4, e);
        Raylib.DrawRectangle(x + 4, y + 2, w - 8, 2, PanelHi.WithAlpha(alpha * 0.5f));
        Raylib.DrawRectangle(x + 2, y + h, w - 4, 2, C(0, 0, 0, (int)(110 * alpha)));
    }

    static float Hover(string id, bool over)
    {
        hover.TryGetValue(id, out float h);
        h = Approach(h, over ? 1 : 0, dt * 10);
        hover[id] = h;
        if (over && lastHovered != id) { lastHovered = id; OnHover?.Invoke(); }
        if (!over && lastHovered == id) lastHovered = null;
        return h;
    }

    public static bool Contains(Rectangle r, Vector2 p) => p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    public static bool Button(string id, Rectangle r, string label, bool enabled = true, Color? accent = null, string? sub = null)
    {
        bool over = enabled && Contains(r, Mouse);
        float h = Hover(id, over);
        var acc = accent ?? PP.BlueRamp[2];
        bool hot = h > 0.5f;
        var rr = new Rectangle(r.X + (hot ? 8 : 0), r.Y, r.Width, r.Height);
        Panel(rr, 1, hot ? acc : null);
        Raylib.DrawRectangle((int)rr.X + 2, (int)rr.Y + 2, hot ? 8 : 4, (int)rr.Height - 4, enabled ? acc : C(70, 70, 70));
        var col = enabled ? (hot ? Color.White : PP.Cream) : C(100, 104, 112);
        float tx = rr.X + 30;
        if (hot && (int)(Time * 3) % 2 == 0) Text(">", new Vector2(rr.X + 16, rr.Y + rr.Height / 2 - 7), 16, acc);
        if (sub == null) Text(label, new Vector2(tx, rr.Y + rr.Height / 2 - 14), 30, col, bold: true);
        else
        {
            Text(label, new Vector2(tx, rr.Y + 6), 30, col, bold: true);
            Text(sub, new Vector2(tx, rr.Y + rr.Height - 20), 16, C(150, 158, 172));
        }
        if (over && Click) { OnClick?.Invoke(); return true; }
        return false;
    }

    public static bool SmallButton(string id, Rectangle r, string label, Color? accent = null)
    {
        bool over = Contains(r, Mouse);
        float h = Hover(id, over);
        var acc = accent ?? PP.BlueRamp[2];
        r = SnapR(r);
        bool hot = h > 0.5f;
        Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 2, (int)r.Width, (int)r.Height, C(0, 0, 0, 120));
        Raylib.DrawRectangleRec(r, hot ? acc : C(30, 34, 44));
        Raylib.DrawRectangleLinesEx(r, 2, hot ? Color.White.WithAlpha(0.7f) : acc);
        Text(label, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), 16, hot ? PP.Ink : PP.Cream, Align.Center);
        if (over && Click) { OnClick?.Invoke(); return true; }
        return false;
    }

    public static bool Slider(string id, Rectangle r, string label, ref float value)
    {
        bool over = Contains(new Rectangle(r.X, r.Y - 8, r.Width, r.Height + 16), Mouse);
        Hover(id, over);
        Text(label, new Vector2(r.X, r.Y - 26), 16, C(150, 158, 172));
        Text($"{(int)MathF.Round(value * 100)}%", new Vector2(r.X + r.Width, r.Y - 26), 16, PP.Cream, Align.Right);
        int segs = 20;
        float sw = r.Width / segs;
        for (int i = 0; i < segs; i++)
        {
            bool on = (i + 0.5f) / segs <= value;
            Raylib.DrawRectangle((int)(r.X + i * sw), (int)r.Y + 4, (int)sw - 2, 12, on ? PP.BlueRamp[2] : C(44, 50, 63));
        }
        bool changed = false;
        if (over && MouseDown)
        {
            float nv = MathF.Round(Clamp01((Mouse.X - r.X) / r.Width) * 20) / 20f;
            changed = MathF.Abs(nv - value) > 0.001f;
            value = nv;
        }
        return changed;
    }

    public static bool Toggle(string id, Rectangle r, string label, ref bool value)
    {
        bool over = Contains(r, Mouse);
        float h = Hover(id, over);
        Panel(r, 1, h > 0.5f ? PP.BlueRamp[2] : null);
        Text(label, new Vector2(r.X + 18, r.Y + r.Height / 2 - 7), 16, PP.Cream);
        var sw = SnapR(new Rectangle(r.X + r.Width - 74, r.Y + r.Height / 2 - 12, 56, 24));
        Raylib.DrawRectangleRec(sw, value ? PP.BlueRamp[1] : C(44, 50, 63));
        Raylib.DrawRectangleLinesEx(sw, 2, value ? PP.BlueRamp[3] : C(70, 78, 96));
        Raylib.DrawRectangle(value ? (int)sw.X + 32 : (int)sw.X + 4, (int)sw.Y + 4, 20, 16, PP.Cream);
        Text(value ? "ON" : "OFF", new Vector2(sw.X - 12, r.Y + r.Height / 2 - 7), 16, value ? PP.BlueRamp[3] : C(120, 128, 140), Align.Right);
        if (over && Click) { value = !value; OnClick?.Invoke(); return true; }
        return false;
    }

    public static void KeyCap(string key, Vector2 pos, float minW = 34)
    {
        float w = KeyCapWidth(key, minW);
        var r = SnapR(new Rectangle(pos.X, pos.Y, w, 32));
        Raylib.DrawRectangle((int)r.X, (int)r.Y + 4, (int)r.Width, (int)r.Height, C(10, 12, 16));
        Raylib.DrawRectangleRec(r, C(70, 78, 96));
        Raylib.DrawRectangle((int)r.X + 2, (int)r.Y + 2, (int)r.Width - 4, (int)r.Height - 8, C(96, 106, 128));
        Text(key, new Vector2(r.X + r.Width / 2, r.Y + 13), 16, Color.White, Align.Center);
    }

    public static float KeyCapWidth(string key, float minW = 34) => MathF.Max(minW, PixelFont.Measure(key, 1).X + 20);

    public static string FormatTime(float t)
    {
        if (t <= 0) return "--:--.--";
        int m = (int)(t / 60);
        float s = t - m * 60;
        return $"{m:00}:{s:00.00}";
    }
}
