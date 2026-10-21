using System.Text.Json;

namespace Portal2D.Game;

public sealed class SaveData
{
    public int Unlocked { get; set; } = 1;
    /// <summary>Best times keyed by chamber name (stable when chambers are added or reordered).</summary>
    public Dictionary<string, float> Times { get; set; } = new();
    /// <summary>Legacy (v1/v2.0-2.1) index-based best times; migrated into <see cref="Times"/> on load.</summary>
    public float[] Best { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] string[] names = [];

    static readonly string[] Legacy18 =
    [
        "Awakening", "Blue Sky", "Two Ends", "Weighted", "Toxic", "Speedy Thing", "Emancipation", "Through the Glass",
        "Relay", "Ascension", "Up and Over", "Spring Loaded", "Two Weights", "Gel Pit", "Umbrella", "Twin Beams",
        "Grid Lock", "Midterm"
    ];
    public float Master { get; set; } = 0.8f;
    public float Sfx { get; set; } = 1f;
    public float Ambience { get; set; } = 0.5f;
    public bool Cones { get; set; } = true;
    public bool Shake { get; set; } = true;
    public bool Fullscreen { get; set; }
    public int TotalDeaths { get; set; }
    public int RandomBestStreak { get; set; }
    public int RandomCleared { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public bool EditorHelpSeen { get; set; }
    public int Schema { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Portal2D", "save.json");

    public static SaveData Load(string[] levelNames)
    {
        SaveData d;
        try
        {
            d = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<SaveData>(File.ReadAllText(FilePath)) ?? new SaveData()
                : new SaveData();
        }
        catch
        {
            d = new SaveData();
        }
        // v2.3: the original "Final Exam" became "Midterm" (chamber 21); a new Final Exam is chamber 50
        if (d.Schema < 3)
        {
            if (d.Times.Remove("Final Exam", out var fe)) d.Times["Midterm"] = fe;
            d.Schema = 3;
        }
        if (d.Times.Count == 0 && d.Best.Length == Legacy18.Length)
            for (int i = 0; i < Legacy18.Length; i++)
                if (d.Best[i] > 0) d.Times[Legacy18[i]] = d.Best[i];
        d.names = levelNames;
        d.Best = levelNames.Select(n => d.Times.TryGetValue(n, out var t) ? t : 0f).ToArray();
        return d;
    }

    public void Store()
    {
        for (int i = 0; i < names.Length && i < Best.Length; i++)
            if (Best[i] > 0) Times[names[i]] = Best[i];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // progress just won't persist; the game keeps running
        }
    }
}
