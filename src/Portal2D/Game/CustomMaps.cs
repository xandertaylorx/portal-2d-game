using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal2D.Render;

namespace Portal2D.Game;

/// <summary>A chamber built by the player in the map builder. Stored as one small JSON file per map.</summary>
public sealed class CustomMap
{
    public const int MinW = 12, MinH = 10, MaxW = 90, MaxH = 45, MaxName = 24;

    /// <summary>Every tile character a player-made map may contain (no fixed-portal markers).</summary>
    public const string Allowed = " #X=~|PECabcdef123xyz456rst<>^vJGQqMNTUVWYZ";

    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "My Chamber";
    public string[] Map { get; set; } = [];
    /// <summary>0 = no portal gun, 1 = blue only, 2 = both portals.</summary>
    public int Gun { get; set; } = 2;
    public int Theme { get; set; }
    /// <summary>Set once the player has finished the map themselves.</summary>
    public bool Verified { get; set; }
    public float Best { get; set; }
    public DateTime Modified { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public string? FilePath;

    static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public int W => Map.Length == 0 ? 0 : Map[0].Length;
    public int H => Map.Length;

    /// <summary>A fresh room: a metal box with a two-tile floor, a start and an exit.</summary>
    public static CustomMap Blank(string name)
    {
        // 32x18 fits the editor window whole
        var b = LevelBuilder.Room(32, 18);
        b.Set(3, 15, 'P').Set(28, 15, 'E');
        return new CustomMap { Name = name, Map = b.Build() };
    }

    public LevelDef ToDef() => new()
    {
        Name = Name.Trim().Equals("Menu", StringComparison.OrdinalIgnoreCase) ? "My Menu" : Name,
        Map = (string[])Map.Clone(),
        Gun = (GunMode)Math.Clamp(Gun, 0, 2),
        Theme = Theme,
        Label = ""
    };

    public CustomMap Clone() => new()
    {
        Id = Id, Name = Name, Map = (string[])Map.Clone(), Gun = Gun, Theme = Theme,
        Verified = Verified, Best = Best, Modified = Modified, FilePath = FilePath
    };

    /// <summary>Makes a map safe to load whatever a file or a pasted code contained. Returns false if it is unusable.</summary>
    public bool Sanitize()
    {
        if (Map == null || Map.Length < MinH) return false;
        int h = Math.Min(Map.Length, MaxH);
        int w = Math.Min(Map.Max(r => r?.Length ?? 0), MaxW);
        if (w < MinW) return false;
        var rows = new string[h];
        bool hasP = false, hasE = false;
        for (int y = 0; y < h; y++)
        {
            var sb = new StringBuilder(w);
            string src = Map[y] ?? "";
            for (int x = 0; x < w; x++)
            {
                char c = x < src.Length ? src[x] : 'X';
                if (!Allowed.Contains(c)) c = ' ';
                if (c == 'P') { if (hasP) c = ' '; hasP = true; }
                if (c == 'E') { if (hasE) c = ' '; hasE = true; }
                sb.Append(c);
            }
            rows[y] = sb.ToString();
        }
        Map = rows;
        Gun = Math.Clamp(Gun, 0, 2);
        Theme = Math.Clamp(Theme, 0, LevelArt.ThemeCount - 1);
        Name = CleanName(Name);
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 16 || Id.Any(ch => !char.IsAsciiLetterOrDigit(ch))) Id = NewId();
        if (!float.IsFinite(Best) || Best < 0) Best = 0;
        return true;
    }

    public static string CleanName(string? n)
    {
        var sb = new StringBuilder();
        foreach (char c in (n ?? "").ToUpperInvariant())
            if (char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '\'' or '!' or '.' or ',' or '&') sb.Append(c);
        var s = sb.ToString().Trim();
        if (s.Length > MaxName) s = s[..MaxName].TrimEnd();
        return s.Length == 0 ? "MY CHAMBER" : s;
    }

    // ---- sharing: a map as one line of text that can be pasted into a message

    const string CodePrefix = "P2D1:";

    public string ToShareCode()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new CustomMap { Name = Name, Map = Map, Gun = Gun, Theme = Theme });
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true)) gz.Write(json);
        return CodePrefix + Convert.ToBase64String(ms.ToArray());
    }

    public static CustomMap? FromShareCode(string? text)
    {
        try
        {
            text = text?.Trim();
            if (text == null || !text.StartsWith(CodePrefix)) return null;
            var bytes = Convert.FromBase64String(text[CodePrefix.Length..]);
            using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            var buf = new byte[8192];
            int n;
            while ((n = gz.Read(buf, 0, buf.Length)) > 0)
            {
                outMs.Write(buf, 0, n);
                if (outMs.Length > 1_000_000) return null;
            }
            var m = JsonSerializer.Deserialize<CustomMap>(outMs.ToArray());
            if (m == null) return null;
            m.Id = NewId(); // an imported map is always a new map of your own, untested until you beat it
            m.Verified = false; m.Best = 0;
            return m.Sanitize() ? m : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Reads and writes the player's maps in the Portal2D folder next to the save file.</summary>
public static class CustomMaps
{
    /// <summary>Tests point this at a temp folder so they never touch the player's maps.</summary>
    public static string? DirOverride;

    public static string Dir => DirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Portal2D", "maps");

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static List<CustomMap> LoadAll()
    {
        var list = new List<CustomMap>();
        try
        {
            if (!Directory.Exists(Dir)) return list;
            var seen = new HashSet<string>();
            foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<CustomMap>(File.ReadAllText(f));
                    if (m == null || !m.Sanitize()) continue;
                    if (!seen.Add(m.Id)) m.Id = Guid.NewGuid().ToString("N")[..8];
                    m.FilePath = f;
                    list.Add(m);
                }
                catch
                {
                    // a file that is not a map is simply not listed
                }
            }
        }
        catch
        {
        }
        return list.OrderByDescending(m => m.Modified).ToList();
    }

    static string PathFor(CustomMap m)
    {
        var slug = new string(m.Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "map";
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return Path.Combine(Dir, $"{slug}-{m.Id}.json");
    }

    /// <summary>Writes the map to disk. Returns false if the folder could not be written.</summary>
    public static bool Save(CustomMap m)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            m.Modified = DateTime.UtcNow;
            var path = PathFor(m);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(m, Pretty));
            File.Move(tmp, path, true);
            if (m.FilePath != null && m.FilePath != path && File.Exists(m.FilePath)) File.Delete(m.FilePath);
            m.FilePath = path;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Delete(CustomMap m)
    {
        try
        {
            if (m.FilePath != null && File.Exists(m.FilePath)) File.Delete(m.FilePath);
        }
        catch
        {
        }
    }

    public static CustomMap Duplicate(CustomMap m)
    {
        var c = m.Clone();
        c.Id = Guid.NewGuid().ToString("N")[..8];
        c.FilePath = null;
        c.Name = CustomMap.CleanName(m.Name.Length > CustomMap.MaxName - 5 ? m.Name[..(CustomMap.MaxName - 5)] + " COPY" : m.Name + " COPY");
        c.Verified = false; c.Best = 0;
        Save(c);
        return c;
    }

    /// <summary>Show the maps folder in Finder / Explorer / the file manager.</summary>
    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (OperatingSystem.IsMacOS()) Process.Start("open", [Dir]);
            else if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", Dir);
            else Process.Start("xdg-open", Dir);
        }
        catch
        {
        }
    }
}
