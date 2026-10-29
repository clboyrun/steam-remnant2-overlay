using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.Companion;

public sealed record CategoryFilter(string Label, ItemCategory[] Categories);
public sealed record WorldFilter(string Label, string? World);

/// <summary>Display names and filters, matching the overlay's.</summary>
public static class Labels
{
    public static readonly string[] Collectable = ["Available", "NeedsPrerequisite", "MissedThisRun"];

    public static readonly CategoryFilter[] Categories =
    [
        new("All", Enum.GetValues<ItemCategory>()),
        new("Weapons", [ItemCategory.Weapon]),
        new("Mods", [ItemCategory.WeaponMod]),
        new("Armor", [ItemCategory.Head, ItemCategory.Body, ItemCategory.Gloves, ItemCategory.Legs]),
        new("Rings", [ItemCategory.Ring]),
        new("Amulets", [ItemCategory.Amulet]),
        new("Mutators", [ItemCategory.Mutator]),
        new("Relics", [ItemCategory.Relic]),
    ];

    public static readonly WorldFilter[] Worlds =
    [
        new("All worlds", null),
        new("Ward 13 / any", "World_Base"),
        new("Losomn", "World_Fae"),
        new("Yaesha", "World_Jungle"),
        new("N'Erud", "World_Nerud"),
        new("Root Earth", "World_Root"),
        new("Labyrinth", "World_Labyrinth"),
        new("The Awakened King", "World_DLC1"),
        new("The Forgotten Kingdom", "World_DLC2"),
        new("The Dark Horizon", "World_DLC3"),
    ];

    public static readonly string[] ShowOptions = ["Missing", "Available this run", "Needs quest", "Owned", "All"];

    public static string Category(ItemCategory? category) => category switch
    {
        ItemCategory.Weapon => "Weapon",
        ItemCategory.WeaponMod => "Mod",
        ItemCategory.Head => "Helm",
        ItemCategory.Body => "Chest",
        ItemCategory.Gloves => "Gloves",
        ItemCategory.Legs => "Legs",
        ItemCategory.Ring => "Ring",
        ItemCategory.Amulet => "Amulet",
        ItemCategory.Mutator => "Mutator",
        ItemCategory.RelicFragment => "Relic fragment",
        ItemCategory.Relic => "Relic",
        _ => "Other",
    };

    /// <summary>CSS class for a status name (Remnant2Overlay.SaveReader.RunItemStatus).</summary>
    public static string StatusClass(string status) => status switch
    {
        "Owned" => "s-owned",
        "Available" => "s-available",
        "NeedsPrerequisite" => "s-quest",
        "MissedThisRun" => "s-missed",
        _ => "s-other",
    };

    public static string StatusText(string status) => status switch
    {
        "Owned" => "Owned",
        "Available" => "Available this run",
        "NeedsPrerequisite" => "Needs quest/unlock",
        "MissedThisRun" => "Missed this run",
        "NotRolled" => "Didn't roll",
        "WorldNotInRun" => "Not in this run",
        _ => "Not tied to a run",
    };

    /// <summary>The item's page on the Remnant 2 wiki (wiki.gg), by its display name.</summary>
    public static string WikiUrl(string name) =>
        "https://remnant2.wiki.gg/wiki/" + Uri.EscapeDataString(name.Replace(' ', '_'));
}
