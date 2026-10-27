using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remnant2Overlay.Sync;

/// <summary>
/// The one file shared between the overlay and the phone app, kept in the user's Google Drive app data folder.
/// The save snapshot is written only by the overlay (it's the one that can read the save); manual marks and the
/// picked adventure can be changed on either side and are merged by <see cref="SyncMerge"/>.
/// </summary>
public sealed class SyncDocument
{
    public const int CurrentSchema = 1;
    public const string FileName = "remnant2-overlay-sync.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Schema { get; set; } = CurrentSchema;

    /// <summary>When and by which side ("overlay" or "phone") the file was last written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public SaveSnapshot? Save { get; set; }

    /// <summary>Manual owned/missing marks by character index, then by item asset name.</summary>
    public Dictionary<int, Dictionary<string, OverrideMark>> ManualOverrides { get; set; } = [];

    /// <summary>World picked in manual mode as the rolled adventure (e.g. World_Fae); Value null means all worlds.</summary>
    public Stamped<string?>? AdventureWorld { get; set; }

    public static SyncDocument Parse(string json) =>
        JsonSerializer.Deserialize<SyncDocument>(json, JsonOptions) ?? new SyncDocument();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>
/// A manual mark on one item. Owned null means the mark was cleared (back to following the save); it is kept
/// so the clear wins over an older mark from the other side.
/// </summary>
public sealed record OverrideMark(bool? Owned, DateTimeOffset UpdatedAt);

/// <summary>A value with the time it was last changed, merged last-writer-wins.</summary>
public sealed record Stamped<T>(T Value, DateTimeOffset UpdatedAt);

/// <summary>What the overlay read from the save, ready to show without the game's files.</summary>
public sealed class SaveSnapshot
{
    public DateTimeOffset ReadAt { get; set; }

    /// <summary>The overlay's status line, e.g. when it could only read the inventory.</summary>
    public string? Status { get; set; }

    public List<CharacterSnapshot> Characters { get; set; } = [];
}

public sealed class CharacterSnapshot
{
    public int Index { get; set; }
    public string Description { get; set; } = "";

    /// <summary>Asset names (Ring_AkariWarBand) of tracked items this character owns.</summary>
    public List<string> Owned { get; set; } = [];

    /// <summary>Campaign or Adventure: the run being played.</summary>
    public string? ActiveSlot { get; set; }

    /// <summary>Rolled runs; empty when the overlay could only read the inventory.</summary>
    public List<RunSnapshot> Runs { get; set; } = [];

    public List<string> CompletedQuests { get; set; } = [];
}

public sealed class RunSnapshot
{
    /// <summary>Campaign or Adventure.</summary>
    public string Slot { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string? RespawnPoint { get; set; }
    public List<ZoneSnapshot> Zones { get; set; } = [];
    public List<string> QuestItems { get; set; } = [];

    /// <summary>Status of every tracked item this character doesn't own, by asset name.</summary>
    public Dictionary<string, ItemStatusSnapshot> Items { get; set; } = [];
}

/// <summary>Status names match Remnant2Overlay.SaveReader.RunItemStatus (Available, NeedsPrerequisite, …).</summary>
public sealed record ItemStatusSnapshot(string Status, string Reason, string? Where);

public sealed record ZoneSnapshot(string World, string Story, bool StoryFinished, List<LocationSnapshot> Locations);

public sealed record LocationSnapshot(string Name, List<string> WorldStones, List<SourceSnapshot> Sources);

public sealed record SourceSnapshot(string Name, string Kind, List<RunItemSnapshot> Items);

public sealed record RunItemSnapshot(string Id, string Name, string Type, string Status, string? Prerequisite, bool HasRequiredMaterial);
