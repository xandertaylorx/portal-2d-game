using System.Numerics;
using Raylib_cs;
using Portal2D.Core;
using Portal2D.Render;
using static Portal2D.Core.MathX;

namespace Portal2D.Game;

/// <summary>The "My Maps" screen: every map the player has built, with play / edit / share / delete.</summary>
public sealed class MapLibrary
{
    public enum Act { None, New, Play, Edit }

    public List<CustomMap> Maps = new();
    int page;
    CustomMap? confirmDelete;
    string? toast;
    float toastT;
    readonly Dictionary<string, (long stamp, Texture2D tex)> thumbs = new();

    const int Cols = 3, PerPage = 6;
    const float Cw = 372, Ch = 232, Gx = 18, Gy = 14, Y0 = 134;

    public void Reload()
    {
        FreeThumbs();
        Maps = CustomMaps.LoadAll();
        page = Math.Min(page, Math.Max(0, (Maps.Count + 1 + PerPage - 1) / PerPage - 1));
    }

    public void FreeThumbs()
    {
        foreach (var (_, t) in thumbs.Values) Raylib.UnloadTexture(t);
        thumbs.Clear();
    }

    void Toast(string s) { toast = s; toastT = 3.5f; }

    Texture2D? Thumb(CustomMap m, ref bool madeOne)
    {
        long stamp = m.Modified.Ticks;
        if (thumbs.TryGetValue(m.Id, out var t) && t.stamp == stamp) return t.tex;
        if (madeOne) return null; // at most one new thumbnail per frame
        madeOne = true;
        if (thumbs.TryGetValue(m.Id, out var old)) Raylib.UnloadTexture(old.tex);
        try
        {
            var pix = Preview.Level(new World(m.ToDef()), 0);
            var tex = pix.ToTexture();
            thumbs[m.Id] = (stamp, tex);
            return tex;
        }
        catch
        {
            return null;
        }
    }

    public (Act act, CustomMap? map) Draw(float dt, float stateT, Color dim)
    {
        toastT = MathF.Max(0, toastT - dt);
        Act act = Act.None;
        CustomMap? target = null;
        bool madeOne = false;
        bool modal = confirmDelete != null;

        int slots = Maps.Count + 1;
        int pages = (slots + PerPage - 1) / PerPage;
        page = Math.Clamp(page, 0, pages - 1);
        float x0 = MathF.Round((K.ScreenW - (Cols * Cw + (Cols - 1) * Gx)) / 2);

        for (int s = page * PerPage; s < Math.Min(slots, page * PerPage + PerPage); s++)
        {
            int local = s - page * PerPage;
            var r = new Rectangle(x0 + local % Cols * (Cw + Gx), Y0 + local / Cols * (Ch + Gy), Cw, Ch);
            if (stateT * 5 - local * 0.1f < 0) continue;
            if (s == 0)
            {
                if (NewCard(r, modal)) act = Act.New;
                continue;
            }
            var m = Maps[s - 1];
            var (a, hit) = MapCard(r, m, modal, ref madeOne);
            if (a != Act.None) { act = a; target = hit; }
        }

        if (Maps.Count == 0 && page == 0)
            UI.Text("NO MAPS YET. MAKE YOUR FIRST CHAMBER!", new Vector2(x0 + Cw + Gx + 24, Y0 + 100), 16, dim);

        // footer
        float fy = Y0 + 2 * (Ch + Gy) + 6;
        float fw = Cols * Cw + (Cols - 1) * Gx;
        if (!modal && UI.SmallButton("lib_paste", new Rectangle(x0, fy, 250, 44), "PASTE MAP CODE", PP.Exit)) ImportFromClipboard();
        if (!modal && UI.SmallButton("lib_folder", new Rectangle(x0 + 262, fy, 250, 44), "OPEN MAPS FOLDER")) CustomMaps.OpenFolder();
        if (pages > 1)
        {
            float px = x0 + fw - 230;
            if (page > 0 && !modal && UI.SmallButton("lib_prev", new Rectangle(px, fy, 60, 44), "<")) { page--; }
            UI.Text($"PAGE {page + 1}/{pages}", new Vector2(px + 115, fy + 22), 16, PP.Cream, UI.Align.Center);
            if (page < pages - 1 && !modal && UI.SmallButton("lib_next", new Rectangle(px + 170, fy, 60, 44), ">")) { page++; }
        }
        UI.Text("SHARE: PRESS SHARE, THEN PASTE THE CODE INTO A MESSAGE. ADD ONE: COPY ITS CODE, PRESS PASTE.",
            new Vector2(x0 + 2, fy + 58), 16, dim.WithAlpha(0.8f));

        if (toastT > 0)
        {
            float al = Clamp01(toastT * 2);
            var m = UI.Measure(toast!, 16);
            var tr = new Rectangle(K.ScreenW / 2f - m.X / 2 - 24, 96, m.X + 48, 40);
            UI.Panel(tr, al, PP.OrangeRamp[2]);
            UI.Text(toast!, new Vector2(tr.X + tr.Width / 2, tr.Y + tr.Height / 2), 16, PP.CreamHi.WithAlpha(al), UI.Align.Center);
        }

        if (confirmDelete != null) DrawConfirm();
        return (act, target);
    }

    bool NewCard(Rectangle r, bool modal)
    {
        bool over = !modal && UI.Contains(r, UI.Mouse);
        if (over) r.Y -= 4;
        UI.Panel(r, 1, over ? PP.Exit : null);
        var c = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2 - 24);
        var col = over ? PP.Exit : PP.Exit.WithAlpha(0.7f);
        Raylib.DrawRectangle((int)c.X - 6, (int)c.Y - 34, 12, 68, col);
        Raylib.DrawRectangle((int)c.X - 34, (int)c.Y - 6, 68, 12, col);
        UI.Text("NEW MAP", new Vector2(c.X, r.Y + r.Height - 68), 30, PP.CreamHi, UI.Align.Center);
        UI.Text("START FROM AN EMPTY ROOM", new Vector2(c.X, r.Y + r.Height - 30), 16, C(150, 158, 172), UI.Align.Center);
        if (over && UI.Click) { UI.OnClick?.Invoke(); return true; }
        return false;
    }

    (Act, CustomMap?) MapCard(Rectangle r, CustomMap m, bool modal, ref bool madeOne)
    {
        Act act = Act.None;
        UI.Panel(r, 1, m.Verified ? PP.Exit.WithAlpha(0.8f) : null);

        // thumbnail
        var area = new Rectangle(r.X + 12, r.Y + 12, r.Width - 24, 124);
        Raylib.DrawRectangleRec(area, C(10, 12, 16));
        var tex = Thumb(m, ref madeOne);
        if (tex is { } t)
        {
            float sc = MathF.Min(area.Width / t.Width, area.Height / t.Height);
            float tw = t.Width * sc, th = t.Height * sc;
            Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height),
                new Rectangle(area.X + (area.Width - tw) / 2, area.Y + (area.Height - th) / 2, tw, th), Vector2.Zero, 0, Color.White);
        }
        else UI.Text("...", new Vector2(area.X + area.Width / 2, area.Y + area.Height / 2), 16, C(90, 98, 110), UI.Align.Center);
        Raylib.DrawRectangleLinesEx(area, 2, UI.PanelEdge);

        UI.Text(m.Name, new Vector2(r.X + 14, r.Y + 146), 16, PP.CreamHi, bold: true);
        string status = m.Verified ? (m.Best > 0 ? $"BEATEN - BEST {UI.FormatTime(m.Best)}" : "BEATEN") : "NOT BEATEN YET";
        UI.Text(status, new Vector2(r.X + 14, r.Y + 168), 16, m.Verified ? PP.Exit : PP.OrangeRamp[2]);
        UI.Text($"{m.W}X{m.H}", new Vector2(r.X + r.Width - 14, r.Y + 168), 16, C(110, 118, 132), UI.Align.Right);

        float bw = (r.Width - 28 - 3 * 6) / 4, by = r.Y + r.Height - 46;
        string id = m.Id;
        if (!modal)
        {
            if (UI.SmallButton("lp" + id, new Rectangle(r.X + 14, by, bw, 34), "PLAY", PP.Exit)) act = Act.Play;
            if (UI.SmallButton("le" + id, new Rectangle(r.X + 14 + (bw + 6), by, bw, 34), "EDIT", PP.OrangeRamp[2])) act = Act.Edit;
            if (UI.SmallButton("ls" + id, new Rectangle(r.X + 14 + 2 * (bw + 6), by, bw, 34), "SHARE"))
            {
                Raylib.SetClipboardText(m.ToShareCode());
                Toast($"CODE FOR {m.Name} COPIED - PASTE IT INTO A MESSAGE");
            }
            if (UI.SmallButton("ld" + id, new Rectangle(r.X + 14 + 3 * (bw + 6), by, bw, 34), "DELETE", C(255, 110, 100))) confirmDelete = m;
        }
        else
        {
            foreach (var (i, label) in new[] { (0, "PLAY"), (1, "EDIT"), (2, "SHARE"), (3, "DELETE") })
            {
                var br = new Rectangle(r.X + 14 + i * (bw + 6), by, bw, 34);
                Raylib.DrawRectangleRec(br, C(30, 34, 44));
                UI.Text(label, new Vector2(br.X + bw / 2, br.Y + 17), 16, C(100, 104, 112), UI.Align.Center);
            }
        }
        return (act, act == Act.None ? null : m);
    }

    void ImportFromClipboard()
    {
        string clip;
        try { clip = Raylib.GetClipboardText_() ?? ""; }
        catch { clip = ""; }
        var m = CustomMap.FromShareCode(clip);
        if (m == null) { Toast("NO MAP CODE ON THE CLIPBOARD. COPY ONE FIRST."); return; }
        if (!CustomMaps.Save(m)) { Toast("COULD NOT SAVE THE MAP"); return; }
        Maps.Insert(0, m);
        page = 0;
        Toast($"ADDED {m.Name}");
    }

    void DrawConfirm()
    {
        Raylib.DrawRectangle(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 190));
        var p = new Rectangle(K.ScreenW / 2f - 270, K.ScreenH / 2f - 110, 540, 220);
        UI.Panel(p, 1, C(255, 110, 100));
        UI.Text("DELETE THIS MAP?", new Vector2(K.ScreenW / 2f, p.Y + 42), 30, PP.CreamHi, UI.Align.Center);
        UI.Text(confirmDelete!.Name, new Vector2(K.ScreenW / 2f, p.Y + 90), 16, PP.OrangeRamp[2], UI.Align.Center);
        UI.Text("THIS CANNOT BE UNDONE.", new Vector2(K.ScreenW / 2f, p.Y + 116), 16, C(150, 158, 172), UI.Align.Center);
        if (UI.SmallButton("del_yes", new Rectangle(p.X + 40, p.Y + 148, 210, 48), "DELETE", C(255, 110, 100)))
        {
            CustomMaps.Delete(confirmDelete);
            if (thumbs.Remove(confirmDelete.Id, out var th)) Raylib.UnloadTexture(th.tex);
            Maps.Remove(confirmDelete);
            confirmDelete = null;
        }
        if (UI.SmallButton("del_no", new Rectangle(p.X + 290, p.Y + 148, 210, 48), "KEEP IT", PP.Exit)) confirmDelete = null;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) confirmDelete = null;
    }

    public bool CancelModal()
    {
        if (confirmDelete == null) return false;
        confirmDelete = null;
        return true;
    }
}
