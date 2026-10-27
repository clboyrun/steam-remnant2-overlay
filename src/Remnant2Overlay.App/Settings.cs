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

    /// <summary>Remnant 2 Paks folder, if it isn't found through Steam automatically.</summary>
    public string? GamePaksPath { get; set; }

    /// <summary>Optional icon-extraction tweaks: CUE4Parse EGame name and a .usmap mappings file.</summary>
    public string? UnrealVersion { get; set; }
    public string? UsmapPath { get; set; }

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

    /// <summary>
    /// When each manual mark was last set or cleared, per character index, and when the adventure pick last changed.
    /// The phone sync uses these to merge changes made on both sides.
    /// </summary>
    public Dictionary<int, Dictionary<string, DateTimeOffset>> ManualOverrideTimes { get; set; } = [];
    public DateTimeOffset? AdventureWorldChangedAt { get; set; }

    /// <summary>Sync with the phone app through Google Drive (signed in on this PC).</summary>
    public bool PhoneSync { get; set; }

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
