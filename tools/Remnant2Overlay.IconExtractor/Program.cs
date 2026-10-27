using System.Text.Json;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using Remnant2Overlay.SaveReader;

// Extracts item icons from your own Remnant 2 install into PNG files named after each item.
//
//   Remnant2Overlay.IconExtractor [--paks <dir>] [--out <dir>] [--game GAME_UE5_2]
//                                 [--usmap <file>] [--aes <0x key>] [--list]
//
// --list only prints every texture whose name contains "icon", which helps when matching fails.

var options = Options.Parse(args);
if (options is null) return 1;

Console.WriteLine($"Paks:   {options.PaksDir}");
Console.WriteLine($"Output: {options.OutDir}");

var provider = new DefaultFileProvider(options.PaksDir, SearchOption.TopDirectoryOnly,
    new VersionContainer(options.Game), StringComparer.OrdinalIgnoreCase);
if (options.UsmapPath is not null)
    provider.MappingsContainer = new FileUsmapTypeMappingsProvider(options.UsmapPath, StringComparer.OrdinalIgnoreCase);
provider.Initialize();
provider.SubmitKey(new FGuid(), new FAesKey(options.AesKey ?? "0x" + new string('0', 64)));
Console.WriteLine($"Mounted {provider.Files.Count} files.");
TextureDecoder.UseAssetRipperTextureDecoder = true;

// Index every package by its /Game path so item folders can be searched directly.
var packages = provider.Files.Values
    .Where(f => f.Extension.Equals("uasset", StringComparison.OrdinalIgnoreCase))
    .Select(f => (File: f, GamePath: ToGamePath(f.PathWithoutExtension)))
    .Where(p => p.GamePath is not null)
    .ToList();

var iconPackages = packages.Where(p => p.File.NameWithoutExtension.Contains("icon", StringComparison.OrdinalIgnoreCase)).ToList();
if (options.ListOnly)
{
    foreach (var p in iconPackages.OrderBy(p => p.GamePath)) Console.WriteLine(p.GamePath);
    Console.WriteLine($"{iconPackages.Count} icon-like packages.");
    return 0;
}

Directory.CreateDirectory(options.OutDir);
var db = ItemDatabase.LoadEmbedded();
var index = new Dictionary<string, string>();
var missing = new List<string>();

foreach (var item in db.Items)
{
    string folder = item.ProfileId[..item.ProfileId.LastIndexOf('/')];
    string stem = item.AssetName[(item.AssetName.IndexOf('_') + 1)..].Replace("_", "");

    // Candidates: icon-named textures in the item's folder tree, then icon-named textures anywhere that
    // mention the item. Order by best name match first.
    var candidates = iconPackages
        .Where(p => p.GamePath!.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                    || Normalize(p.File.NameWithoutExtension).Contains(Normalize(stem), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p => Normalize(p.File.NameWithoutExtension).Contains(Normalize(stem), StringComparison.OrdinalIgnoreCase))
        .ThenBy(p => p.GamePath!.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(p => p.File.NameWithoutExtension.Length);

    string? written = null;
    foreach (var (file, gamePath) in candidates)
    {
        try
        {
            if (!provider.TryLoadPackageObject<UTexture2D>($"{file.PathWithoutExtension}.{file.NameWithoutExtension}", out var texture))
                continue;
            var bitmap = texture.Decode(ETexturePlatform.DesktopMobile);
            if (bitmap is null) continue;
            byte[] png = TextureEncoder.Encode(bitmap, ETextureFormat.Png, false, out _, 100);
            File.WriteAllBytes(Path.Combine(options.OutDir, item.AssetName + ".png"), png);
            written = gamePath;
            break;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"  {gamePath}: {e.Message}");
        }
    }

    if (written is null) missing.Add(item.AssetName);
    else index[item.AssetName] = written;
    Console.WriteLine($"{(written is null ? "MISSING" : "ok     ")} {item.AssetName}{(written is null ? "" : "  <- " + written)}");
}

File.WriteAllText(Path.Combine(options.OutDir, "index.json"),
    JsonSerializer.Serialize(new { icons = index, missing }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Done: {index.Count} icons written, {missing.Count} missing. See index.json.");
return 0;

static string? ToGamePath(string pathWithoutExtension)
{
    // Pak paths look like Remnant2/Content/World_Base/... ; the game refers to them as /Game/World_Base/...
    int i = pathWithoutExtension.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
    return i < 0 ? null : "/Game/" + pathWithoutExtension[(i + "/Content/".Length)..];
}

static string Normalize(string s) => s.Replace("_", "").Replace("'", "");

sealed record Options(string PaksDir, string OutDir, EGame Game, string? UsmapPath, string? AesKey, bool ListOnly)
{
    public static string DefaultOutDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Remnant2Overlay", "icons");

    private const string DefaultPaks = @"C:\Program Files (x86)\Steam\steamapps\common\Remnant2\Remnant2\Content\Paks";

    public static Options? Parse(string[] args)
    {
        string paks = DefaultPaks, outDir = DefaultOutDir;
        string? usmap = null, aes = null;
        var game = EGame.GAME_UE5_2;
        bool list = false;

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--paks": paks = Next(); break;
                case "--out": outDir = Next(); break;
                case "--usmap": usmap = Next(); break;
                case "--aes": aes = Next(); break;
                case "--game": game = Enum.Parse<EGame>(Next()); break;
                case "--list": list = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option {args[i]}");
                    return null;
            }
        }

        if (!Directory.Exists(paks))
        {
            Console.Error.WriteLine($"Paks folder not found: {paks}. Pass --paks <Remnant2\\Remnant2\\Content\\Paks>.");
            return null;
        }

        return new Options(paks, outDir, game, usmap, aes, list);
    }
}
