using System.Text.Json;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.Icons;

/// <summary>
/// Local folder of extracted icons ({AssetName}.png) plus index.json, which remembers where each icon came
/// from and which items had no icon, so those aren't searched again until the game files change.
/// </summary>
public sealed class IconCache(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Directory { get; } = directory;

    public string PathFor(string assetName) => Path.Combine(Directory, assetName + ".png");

    public bool Has(string assetName) => File.Exists(PathFor(assetName));

    public IconIndex LoadIndex()
    {
        try
        {
            string path = IndexPath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<IconIndex>(File.ReadAllText(path)) ?? new IconIndex();
        }
        catch (JsonException)
        {
            // A corrupt index only costs a re-scan.
        }
        return new IconIndex();
    }

    public void SaveIndex(IconIndex index)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOptions));
    }

    /// <summary>Items still worth extracting: no PNG yet, and not already known to have no icon in these paks.</summary>
    public IReadOnlyList<ItemInfo> ItemsToExtract(IEnumerable<ItemInfo> items, IconIndex index, string paksSignature)
    {
        bool sameGameFiles = index.PaksSignature == paksSignature;
        return items
            .Where(i => !Has(i.AssetName))
            .Where(i => !(sameGameFiles && index.Missing.Contains(i.AssetName)))
            .ToList();
    }

    /// <summary>Forget everything so the next run extracts all icons again.</summary>
    public void Clear()
    {
        if (!System.IO.Directory.Exists(Directory)) return;
        foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "*.png")) File.Delete(file);
        if (File.Exists(IndexPath)) File.Delete(IndexPath);
    }

    private string IndexPath => Path.Combine(Directory, "index.json");
}

public sealed class IconIndex
{
    public string? PaksSignature { get; set; }
    public Dictionary<string, string> Icons { get; set; } = [];
    public HashSet<string> Missing { get; set; } = [];
}
