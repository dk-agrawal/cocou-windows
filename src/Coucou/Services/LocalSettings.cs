using System.IO;
using System.Text.Json;

namespace Coucou.Services;

public sealed class LocalSettings
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Coucou", "settings.json");

    public bool CursorTracking { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public Dictionary<string, string> ClaudeSessions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, true);
    }

    public static LocalSettings Load()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Coucou", "settings.json");
            if (!File.Exists(path)) return new();

            var settings = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(path)) ?? new();
            settings.ClaudeSessions = new Dictionary<string, string>(
                settings.ClaudeSessions ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch { return new(); }
    }
}
