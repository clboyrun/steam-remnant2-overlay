using System.IO;
using System.Text.Json;

namespace Remnant2Overlay.App;

/// <summary>Per-user settings and manual-mode choices, stored in %LOCALAPPDATA%\Remnant2Overlay.</summary>
public sealed class Settings
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Remnant2Overlay");

    public static string IconDir => Path.Combine(DataDir, "icons");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public string? ProfilePath { get; set; }
    public int CharacterIndex { get; set; }
    public double Opacity { get; set; } = 0.92;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 720;
    public double Height { get; set; } = 640;

    /// <summary>Manual owned/missing overrides by asset name, per character index.</summary>
    public Dictionary<int, Dictionary<string, bool>> ManualOverrides { get; set; } = [];

    /// <summary>World picked in manual mode as the rolled adventure (e.g. World_Fae), or null for all.</summary>
    public string? AdventureWorld { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Ignoring unreadable settings: {e.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
