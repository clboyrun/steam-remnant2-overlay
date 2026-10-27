using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.Icons;

/// <param name="UnrealVersion">CUE4Parse EGame name such as GAME_UE5_2; null or unknown uses GAME_UE5_2.</param>
public sealed record IconExtractorOptions(
    string PaksDir,
    string? UnrealVersion = null,
    string? UsmapPath = null,
    string? AesKey = null)
{
    // Kept internal so callers don't need a CUE4Parse reference of their own.
    internal EGame Game => Enum.TryParse<EGame>(UnrealVersion, ignoreCase: true, out var game) ? game : EGame.GAME_UE5_2;
}

/// <summary>Reads item icons out of the user's own Remnant 2 paks with CUE4Parse.</summary>
public sealed class IconExtractor : IDisposable
{
    private readonly DefaultFileProvider _provider;
    private readonly List<(GameFile File, string GamePath)> _iconPackages;

    public IconExtractor(IconExtractorOptions options)
    {
        _provider = new DefaultFileProvider(options.PaksDir, SearchOption.TopDirectoryOnly,
            new VersionContainer(options.Game), StringComparer.OrdinalIgnoreCase);
        if (options.UsmapPath is not null)
            _provider.MappingsContainer = new FileUsmapTypeMappingsProvider(options.UsmapPath, StringComparer.OrdinalIgnoreCase);
        _provider.Initialize();
        _provider.SubmitKey(new FGuid(), new FAesKey(options.AesKey ?? "0x" + new string('0', 64)));
        TextureDecoder.UseAssetRipperTextureDecoder = true;

        // Only packages with "icon" in the name are candidates; indexing them up front keeps lookups cheap.
        _iconPackages = _provider.Files.Values
            .Where(f => f.Extension.Equals("uasset", StringComparison.OrdinalIgnoreCase)
                        && f.NameWithoutExtension.Contains("icon", StringComparison.OrdinalIgnoreCase))
            .Select(f => (File: f, GamePath: ToGamePath(f.PathWithoutExtension)))
            .Where(p => p.GamePath is not null)
            .Select(p => (p.File, p.GamePath!))
            .ToList();
    }

    public int MountedFileCount => _provider.Files.Count;

    public IEnumerable<string> IconPackagePaths => _iconPackages.Select(p => p.GamePath);

    /// <summary>
    /// Signature of the paks folder (names, sizes, timestamps). When it changes, icons that were not found
    /// before are worth looking for again.
    /// </summary>
    public static string PaksSignature(string paksDir) => string.Join("|",
        Directory.EnumerateFiles(paksDir)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));

    /// <summary>Writes the item's icon as PNG to <paramref name="outFile"/>. Returns the source asset path, or null.</summary>
    public string? Extract(ItemInfo item, string outFile, Action<string>? log = null)
    {
        string folder = item.ProfileId[..item.ProfileId.LastIndexOf('/')];
        string stem = Normalize(item.AssetName[(item.AssetName.IndexOf('_') + 1)..]);

        // Icon-named textures in the item's folder tree, then icon-named textures anywhere that mention the item.
        var candidates = _iconPackages
            .Select(p => (p.File, p.GamePath,
                InFolder: p.GamePath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase),
                NameMatch: Normalize(p.File.NameWithoutExtension).Contains(stem, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p.InFolder || p.NameMatch)
            .OrderByDescending(p => p.NameMatch)
            .ThenByDescending(p => p.InFolder)
            .ThenBy(p => p.File.NameWithoutExtension.Length);

        int tried = 0;
        foreach (var (file, gamePath, _, _) in candidates)
        {
            tried++;
            try
            {
                // LoadPackageObject (not TryLoad…) so the reason for a failure ends up in the log.
                var export = _provider.LoadPackageObject($"{file.PathWithoutExtension}.{file.NameWithoutExtension}");
                if (export is not UTexture2D texture)
                {
                    log?.Invoke($"  skip {gamePath}: {export.ExportType}, not a texture");
                    continue;
                }
                var bitmap = texture.Decode(ETexturePlatform.DesktopMobile);
                if (bitmap is null)
                {
                    log?.Invoke($"  skip {gamePath}: texture could not be decoded");
                    continue;
                }

                byte[] png = TextureEncoder.Encode(bitmap, ETextureFormat.Png, false, out _, 100);
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                // Write to a temp file first so the UI never loads a half-written PNG.
                string temp = outFile + ".tmp";
                File.WriteAllBytes(temp, png);
                File.Move(temp, outFile, overwrite: true);
                return gamePath;
            }
            catch (Exception e)
            {
                log?.Invoke($"  error {gamePath}: {e.GetType().Name}: {e.Message}");
            }
        }

        if (tried == 0) log?.Invoke($"  no candidates (folder {folder}, name containing \"{stem}\")");
        return null;
    }

    public void Dispose() => _provider.Dispose();

    private static string? ToGamePath(string pathWithoutExtension)
    {
        // Pak paths look like Remnant2/Content/World_Base/... ; the game refers to them as /Game/World_Base/...
        int i = pathWithoutExtension.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? null : "/Game/" + pathWithoutExtension[(i + "/Content/".Length)..];
    }

    private static string Normalize(string s) => s.Replace("_", "").Replace("'", "");
}
