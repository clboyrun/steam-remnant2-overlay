using Remnant2Overlay.SaveReader;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.App;

/// <summary>Turns what the overlay read from the save into the phone sync file's save snapshot.</summary>
public static class SyncSnapshot
{
    public static SaveSnapshot FromRuns(ItemDatabase db, IReadOnlyList<CharacterRuns> characters, DateTimeOffset readAt, string? status) => new()
    {
        ReadAt = readAt,
        Status = status,
        Characters = characters.Select(c => new CharacterSnapshot
        {
            Index = c.Index,
            Description = c.Description,
            Owned = db.Items.Where(i => c.OwnedProfileIds.Contains(i.ProfileId)).Select(i => i.AssetName).ToList(),
            ActiveSlot = c.ActiveSlot.ToString(),
            Runs = new[] { c.Campaign, c.Adventure }.OfType<RunInfo>().Select(run => FromRun(db, c, run)).ToList(),
            CompletedQuests = [.. c.CompletedQuests],
        }).ToList(),
    };

    /// <summary>Inventory-only snapshot, when the overlay fell back to the simple profile reader.</summary>
    public static SaveSnapshot FromInventories(ItemDatabase db, IReadOnlyList<CharacterInventory> inventories, DateTimeOffset readAt, string? status) => new()
    {
        ReadAt = readAt,
        Status = status,
        Characters = inventories.Select(c => new CharacterSnapshot
        {
            Index = c.Index,
            Description = $"Character {c.Index + 1}",
            Owned = c.Items.Select(i => i.AssetName).Where(a => db.Find(a) is not null).Distinct().ToList(),
        }).ToList(),
    };

    private static RunSnapshot FromRun(ItemDatabase db, CharacterRuns character, RunInfo run) => new()
    {
        Slot = run.Slot.ToString(),
        Difficulty = run.Difficulty,
        RespawnPoint = run.RespawnPoint,
        QuestItems = [.. run.QuestItems],
        Zones = run.Zones.Select(z => new ZoneSnapshot(z.World, z.Story, z.StoryFinished,
            z.Locations.Select(l => new LocationSnapshot(l.Name, [.. l.WorldStones],
                l.Sources.Select(s => new SourceSnapshot(s.Name, s.Kind,
                    s.Items.Select(i => new RunItemSnapshot(i.Id, i.Name, i.Type, i.Status.ToString(), i.Prerequisite, i.HasRequiredMaterial))
                        .ToList())).ToList())).ToList())).ToList(),
        // Owned items are already in the character's Owned list; leaving them out keeps the file small.
        Items = RunAnalysis.Evaluate(db, character, run)
            .Where(s => s.Status != RunItemStatus.Owned)
            .ToDictionary(s => s.Item.AssetName, s => new ItemStatusSnapshot(s.Status.ToString(), s.Reason, s.Where)),
    };
}
