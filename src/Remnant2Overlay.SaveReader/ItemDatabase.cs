using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remnant2Overlay.SaveReader;

/// <summary>One collectible item from the bundled item list (Data/items.json).</summary>
public sealed partial record ItemInfo
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public required string Type { get; init; }
    public string? Subtype { get; init; }
    public string? World { get; init; }
    public string? DropType { get; init; }
    public string? DropReference { get; init; }
    public string? Note { get; init; }
    public string? Prerequisite { get; init; }
    public string? Difficulty { get; init; }
    public bool Hardcore { get; init; }
    public bool Coop { get; init; }
    public string? Material { get; init; }
    public string? WeaponOrMod { get; init; }
    public required string ProfileId { get; init; }

    /// <summary>Asset name as it appears in the save, e.g. Ring_AkariWarBand.</summary>
    [JsonIgnore]
    public string AssetName
    {
        get
        {
            string name = ProfileId[(ProfileId.LastIndexOf('.') + 1)..];
            return name.EndsWith("_C", StringComparison.Ordinal) ? name[..^2] : name;
        }
    }

    [JsonIgnore]
    public ItemCategory? Category => ItemCategories.FromAssetName(AssetName);

    /// <summary>Name, or one derived from the Id (Ring_AkariWarBand becomes "Akari War Band").</summary>
    [JsonIgnore]
    public string DisplayName => Name ?? CamelToWords(Id[(Id.LastIndexOf('_') + 1)..]);

    /// <summary>Friendly world name, e.g. World_Fae becomes Losomn.</summary>
    [JsonIgnore]
    public string WorldName => World switch
    {
        "World_Base" => "Any / Ward 13",
        "World_Fae" => "Losomn",
        "World_Jungle" => "Yaesha",
        "World_Nerud" => "N'Erud",
        "World_Root" => "Root Earth",
        "World_Labyrinth" => "Labyrinth",
        "World_DLC1" => "The Awakened King",
        "World_DLC2" => "The Forgotten Kingdom",
        "World_DLC3" => "The Dark Horizon",
        "World_City" => "Ward 13",
        null => "Unknown",
        var w => w,
    };

    private static string CamelToWords(string s) => CamelBoundary().Replace(s, " ");

    [System.Text.RegularExpressions.GeneratedRegex("(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial System.Text.RegularExpressions.Regex CamelBoundary();
}

public sealed class ItemDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, ItemInfo> _byAssetName;

    public IReadOnlyList<ItemInfo> Items { get; }

    public ItemDatabase(IEnumerable<ItemInfo> items)
    {
        Items = items.Where(i => i.Category is not null).ToList();
        _byAssetName = Items.ToDictionary(i => i.AssetName, StringComparer.Ordinal);
    }

    /// <summary>Loads the item list embedded in this assembly.</summary>
    public static ItemDatabase LoadEmbedded()
    {
        using var stream = typeof(ItemDatabase).Assembly.GetManifestResourceStream("Remnant2Overlay.Items.json")
            ?? throw new InvalidOperationException("Embedded items.json is missing.");
        return new ItemDatabase(JsonSerializer.Deserialize<List<ItemInfo>>(stream, JsonOptions)!);
    }

    public ItemInfo? Find(string assetName) => _byAssetName.GetValueOrDefault(assetName);

    /// <summary>Pairs every known item with whether the character owns it.</summary>
    public IReadOnlyList<CollectionEntry> Compare(CharacterInventory character)
    {
        var owned = character.Items.Select(i => i.AssetName).ToHashSet(StringComparer.Ordinal);
        return Items.Select(i => new CollectionEntry(i, owned.Contains(i.AssetName))).ToList();
    }
}

public sealed record CollectionEntry(ItemInfo Item, bool Owned);
