using Remnant2Overlay.Planner;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.Companion;

/// <summary>A character the planner can compare a build with: one synced from the overlay, or one kept on this device.</summary>
public sealed record RuntimeCharacter(string Key, string Label, Collection Collection);

/// <summary>Joins the planner's items (Remnant 2 Toolkit) with the save reader's (lib.remnant2.analyzer) by asset name.</summary>
public static class PlannerLink
{
    public static ItemState State(Collection collection, PlannerItem item)
    {
        if (!item.HasAssetName || collection.ByAsset(item.Id) is not { } info)
            return new ItemState(null, null, null, item.Where, null, null, false);

        string status = collection.Status(info);
        // World drops' reference is just the world's asset name, which the world column already says.
        string? how = info.DropType is null or "World" ? null : $"{info.DropType}: {info.DropReference}";
        if (info.Note is not null) how = how is null ? info.Note : $"{how}. {info.Note}";
        return new ItemState(
            collection.Owned(info),
            collection.Run is null || status == "Unknown" ? null : status,
            info.WorldName,
            how ?? item.Where,
            info.Prerequisite,
            collection.Detail(info)?.Where,
            info.DropType == "Vendor" && info.World == "World_Base");
    }

    /// <summary>Owned according to the character, or null when the save doesn't track the item.</summary>
    public static bool? Owned(Collection? collection, PlannerItem item) =>
        collection is not null && item.HasAssetName && collection.ByAsset(item.Id) is { } info ? collection.Owned(info) : null;
}
