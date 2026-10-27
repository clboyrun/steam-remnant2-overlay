using Remnant2Overlay.SaveReader;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Companion;

/// <summary>One character's items in one run, after the manual marks: what the overlay's tiles show.</summary>
public sealed class Collection(ItemDatabase db, CharacterSnapshot? character, RunSnapshot? run, IReadOnlyDictionary<string, bool> marks)
{
    private readonly HashSet<string> _owned = character?.Owned.ToHashSet(StringComparer.Ordinal) ?? [];
    private readonly Dictionary<string, ItemInfo> _byId = db.Items.ToDictionary(i => i.Id);

    public ItemDatabase Db => db;
    public ItemInfo? ById(string id) => _byId.GetValueOrDefault(id);
    public RunSnapshot? Run => run;

    public bool OwnedInSave(ItemInfo item) => _owned.Contains(item.AssetName);

    public bool? Mark(ItemInfo item) => marks.TryGetValue(item.AssetName, out bool owned) ? owned : null;

    public bool Owned(ItemInfo item) => Mark(item) ?? OwnedInSave(item);

    public string Status(ItemInfo item) =>
        Owned(item) ? "Owned" : run?.Items.GetValueOrDefault(item.AssetName)?.Status ?? "Unknown";

    public ItemStatusSnapshot? Detail(ItemInfo item) => run?.Items.GetValueOrDefault(item.AssetName);

    /// <summary>A run-map item the character still wants: not owned and rolled in this run.</summary>
    public bool Wanted(RunItemSnapshot item) =>
        Labels.Collectable.Contains(item.Status)
        && _byId.TryGetValue(item.Id, out var info)
        && !Owned(info);
}
