using Remnant2Overlay.Icons;
using Remnant2Overlay.SaveReader;

// Extracts item icons from your own Remnant 2 install into the overlay's icon cache.
// The overlay does this by itself on start-up; this tool is for re-running or debugging it.
//
//   Remnant2Overlay.IconExtractor [--paks <dir>] [--out <dir>] [--game GAME_UE5_2]
//                                 [--usmap <file>] [--aes <0x key>] [--list] [--force]
//
// --list   only prints every texture whose name contains "icon", which helps when matching fails.
// --force  clears the cache first and extracts everything again.

string? paks = null, outDir = null, usmap = null, aes = null, game = null;
bool list = false, force = false;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--paks": paks = Next(); break;
        case "--out": outDir = Next(); break;
        case "--usmap": usmap = Next(); break;
        case "--aes": aes = Next(); break;
        case "--game": game = Next(); break;
        case "--list": list = true; break;
        case "--force": force = true; break;
        default:
            Console.Error.WriteLine($"Unknown option {args[i]}");
            return 1;
    }
}

paks = paks is null ? GameLocator.FindPaksFolder() : GameLocator.NormalizePaksFolder(paks);
if (paks is null)
{
    Console.Error.WriteLine("Could not find Remnant 2. Pass --paks <...\\Remnant2\\Remnant2\\Content\\Paks>.");
    return 1;
}

outDir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Remnant2Overlay", "icons");
usmap ??= GameLocator.FindMappingsFile(paks,
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Remnant2Overlay"));
var options = new IconExtractorOptions(paks, game, usmap, aes);
Console.WriteLine($"Usmap:  {usmap ?? "none"}");
Console.WriteLine($"Paks:   {paks}");
Console.WriteLine($"Output: {outDir}");

if (list)
{
    using var extractor = new IconExtractor(options);
    var paths = extractor.IconPackagePaths.Order().ToList();
    foreach (string path in paths) Console.WriteLine(path);
    Console.WriteLine($"{paths.Count} icon-like packages out of {extractor.MountedFileCount} files.");
    return 0;
}

var cache = new IconCache(outDir);
if (force) cache.Clear();

var progress = new Progress<IconSyncProgress>(p =>
    Console.WriteLine($"[{p.Done}/{p.Total}] {(p.IconPath is null ? "MISSING" : "ok     ")} {p.AssetName}"));
var stage = new Progress<string>(Console.WriteLine);
var result = IconSync.Run(cache, ItemDatabase.LoadEmbedded().Items, options, progress, msg => Console.Error.WriteLine("  " + msg),
    stage: stage);
Console.WriteLine($"Done: {result.Extracted} extracted, {result.NotFound} not found. See index.json in the output folder.");
if (result.NeedsMappings)
    Console.WriteLine("The game needs a mappings file (.usmap) to read textures. Pass --usmap <file>; see README.");
return 0;
