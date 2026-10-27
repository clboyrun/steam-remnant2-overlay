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
