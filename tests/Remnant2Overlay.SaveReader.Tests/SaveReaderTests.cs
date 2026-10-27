using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.SaveReader.Tests;

public class SaveReaderTests
{
    private const string CharacterPath =
        "/Game/Characters/Player/Base/Character_Master_Player.Character_Master_Player_C";

    [Fact]
    public void Decompress_JoinsAllChunks()
    {
        byte[] payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();

        byte[] result = SaveFile.Decompress(BuildSave(payload, chunkSize: 131_072));

        Assert.Equal(payload, result);
    }

    [Fact]
    public void Decompress_RejectsMissingChunkTag()
    {
        byte[] save = BuildSave(new byte[100], chunkSize: 131_072);
        save[SaveFile.FileHeaderSize] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => SaveFile.Decompress(save));
    }

    [Fact]
    public void Read_FindsItemsPerCharacter()
    {
        byte[] data = Concat(
            FString("/Game/World_Base/Items/Weapons/Before/Weapon_Ignored.Weapon_Ignored_C"),
            Character(
                "/Game/World_Base/Items/Weapons/HandGuns/Standard/SMG/Weapon_SMG.Weapon_SMG_C",
                "/Game/World_Base/Items/Trinkets/Rings/AkariWarBand/Ring_AkariWarBand.Ring_AkariWarBand_C",
                "/Game/World_Base/Items/Gems/Mutators/Resentment/MetaGem_Resentment.MetaGem_Resentment_C",
                "/Game/World_Base/Items/Trinkets/Rings/AkariWarBand/Ring_AkariWarBand.Ring_AkariWarBand_C",
                "/Game/World_Base/Items/Materials/Scraps/Material_Scraps.Material_Scraps_C"),
            Character(
                "/Game/World_Base/Items/Armor/Void/Armor_Body_Void.Armor_Body_Void_C"));

        var characters = ProfileReader.Read(data);

        Assert.Equal(2, characters.Count);
        Assert.Equal(
            ["Weapon_SMG", "Ring_AkariWarBand", "MetaGem_Resentment"],
            characters[0].Items.Select(i => i.AssetName));
        Assert.Equal(ItemCategory.Mutator, characters[0].Items[2].Category);
        var body = Assert.Single(characters[1].Items);
        Assert.Equal(ItemCategory.Body, body.Category);
    }

    [Fact]
    public void Read_RealProfile_WhenAvailable()
    {
        // Set REMNANT2_PROFILE to a copy of a real profile.sav to run this against real data.
        string? path = Environment.GetEnvironmentVariable("REMNANT2_PROFILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var characters = ProfileReader.ReadFile(path);

        Assert.NotEmpty(characters);
        Assert.All(characters, c => Assert.Contains(c.Items, i => i.Category == ItemCategory.Weapon));
    }

    private static byte[] Character(params string[] itemPaths) =>
        Concat([FString(CharacterPath), .. itemPaths.Select(FString), FString("Character_Master_Player_C")]);

    private static byte[] FString(string s)
    {
        byte[] text = Encoding.ASCII.GetBytes(s + "\0");
        var result = new byte[4 + text.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, text.Length);
        text.CopyTo(result, 4);
        return result;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] BuildSave(byte[] payload, int chunkSize)
    {
        using var output = new MemoryStream();
        output.Write(new byte[SaveFile.FileHeaderSize]);

        for (int offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var chunk = payload.AsSpan(offset, Math.Min(chunkSize, payload.Length - offset));
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(chunk);

            var header = new byte[SaveFile.ChunkHeaderSize];
            BinaryPrimitives.WriteUInt64LittleEndian(header, SaveFile.ChunkTag);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), chunkSize);
            header[16] = 3;
            for (int i = 0; i < 2; i++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(17 + i * 16), compressed.Length);
                BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(25 + i * 16), chunk.Length);
            }

            output.Write(header);
            output.Write(compressed.ToArray());
        }

        return output.ToArray();
    }
}

public class ItemDatabaseTests
{
    [Fact]
    public void LoadEmbedded_CategorizesEveryItem()
    {
        var db = ItemDatabase.LoadEmbedded();

        Assert.True(db.Items.Count > 600);
        foreach (var category in Enum.GetValues<ItemCategory>().Except([ItemCategory.RelicFragment]))
            Assert.Contains(db.Items, i => i.Category == category);
        Assert.Equal("Akari War Band", db.Find("Ring_AkariWarBand")?.DisplayName);
        Assert.Equal("Bruiser Bodyplate", db.Find("Armor_Body_Challenger")?.DisplayName);
    }

    [Fact]
    public void Compare_MarksOwnedItems()
    {
        var db = ItemDatabase.LoadEmbedded();
        var character = new CharacterInventory(0,
            [new OwnedItem("Ring_AkariWarBand", "", ItemCategory.Ring)]);

        var entries = db.Compare(character);

        Assert.Equal(db.Items.Count, entries.Count);
        Assert.Single(entries, e => e.Owned);
        Assert.True(entries.Single(e => e.Item.AssetName == "Ring_AkariWarBand").Owned);
    }
}

public class RunAnalysisTests
{
    private static readonly ItemDatabase Db = ItemDatabase.LoadEmbedded();

    private static RunItem Item(string id, RunItemStatus status, string? prerequisite = null) =>
        new(id, id, Db.Items.First(i => i.Id == id).Type, status, prerequisite, false);

    private static CharacterRuns Character(RunInfo run, params string[] ownedIds) => new(
        0, "Test", Db.Items.Where(i => ownedIds.Contains(i.Id)).Select(i => i.ProfileId).ToHashSet(),
        lib.remnant2.analyzer.Enums.WorldSlot.Adventure, run, run, []);

    private static RunInfo Run(params RunItem[] items) => new(
        lib.remnant2.analyzer.Enums.WorldSlot.Adventure, "Survivor", null,
        [new RunZone("Losomn", "The One True King", false,
            [new RunLocation("Forlorn Coast", [], [new RunSource("Forlorn Coast", "Location", items)])])],
        []);

    [Fact]
    public void Evaluate_ClassifiesEachItem()
    {
        var run = Run(
            Item("Ring_AkariWarBand", RunItemStatus.Available),
            Item("Weapon_Monarch", RunItemStatus.NeedsPrerequisite, "Crown of the Red Prince"),
            Item("Weapon_Wrathbringer", RunItemStatus.MissedThisRun));
        var character = Character(run, "Ring_AkariWarBand");

        var statuses = RunAnalysis.Evaluate(Db, character, run).ToDictionary(s => s.Item.Id);

        Assert.Equal(RunItemStatus.Owned, statuses["Ring_AkariWarBand"].Status);
        Assert.Equal(RunItemStatus.NeedsPrerequisite, statuses["Weapon_Monarch"].Status);
        Assert.Contains("Crown of the Red Prince", statuses["Weapon_Monarch"].Reason);
        Assert.Equal(RunItemStatus.MissedThisRun, statuses["Weapon_Wrathbringer"].Status);

        // Losomn is in the run, so a Losomn item that isn't listed simply didn't roll.
        var otherLosomn = statuses.Values.First(s => s.Item.World == "World_Fae" && s.Status != RunItemStatus.Owned
                                                     && s.Item.Id is not ("Weapon_Monarch" or "Weapon_Wrathbringer"));
        Assert.Equal(RunItemStatus.NotRolled, otherLosomn.Status);
        Assert.Contains(statuses.Values, s => s.Item.World == "World_Jungle" && s.Status == RunItemStatus.WorldNotInRun);
    }

    [Fact]
    public void Evaluate_PrefersAvailableOccurrence()
    {
        var run = Run(Item("Ring_AkariWarBand", RunItemStatus.MissedThisRun), Item("Ring_AkariWarBand", RunItemStatus.Available));

        var status = RunAnalysis.Evaluate(Db, Character(run), run).Single(s => s.Item.Id == "Ring_AkariWarBand");

        Assert.Equal(RunItemStatus.Available, status.Status);
    }

    [Fact]
    public void Analyze_RealSaves_WhenAvailable()
    {
        // Set REMNANT2_PROFILE to a copy of profile.sav with its save_N.sav files next to it.
        string? path = Environment.GetEnvironmentVariable("REMNANT2_PROFILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var characters = RunAnalysis.Analyze(Path.GetDirectoryName(Path.GetFullPath(path))!);

        Assert.NotEmpty(characters);
        var run = characters[0].Get(characters[0].ActiveSlot)!;
        Assert.Contains(run.Zones, z => z.World != "Ward 13");
        Assert.Contains(RunAnalysis.Evaluate(Db, characters[0], run), s => s.Status == RunItemStatus.Available);

        // Every item with a prerequisite gets one check per comma-separated group, with readable names.
        foreach (var item in Db.Items.Where(i => i.Prerequisite is not null))
        {
            var checks = RunAnalysis.CheckPrerequisites(item.Prerequisite, characters[0], run);
            Assert.Equal(item.Prerequisite!.Split(',').Length, checks.Count);
            Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        }
    }

    [Fact]
    public void CheckPrerequisites_SplitsAllOfAndAnyOf()
    {
        var checks = RunAnalysis.CheckPrerequisites("Amulet_RedDoeSigil, Quest_Item_Train_AICore | Quest_Item_Extermination_AICore", null, null);

        Assert.Equal(2, checks.Count);
        Assert.Contains(" or ", checks[1].Label);
        Assert.All(checks, c => Assert.Null(c.Met)); // no character loaded: unknown, not "missing"
        Assert.Empty(RunAnalysis.CheckPrerequisites(null, null, null));
    }
}

public class IconCacheTests
{
    [Fact]
    public void ItemsToExtract_SkipsCachedAndKnownMissing_UntilGameFilesChange()
    {
        string dir = Path.Combine(Path.GetTempPath(), "r2o-icons-" + Guid.NewGuid());
        try
        {
            var db = ItemDatabase.LoadEmbedded();
            var items = db.Items.Take(3).ToList();
            var cache = new Remnant2Overlay.Icons.IconCache(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(cache.PathFor(items[0].AssetName), [1]);
            var index = new Remnant2Overlay.Icons.IconIndex { PaksSignature = "v1", Missing = [items[1].AssetName] };

            Assert.Equal([items[2]], cache.ItemsToExtract(items, index, "v1"));
            Assert.Equal([items[1], items[2]], cache.ItemsToExtract(items, index, "v2"));

            cache.SaveIndex(index);
            Assert.Contains(items[1].AssetName, cache.LoadIndex().Missing);
            cache.Clear();
            Assert.Equal(items, cache.ItemsToExtract(items, cache.LoadIndex(), "v1"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
