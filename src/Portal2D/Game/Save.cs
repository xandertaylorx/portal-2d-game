using System.Text.Json;

namespace Portal2D.Game;

public sealed class SaveData
{
    public int Unlocked { get; set; } = 1;
    public float[] Best { get; set; } = [];
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

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Portal2D", "save.json");

    public static SaveData Load(int levelCount)
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
        if (d.Best.Length != levelCount) d.Best = d.Best.Concat(new float[Math.Max(0, levelCount - d.Best.Length)]).Take(levelCount).ToArray();
        d.Unlocked = Math.Clamp(d.Unlocked, 1, levelCount);
        return d;
    }

    public void Store()
    {
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
