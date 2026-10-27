namespace Remnant2Overlay.Sync;

/// <summary>Combines a local and a remote copy of the sync file.</summary>
public static class SyncMerge
{
    /// <summary>
    /// The newer save snapshot wins as a whole; manual marks are merged item by item and the adventure pick as a
    /// value, each by last change (ties go to local). Neither input is modified.
    /// </summary>
    public static SyncDocument Merge(SyncDocument local, SyncDocument? remote)
    {
        if (remote is null) return local;

        var overrides = new Dictionary<int, Dictionary<string, OverrideMark>>();
        foreach (int character in local.ManualOverrides.Keys.Union(remote.ManualOverrides.Keys))
        {
            var mine = local.ManualOverrides.GetValueOrDefault(character) ?? [];
            var theirs = remote.ManualOverrides.GetValueOrDefault(character) ?? [];
            var merged = new Dictionary<string, OverrideMark>(mine);
            foreach (var (asset, mark) in theirs)
                if (!merged.TryGetValue(asset, out var current) || mark.UpdatedAt > current.UpdatedAt)
                    merged[asset] = mark;
            if (merged.Count > 0) overrides[character] = merged;
        }

        return new SyncDocument
        {
            Schema = SyncDocument.CurrentSchema,
            UpdatedAt = local.UpdatedAt,
            UpdatedBy = local.UpdatedBy,
            Save = Newer(local.Save, remote.Save),
            ManualOverrides = overrides,
            AdventureWorld = remote.AdventureWorld is { } r && (local.AdventureWorld is null || r.UpdatedAt > local.AdventureWorld.UpdatedAt)
                ? r
                : local.AdventureWorld,
        };
    }

    /// <summary>True when the merge changed something the other side doesn't have yet, so it needs uploading.</summary>
    public static bool DiffersFrom(SyncDocument merged, SyncDocument? remote)
    {
        if (remote is null) return true;
        if (merged.Save?.ReadAt != remote.Save?.ReadAt) return true;
        if (merged.AdventureWorld != remote.AdventureWorld) return true;
        if (merged.ManualOverrides.Count != remote.ManualOverrides.Count) return true;
        foreach (var (character, marks) in merged.ManualOverrides)
        {
            if (!remote.ManualOverrides.TryGetValue(character, out var theirs) || theirs.Count != marks.Count) return true;
            foreach (var (asset, mark) in marks)
                if (!theirs.TryGetValue(asset, out var other) || other != mark) return true;
        }
        return false;
    }

    /// <summary>The marks that are set (not cleared) for one character, as owned/missing by asset name.</summary>
    public static Dictionary<string, bool> ActiveOverrides(SyncDocument doc, int character) =>
        (doc.ManualOverrides.GetValueOrDefault(character) ?? [])
            .Where(kv => kv.Value.Owned is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Owned!.Value);

    private static SaveSnapshot? Newer(SaveSnapshot? a, SaveSnapshot? b) =>
        a is null ? b : b is null ? a : b.ReadAt > a.ReadAt ? b : a;
}
