using lib.remnant2.analyzer;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model;

namespace Remnant2Overlay.SaveReader;

/// <summary>Where a collectible stands for one character in one run (campaign or adventure).</summary>
public enum RunItemStatus
{
    /// <summary>Already in the character's inventory.</summary>
    Owned,
    /// <summary>Rolled in this run and obtainable now.</summary>
    Available,
    /// <summary>Rolled in this run, but needs a quest item, unlock or other prerequisite first.</summary>
    NeedsPrerequisite,
    /// <summary>Rolled in this run, but already taken here or skipped (for example the alternate boss reward).</summary>
    MissedThisRun,
    /// <summary>The item's world is in this run, but the location/event that drops it did not roll.</summary>
    NotRolled,
    /// <summary>The item's world is not part of this run at all.</summary>
    WorldNotInRun,
    /// <summary>Not tied to a rolled location (crafted, account award, global drop and so on).</summary>
    Unknown,
}

public sealed record RunItem(
    string Id,
    string Name,
    string Type,
    RunItemStatus Status,
    string? Prerequisite,
    bool HasRequiredMaterial);

/// <summary>A boss, miniboss, injectable, dungeon, vendor, world drop or location's free loot.</summary>
public sealed record RunSource(string Name, string Kind, IReadOnlyList<RunItem> Items);

public sealed record RunLocation(string Name, IReadOnlyList<string> WorldStones, IReadOnlyList<RunSource> Sources);

public sealed record RunZone(string World, string Story, bool StoryFinished, IReadOnlyList<RunLocation> Locations);

public sealed record RunInfo(
    WorldSlot Slot,
    string Difficulty,
    string? RespawnPoint,
    IReadOnlyList<RunZone> Zones,
    IReadOnlyList<string> QuestItems)
{
    public IEnumerable<RunItem> AllItems => Zones.SelectMany(z => z.Locations).SelectMany(l => l.Sources).SelectMany(s => s.Items);
}

public sealed record CharacterRuns(
    int Index,
    string Description,
    IReadOnlySet<string> OwnedProfileIds,
    WorldSlot ActiveSlot,
    RunInfo Campaign,
    RunInfo? Adventure,
    IReadOnlyList<string> CompletedQuests)
{
    public RunInfo? Get(WorldSlot slot) => slot == WorldSlot.Campaign ? Campaign : Adventure;
}

/// <summary>Per-item status in a run, with a short human-readable reason.</summary>
public sealed record ItemRunStatus(ItemInfo Item, RunItemStatus Status, string Reason, string? Where);

/// <summary>
/// Reads the rolled worlds (campaign and adventure) for each character using lib.remnant2.analyzer,
/// and works out per item whether it can be collected in the run.
/// </summary>
public static class RunAnalysis
{
    // Collectible types we track; anything else (traits, quest items, engrams…) is shown as context only.
    private static readonly HashSet<string> TrackedTypes = ["weapon", "armor", "ring", "amulet", "mutator", "mod", "relic"];

    private static readonly Dictionary<string, string> WorldIdToName = new()
    {
        ["World_Fae"] = "Losomn",
        ["World_DLC1"] = "Losomn",
        ["World_Jungle"] = "Yaesha",
        ["World_DLC2"] = "Yaesha",
        ["World_Nerud"] = "N'Erud",
        ["World_DLC3"] = "N'Erud",
        ["World_Root"] = "Root Earth",
        ["World_Labyrinth"] = "Labyrinth",
    };

    /// <summary>Analyzes a save folder (the one holding profile.sav and save_N.sav).</summary>
    public static IReadOnlyList<CharacterRuns> Analyze(string saveFolder)
    {
        Dataset dataset = Analyzer.Analyze(saveFolder);
        return dataset.Characters.Select(c => new CharacterRuns(
            c.Index,
            c.Profile.ProfileString,
            c.Profile.Inventory.Where(i => i.Quantity is not 0).Select(i => i.ProfileId).ToHashSet(StringComparer.OrdinalIgnoreCase),
            c.ActiveWorldSlot,
            Convert(WorldSlot.Campaign, c.Save.Campaign),
            c.Save.Adventure is { } adventure ? Convert(WorldSlot.Adventure, adventure) : null,
            c.Save.QuestCompletedLog.Select(q => ItemDb.GetItemByIdOrDefault(q)?.Name ?? q).ToList())).ToList();
    }

    private static RunInfo Convert(WorldSlot slot, RolledWorld world) => new(
        slot,
        world.Difficulty,
        world.RespawnPoint?.ToString(),
        world.AllZones.Select(z => new RunZone(
            z.Name,
            z.Story,
            z.Finished,
            z.Locations.Select(l => new RunLocation(
                l.Name,
                l.WorldStones,
                l.LootGroups.Select(g => new RunSource(
                    g.Name ?? l.Name,
                    g.Type,
                    g.Items.Select(ConvertItem).ToList())).ToList())).ToList())).ToList(),
        world.QuestInventory.Select(q => q.LootItem?.Name ?? q.ProfileId).ToList());

    private static RunItem ConvertItem(LootItemExtended item) => new(
        item.Id,
        item.Name,
        item.Type,
        item.IsLooted ? RunItemStatus.MissedThisRun
            : item.IsPrerequisiteMissing ? RunItemStatus.NeedsPrerequisite
            : RunItemStatus.Available,
        item.Properties.TryGetValue("Prerequisite", out string? prerequisite) ? DescribePrerequisite(prerequisite) : null,
        item.HasRequiredMaterial);

    /// <summary>Turns "Engram_Challenger, Amulet_RedDoeSigil" into "Challenger, Red Doe Sigil".</summary>
    public static string DescribePrerequisite(string prerequisite)
    {
        static string Name(string id) => ItemDb.GetItemByIdOrDefault(id.Trim())?.Name ?? id.Trim();
        return string.Join(", ", prerequisite.Split(',').Select(all =>
            string.Join(" or ", all.Split('|').Select(Name))));
    }

    /// <summary>Status of every tracked item for one character in one run.</summary>
    public static IReadOnlyList<ItemRunStatus> Evaluate(ItemDatabase db, CharacterRuns character, RunInfo run)
    {
        // Best occurrence of each item in the run: Available beats NeedsPrerequisite beats MissedThisRun.
        var best = new Dictionary<string, (RunItem Item, string Where)>(StringComparer.Ordinal);
        foreach (var zone in run.Zones)
        foreach (var location in zone.Locations)
        foreach (var source in location.Sources)
        foreach (var item in source.Items)
        {
            if (!TrackedTypes.Contains(item.Type)) continue;
            string where = source.Name == location.Name
                ? $"{zone.World} › {location.Name}"
                : $"{zone.World} › {location.Name} › {source.Name} ({source.Kind})";
            if (!best.TryGetValue(item.Id, out var current) || item.Status < current.Item.Status)
                best[item.Id] = (item, where);
        }

        var worldsInRun = run.Zones.Select(z => z.World).ToHashSet();

        return db.Items.Select(info =>
        {
            if (character.OwnedProfileIds.Contains(info.ProfileId))
                return new ItemRunStatus(info, RunItemStatus.Owned, "Owned", best.GetValueOrDefault(info.Id).Where);

            if (best.TryGetValue(info.Id, out var hit))
            {
                string reason = hit.Item.Status switch
                {
                    RunItemStatus.Available => hit.Item.HasRequiredMaterial ? "Available now (you have the material)" : "Available in this run",
                    RunItemStatus.NeedsPrerequisite => $"Rolled, but needs: {hit.Item.Prerequisite ?? "a quest step"}",
                    RunItemStatus.MissedThisRun => "Rolled, but already taken or skipped in this run",
                    _ => hit.Item.Status.ToString(),
                };
                return new ItemRunStatus(info, hit.Item.Status, reason, hit.Where);
            }

            if (info.World is { } worldId && WorldIdToName.TryGetValue(worldId, out var worldName))
            {
                return worldsInRun.Contains(worldName)
                    ? new ItemRunStatus(info, RunItemStatus.NotRolled, $"{worldName} is in this run, but this drop didn't roll", null)
                    : new ItemRunStatus(info, RunItemStatus.WorldNotInRun, $"{worldName} isn't in this run", null);
            }

            return new ItemRunStatus(info, RunItemStatus.Unknown, info.DropType is null ? "Not tied to a location" : $"{info.DropType}: {info.DropReference}", null);
        }).ToList();
    }
}
