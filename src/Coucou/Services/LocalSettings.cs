using System.Text.Json;

namespace Coucou.Services;

public sealed class LocalSettings
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Coucou", "settings.json");

    public bool CursorTracking { get; set; } = true;
    public bool StartWithWindows { get; set; }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public static LocalSettings Load()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Coucou", "settings.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch { return new(); }
    }
}
