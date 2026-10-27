using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Sync.Tests;

public class SyncMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static SyncDocument Doc(params (int Character, string Asset, bool? Owned, int Minutes)[] marks)
    {
        var doc = new SyncDocument();
        foreach (var (character, asset, owned, minutes) in marks)
        {
            if (!doc.ManualOverrides.TryGetValue(character, out var forCharacter))
                doc.ManualOverrides[character] = forCharacter = [];
            forCharacter[asset] = new OverrideMark(owned, T0.AddMinutes(minutes));
        }
        return doc;
    }

    [Fact]
    public void Merge_KeepsTheLaterMarkPerItem()
    {
        var local = Doc((0, "Ring_A", true, 5), (0, "Ring_B", true, 1));
        var remote = Doc((0, "Ring_A", false, 2), (0, "Ring_B", false, 9), (1, "Amulet_C", true, 3));

        var merged = SyncMerge.Merge(local, remote);

        Assert.Equal(true, merged.ManualOverrides[0]["Ring_A"].Owned);
        Assert.Equal(false, merged.ManualOverrides[0]["Ring_B"].Owned);
        Assert.Equal(true, merged.ManualOverrides[1]["Amulet_C"].Owned);
    }

    [Fact]
    public void Merge_LaterClearBeatsOlderMark()
    {
        var local = Doc((0, "Ring_A", true, 1));
        var remote = Doc((0, "Ring_A", null, 4));

        var merged = SyncMerge.Merge(local, remote);

        Assert.Empty(SyncMerge.ActiveOverrides(merged, 0));
        Assert.Null(merged.ManualOverrides[0]["Ring_A"].Owned);
    }

    [Fact]
    public void Merge_TakesNewerSaveSnapshotAndAdventurePick()
    {
        var local = new SyncDocument
        {
            Save = new SaveSnapshot { ReadAt = T0.AddMinutes(10), Status = "local" },
            AdventureWorld = new Stamped<string?>("World_Fae", T0),
        };
        var remote = new SyncDocument
        {
            Save = new SaveSnapshot { ReadAt = T0, Status = "remote" },
            AdventureWorld = new Stamped<string?>(null, T0.AddMinutes(1)),
        };

        var merged = SyncMerge.Merge(local, remote);

        Assert.Equal("local", merged.Save!.Status);
        Assert.Null(merged.AdventureWorld!.Value);
    }

    [Fact]
    public void Merge_KeepsRemoteSaveWhenLocalHasNone()
    {
        var remote = new SyncDocument { Save = new SaveSnapshot { ReadAt = T0, Status = "remote" } };

        Assert.Equal("remote", SyncMerge.Merge(new SyncDocument(), remote).Save!.Status);
    }

    [Fact]
    public void DiffersFrom_IsFalseAfterRoundTrip()
    {
        var doc = Doc((0, "Ring_A", true, 1), (2, "MetaGem_B", null, 3));
        doc.Save = new SaveSnapshot { ReadAt = T0, Characters = [new CharacterSnapshot { Index = 0, Owned = ["Ring_A"] }] };
        doc.AdventureWorld = new Stamped<string?>("World_Jungle", T0);

        var remote = SyncDocument.Parse(doc.ToJson());

        Assert.False(SyncMerge.DiffersFrom(SyncMerge.Merge(doc, remote), remote));
        Assert.True(SyncMerge.DiffersFrom(SyncMerge.Merge(Doc((0, "Ring_A", false, 5)), remote), remote));
        Assert.True(SyncMerge.DiffersFrom(doc, null));
    }
}
