using Raylib_cs;

namespace Portal2D.Render;

/// <summary>
/// A CPU pixel buffer. All pixel art (tiles, sprites, the static level layers) is painted into these,
/// then uploaded as textures. Because it is pure CPU it can also be exported to PNG without a window.
/// </summary>
public sealed class Pix
{
    public readonly int W, H;
    public readonly Color[] D;

    public Pix(int w, int h)
    {
        W = w; H = h;
        D = new Color[w * h];
    }

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public Color Get(int x, int y) => In(x, y) ? D[y * W + x] : default;

    public void Set(int x, int y, Color c)
    {
        if (In(x, y)) D[y * W + x] = c;
    }

    /// <summary>Alpha-blend a colour over the existing pixel.</summary>
    public void Blend(int x, int y, Color c)
    {
        if (!In(x, y) || c.A == 0) return;
        if (c.A == 255) { D[y * W + x] = c; return; }
        var d = D[y * W + x];
        float a = c.A / 255f, ia = 1 - a;
        float outA = a + d.A / 255f * ia;
        D[y * W + x] = new Color(
            (byte)(c.R * a + d.R * ia), (byte)(c.G * a + d.G * ia), (byte)(c.B * a + d.B * ia),
            (byte)Math.Clamp((int)(outA * 255), 0, 255));
    }

    public void Rect(int x, int y, int w, int h, Color c)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                Set(i, j, c);
    }

    public void BlendRect(int x, int y, int w, int h, Color c)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                Blend(i, j, c);
    }

    public void HLine(int x, int y, int w, Color c) => Rect(x, y, w, 1, c);
    public void VLine(int x, int y, int h, Color c) => Rect(x, y, 1, h, c);

    /// <summary>Darken (or lighten with negative amount) an existing pixel.</summary>
    public void Shade(int x, int y, float amount)
    {
        if (!In(x, y)) return;
        var d = D[y * W + x];
        float k = 1 - amount;
        D[y * W + x] = new Color((byte)Math.Clamp(d.R * k, 0, 255), (byte)Math.Clamp(d.G * k, 0, 255), (byte)Math.Clamp(d.B * k, 0, 255), d.A);
    }

    /// <summary>Copy another buffer onto this one (alpha blended).</summary>
    public void Draw(Pix src, int dx, int dy, bool flipX = false)
    {
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
                Blend(dx + (flipX ? src.W - 1 - x : x), dy + y, src.D[y * src.W + x]);
    }

    /// <summary>Add a 1px dark outline around every opaque pixel (classic sprite outline).</summary>
    public void Outline(Color c)
    {
        var copy = (Color[])D.Clone();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (copy[y * W + x].A > 0) continue;
                bool near = false;
                for (int k = 0; k < 4 && !near; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx >= 0 && ny >= 0 && nx < W && ny < H && copy[ny * W + nx].A > 200) near = true;
                }
                if (near) D[y * W + x] = c;
            }
    }

    public unsafe Texture2D ToTexture()
    {
        fixed (Color* p = D)
        {
            var img = new Image { Data = p, Width = W, Height = H, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Point);
            return tex;
        }
    }

    public unsafe void Export(string path, int scale = 1)
    {
        var src = scale == 1 ? this : Scaled(scale);
        fixed (Color* p = src.D)
        {
            var img = new Image { Data = p, Width = src.W, Height = src.H, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
            Raylib.ExportImage(img, path);
        }
    }

    public Pix Scaled(int s)
    {
        var o = new Pix(W * s, H * s);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o.D[y * o.W + x] = D[(y / s) * W + x / s];
        return o;
    }
}
