using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.Icons;

public sealed record IconSyncProgress(int Done, int Total, string AssetName, string? IconPath);

public sealed record IconSyncResult(int Extracted, int NotFound);

/// <summary>Fills the icon cache with whatever icons are missing. Safe to call on every start-up.</summary>
public static class IconSync
{
    public static IconSyncResult Run(
        IconCache cache,
        IEnumerable<ItemInfo> items,
        IconExtractorOptions options,
        IProgress<IconSyncProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        string signature = IconExtractor.PaksSignature(options.PaksDir);
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
        using var extractor = new IconExtractor(options);
        try
        {
            foreach (var item in todo)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outFile = cache.PathFor(item.AssetName);
                string? source = extractor.Extract(item, outFile, log);
                if (source is null)
                {
                    index.Missing.Add(item.AssetName);
                    notFound++;
                }
                else
                {
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

        return new IconSyncResult(extracted, notFound);
    }
}
