using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remnant2Overlay.App;

/// <summary>
/// Snapshot written for the in-game UI (Remnant2Overlay.InGame.dll). The overlay app does all the save analysis;
/// the DLL inside the game only reads this file and draws it, and reloads it whenever it changes.
/// </summary>
public sealed record InGameState(
    int Version,
    DateTime Updated,
    string Character,
    string Run,
    string Summary,
    bool NothingLeft,
    IReadOnlyList<string> Quests,
    IReadOnlyList<InGameItem> Items)
{
    public static string FilePath => Path.Combine(Settings.DataDir, "ingame-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes the file atomically, so the game never reads a half-written snapshot.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Settings.DataDir);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}

public sealed record InGameItem(
    string Name,
    string Category,
    string World,
    string Status,
    bool Owned,
    bool Manual,
    string Reason,
    string? Where,
    string? Source,
    string? QuestState,
    IReadOnlyList<InGameRequirement> Requirements,
    IReadOnlyList<string> Extras,
    string? Note)
{
    public static InGameItem From(ItemDetails d) => new(
        d.Name,
        d.Tile.CategoryName,
        d.Tile.Item.WorldName,
        // What the tile shows, after manual marks.
        d.Tile.DisplayStatus.ToString(),
        d.Tile.Owned,
        d.Tile.IsManual,
        d.Reason,
        d.Where,
        d.Source,
        d.QuestState,
        d.Requirements.Select(r => new InGameRequirement(r.Label, r.Met)).ToList(),
        d.Extras,
        d.Note);
}

public sealed record InGameRequirement(string Label, bool? Met);
