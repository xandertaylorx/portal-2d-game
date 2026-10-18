using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using static Portal2D.Core.MathX;

namespace Portal2D;

/// <summary>Small immediate-mode UI kit. All coordinates are virtual 1280x720 screen units.</summary>
public static class UI
{
    public enum Align { Left, Center, Right }

    public static Font Body, Bold;
    static bool customFonts;
    static readonly Dictionary<string, float> hover = new();
    static float dt;
    public static Vector2 Mouse;
    public static bool Click, MouseDown;
    public static Action? OnHover, OnClick;
    static string? lastHovered;

    public static void Load()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "fonts");
        string body = Path.Combine(dir, "Rajdhani-SemiBold.ttf"), bold = Path.Combine(dir, "Rajdhani-Bold.ttf");
        if (File.Exists(body) && File.Exists(bold))
        {
            Body = Raylib.LoadFontEx(body, 72, null, 0);
            Bold = Raylib.LoadFontEx(bold, 144, null, 0);
            Raylib.GenTextureMipmaps(ref Body.Texture);
            Raylib.GenTextureMipmaps(ref Bold.Texture);
            Raylib.SetTextureFilter(Body.Texture, TextureFilter.Trilinear);
            Raylib.SetTextureFilter(Bold.Texture, TextureFilter.Trilinear);
            customFonts = true;
        }
        else
        {
            Body = Bold = Raylib.GetFontDefault();
        }
    }

    public static void Unload()
    {
        if (!customFonts) return;
        Raylib.UnloadFont(Body);
        Raylib.UnloadFont(Bold);
    }

    public static void BeginFrame(float frameDt, Vector2 mouse)
    {
        dt = frameDt;
        Mouse = mouse;
        Click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        MouseDown = Raylib.IsMouseButtonDown(MouseButton.Left);
    }

    static float Spacing(float size) => customFonts ? size * 0.02f : size / 10f;

    public static Vector2 Measure(string s, float size, bool bold = false, float spacing = -1)
        => Raylib.MeasureTextEx(bold ? Bold : Body, s, size, spacing < 0 ? Spacing(size) : spacing);

    public static void Text(string s, Vector2 pos, float size, Color col, Align align = Align.Left, bool bold = false, float spacing = -1)
    {
        var font = bold ? Bold : Body;
        float sp = spacing < 0 ? Spacing(size) : spacing;
        var m = Raylib.MeasureTextEx(font, s, size, sp);
        float x = align switch { Align.Center => pos.X - m.X / 2, Align.Right => pos.X - m.X, _ => pos.X };
        // fonts sit a little low in their em box; nudge so pos.Y is the visual centre for centred text
        float y = align == Align.Center ? pos.Y - m.Y / 2 : pos.Y;
        Raylib.DrawTextEx(font, s, new Vector2(MathF.Round(x * K.SS) / K.SS, MathF.Round(y * K.SS) / K.SS), size, sp, col);
    }

    public static void TextShadow(string s, Vector2 pos, float size, Color col, Align align = Align.Left, bool bold = false, float spacing = -1)
    {
        Text(s, pos + new Vector2(0, 3), size, C(0, 0, 0, 120), align, bold, spacing);
        Text(s, pos, size, col, align, bold, spacing);
    }

    public static void Panel(Rectangle r, float alpha = 1f)
    {
        Raylib.DrawRectangleRounded(r, 0.06f, 8, Palette.UiBg.WithAlpha(alpha * Palette.UiBg.A / 255f));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.06f, 8, 1.5f, Palette.UiLine.WithAlpha(alpha * 0.12f));
    }

    static float Hover(string id, bool over)
    {
        hover.TryGetValue(id, out float h);
        h = Approach(h, over ? 1 : 0, dt * 8);
        hover[id] = h;
        if (over && lastHovered != id) { lastHovered = id; OnHover?.Invoke(); }
        if (!over && lastHovered == id) lastHovered = null;
        return h;
    }

    public static bool Contains(Rectangle r, Vector2 p) => p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    /// <summary>Big menu button with a sliding accent bar.</summary>
    public static bool Button(string id, Rectangle r, string label, bool enabled = true, Color? accent = null, string? sub = null)
    {
        bool over = enabled && Contains(r, Mouse);
        float h = EaseOutCubic(Hover(id, over));
        var acc = accent ?? Palette.Blue;
        var rr = new Rectangle(r.X + h * 8, r.Y, r.Width, r.Height);
        Raylib.DrawRectangleRounded(rr, 0.18f, 8, Mix(C(22, 27, 34, 220), C(36, 44, 56, 240), h));
        Raylib.DrawRectangleRounded(new Rectangle(rr.X, rr.Y + 6, 4 + h * 3, rr.Height - 12), 0.8f, 4, enabled ? acc.WithAlpha(0.35f + 0.65f * h) : C(80, 80, 80));
        if (h > 0.01f)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawRectangleGradientH((int)rr.X, (int)rr.Y, (int)(rr.Width * 0.6f), (int)rr.Height, acc.WithAlpha(0.18f * h), acc.WithAlpha(0));
            Raylib.EndBlendMode();
        }
        var col = enabled ? Mix(Palette.UiText, Palette.White, h) : Palette.UiDim.WithAlpha(0.5f);
        float size = MathF.Min(30, r.Height * 0.55f);
        if (sub == null)
            Text(label, new Vector2(rr.X + 24, rr.Y + rr.Height / 2 - size * 0.55f), size, col, Align.Left, bold: true, spacing: 2);
        else
        {
            Text(label, new Vector2(rr.X + 24, rr.Y + rr.Height / 2 - size * 0.8f), size, col, Align.Left, bold: true, spacing: 2);
            Text(sub, new Vector2(rr.X + 24, rr.Y + rr.Height / 2 + size * 0.15f), size * 0.55f, Palette.UiDim);
        }
        if (over && Click) { OnClick?.Invoke(); return true; }
        return false;
    }

    /// <summary>Compact pill button.</summary>
    public static bool SmallButton(string id, Rectangle r, string label, Color? accent = null)
    {
        bool over = Contains(r, Mouse);
        float h = Hover(id, over);
        var acc = accent ?? Palette.Blue;
        Raylib.DrawRectangleRounded(r, 0.5f, 8, Mix(C(30, 36, 46, 230), acc.WithAlpha(230), h * 0.85f));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.5f, 8, 1.5f, acc.WithAlpha(0.4f + 0.6f * h));
        Text(label, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), 20, Mix(Palette.UiText, Palette.Ink, h), Align.Center, bold: true, spacing: 1.5f);
        if (over && Click) { OnClick?.Invoke(); return true; }
        return false;
    }

    public static bool Slider(string id, Rectangle r, string label, ref float value)
    {
        bool over = Contains(new Rectangle(r.X, r.Y - 8, r.Width, r.Height + 16), Mouse);
        float h = Hover(id, over);
        Text(label, new Vector2(r.X, r.Y - 30), 20, Palette.UiDim, bold: true, spacing: 1.5f);
        Text($"{(int)MathF.Round(value * 100)}%", new Vector2(r.X + r.Width, r.Y - 30), 20, Palette.UiText, Align.Right, bold: true);
        var track = new Rectangle(r.X, r.Y + r.Height / 2 - 3, r.Width, 6);
        Raylib.DrawRectangleRounded(track, 1, 6, C(50, 58, 70));
        Raylib.DrawRectangleRounded(new Rectangle(track.X, track.Y, track.Width * value, track.Height), 1, 6, Palette.Blue);
        Raylib.DrawCircleV(new Vector2(r.X + r.Width * value, r.Y + r.Height / 2), 9 + h * 2, Palette.White);
        bool changed = false;
        if (over && MouseDown)
        {
            float nv = Clamp01((Mouse.X - r.X) / r.Width);
            changed = MathF.Abs(nv - value) > 0.001f;
            value = nv;
        }
        return changed;
    }

    public static bool Toggle(string id, Rectangle r, string label, ref bool value)
    {
        bool over = Contains(r, Mouse);
        float h = Hover(id, over);
        Raylib.DrawRectangleRounded(r, 0.25f, 8, Mix(C(24, 29, 37, 200), C(36, 44, 56, 230), h));
        Text(label, new Vector2(r.X + 18, r.Y + r.Height / 2 - 12), 22, Palette.UiText, bold: true, spacing: 1.5f);
        var sw = new Rectangle(r.X + r.Width - 70, r.Y + r.Height / 2 - 13, 52, 26);
        Raylib.DrawRectangleRounded(sw, 1, 10, value ? Palette.Blue : C(60, 68, 80));
        Raylib.DrawCircleV(new Vector2(value ? sw.X + 39 : sw.X + 13, sw.Y + 13), 10, Palette.White);
        if (over && Click) { value = !value; OnClick?.Invoke(); return true; }
        return false;
    }

    public static void KeyCap(string key, Vector2 pos, float minW = 34)
    {
        var m = Measure(key, 18, bold: true);
        float w = MathF.Max(minW, m.X + 18);
        var r = new Rectangle(pos.X, pos.Y, w, 32);
        Raylib.DrawRectangleRounded(new Rectangle(r.X, r.Y + 3, r.Width, r.Height), 0.3f, 6, C(10, 12, 16));
        Raylib.DrawRectangleRounded(r, 0.3f, 6, C(58, 66, 78));
        Raylib.DrawRectangleRounded(new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 6), 0.3f, 6, C(72, 82, 96));
        Text(key, new Vector2(r.X + r.Width / 2, r.Y + 15), 18, Palette.White, Align.Center, bold: true, spacing: 1);
    }

    public static float KeyCapWidth(string key, float minW = 34) => MathF.Max(minW, Measure(key, 18, bold: true).X + 18);

    public static string FormatTime(float t)
    {
        if (t <= 0) return "--:--.--";
        int m = (int)(t / 60);
        float s = t - m * 60;
        return $"{m:00}:{s:00.00}";
    }
}
