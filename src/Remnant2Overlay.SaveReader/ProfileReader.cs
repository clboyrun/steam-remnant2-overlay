using System.Text;
using System.Text.RegularExpressions;

namespace Remnant2Overlay.SaveReader;

public sealed record OwnedItem(string AssetName, string AssetPath, ItemCategory Category);

public sealed record CharacterInventory(int Index, IReadOnlyList<OwnedItem> Items)
{
    public IEnumerable<OwnedItem> InCategory(ItemCategory category) =>
        Items.Where(i => i.Category == category);
}

/// <summary>
/// Reads owned items per character from a decompressed profile.sav.
/// </summary>
/// <remarks>
/// This does not fully parse the Unreal object graph. Each character is a nested blob that starts
/// with the Character_Master_Player class path and ends with its own name table, where the class
/// name appears again as a bare length-prefixed string. Owned items are the item asset paths that
/// appear between those two markers.
/// </remarks>
public static partial class ProfileReader
{
    private const string CharacterClassPath =
        "/Game/Characters/Player/Base/Character_Master_Player.Character_Master_Player_C";

    // Length-prefixed FString as it appears in a name table: int32 length (incl. NUL), text, NUL.
    private static readonly byte[] CharacterNameTableEntry =
        [0x1A, 0x00, 0x00, 0x00, .. "Character_Master_Player_C\0"u8];

    [GeneratedRegex(@"/Game/[\x21-\x7E]+?\.([A-Za-z0-9_]+)_C(?=\x00)")]
    private static partial Regex ClassPathRegex();

    public static IReadOnlyList<CharacterInventory> ReadFile(string profilePath) =>
        Read(SaveFile.Decompress(profilePath));

    public static IReadOnlyList<CharacterInventory> Read(byte[] decompressed)
    {
        var characters = new List<CharacterInventory>();
        byte[] start = Encoding.ASCII.GetBytes(CharacterClassPath);
        int offset = 0;

        while (true)
        {
            int begin = IndexOf(decompressed, start, offset);
            if (begin < 0) break;

            int end = IndexOf(decompressed, CharacterNameTableEntry, begin);
            if (end < 0) end = decompressed.Length;

            characters.Add(new CharacterInventory(characters.Count, ExtractItems(decompressed, begin, end)));
            offset = end;
        }

        return characters;
    }

    internal static IReadOnlyList<OwnedItem> ExtractItems(byte[] data, int begin, int end)
    {
        // Latin1 keeps a 1:1 byte-to-char mapping so the regex can run over raw bytes.
        string text = Encoding.Latin1.GetString(data, begin, end - begin);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<OwnedItem>();

        foreach (Match m in ClassPathRegex().Matches(text))
        {
            string name = m.Groups[1].Value;
            if (ItemCategories.FromAssetName(name) is not { } category) continue;
            if (!m.Value.Contains("/Items/", StringComparison.Ordinal)) continue;
            if (!seen.Add(name)) continue;
            items.Add(new OwnedItem(name, m.Value, category));
        }

        return items;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from) =>
        haystack.AsSpan(from).IndexOf(needle) is var i and >= 0 ? from + i : -1;
}
