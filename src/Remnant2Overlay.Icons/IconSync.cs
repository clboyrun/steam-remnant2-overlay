using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.Icons;

public sealed record IconSyncProgress(int Done, int Total, string AssetName, string? IconPath);

public sealed record IconSyncResult(int Extracted, int NotFound, bool NeedsMappings = false);

/// <summary>Fills the icon cache with whatever icons are missing. Safe to call on every start-up.</summary>
public static class IconSync
{
    public static IconSyncResult Run(
        IconCache cache,
        IEnumerable<ItemInfo> items,
        IconExtractorOptions options,
        IProgress<IconSyncProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? stage = null)
    {
        // Include the mappings file: adding one makes earlier misses worth retrying.
        string signature = IconExtractor.PaksSignature(options.PaksDir)
                           + (options.UsmapPath is { } usmap && File.Exists(usmap) ? $"|usmap:{new FileInfo(usmap).Length}" : "");
        var index = cache.LoadIndex();
        var todo = cache.ItemsToExtract(items, index, signature);
        if (todo.Count == 0) return new IconSyncResult(0, 0);

        if (index.PaksSignature != signature)
        {
            // Game files changed (or first run): earlier misses may exist now.
            index.Missing.Clear();
            index.PaksSignature = signature;
        }

        int extracted = 0, notFound = 0, done = 0;
        void Stage(string message)
        {
            log?.Invoke(message);
            stage?.Report(message);
        }

        Stage($"Reading game files to find {todo.Count} icons (can take a minute)…");
        using var extractor = new IconExtractor(options);
        int iconTextures = extractor.IconPackagePaths.Count();
        Stage($"Indexed {extractor.MountedFileCount:N0} game files, {iconTextures:N0} icon textures. Extracting…");
        // A sample of what icon packages are called, to tune matching from the log alone.
        var paths = extractor.IconPackagePaths.Order().ToList();
        foreach (string path in paths.Where(p => p.Contains("/Items/", StringComparison.OrdinalIgnoreCase)).Take(40)
                     .Concat(paths.Where(p => !p.Contains("/Items/", StringComparison.OrdinalIgnoreCase)).Take(40)))
            log?.Invoke($"  sample {path}");
        try
        {
            foreach (var item in todo)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outFile = cache.PathFor(item.AssetName);
                string? source = extractor.Extract(item, outFile, log);
                if (source is null)
                {
                    log?.Invoke($"MISSING {item.AssetName} ({item.ProfileId})");
                    // Without mappings nothing can be read, so don't remember these as "no icon".
                    if (!extractor.NeedsMappings) index.Missing.Add(item.AssetName);
                    notFound++;
                }
                else
                {
                    log?.Invoke($"ok {item.AssetName} <- {source}");
                    index.Icons[item.AssetName] = source;
                    index.Missing.Remove(item.AssetName);
                    extracted++;
                }

                done++;
                progress?.Report(new IconSyncProgress(done, todo.Count, item.AssetName, source is null ? null : outFile));

                // Save as we go so a closed app keeps what it already found.
                if (done % 50 == 0) cache.SaveIndex(index);
            }
        }
        finally
        {
            cache.SaveIndex(index);
        }

        return new IconSyncResult(extracted, notFound, extractor.NeedsMappings);
    }
}
