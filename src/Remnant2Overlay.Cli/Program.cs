using lib.remnant2.analyzer.Enums;
using Remnant2Overlay.SaveReader;

// Usage: Remnant2Overlay.Cli [path\to\profile.sav] [--items]
// With no path, uses the first Steam save folder under %USERPROFILE%\Saved Games\Remnant2\Steam.
// Prints owned/total per category and what the current campaign and adventure can still give you.
// --items also lists every owned item.
bool listItems = args.Contains("--items");
string? path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindDefaultProfile();
if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("Could not find profile.sav. Pass its path as the first argument.");
    return 1;
}

var db = ItemDatabase.LoadEmbedded();
var characters = RunAnalysis.Analyze(Path.GetDirectoryName(Path.GetFullPath(path))!);
Console.WriteLine($"{path}: {characters.Count} character(s)");

foreach (var character in characters)
{
    Console.WriteLine();
    Console.WriteLine($"== Character {character.Index + 1}: {character.Description} (playing {character.ActiveSlot}) ==");

    foreach (var run in new[] { character.Campaign, character.Adventure })
    {
        if (run is null) continue;
        var statuses = RunAnalysis.Evaluate(db, character, run);

        Console.WriteLine();
        Console.WriteLine($"-- {run.Slot} ({run.Difficulty}){(run.Slot == character.ActiveSlot ? " [active]" : "")} --");
        foreach (var zone in run.Zones.Where(z => z.World != "Ward 13"))
            Console.WriteLine($"  {zone.World}: {zone.Story} — {(zone.StoryFinished ? "story done" : "story in progress")}, " +
                              $"{zone.Locations.Count} locations");
        if (run.QuestItems.Count > 0)
            Console.WriteLine($"  Quest items held: {string.Join(", ", run.QuestItems)}");

        foreach (var group in statuses.Where(s => s.Status != RunItemStatus.Owned).GroupBy(s => s.Status).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key}: {group.Count()}");

        var available = statuses.Where(s => s.Status == RunItemStatus.Available).ToList();
        if (available.Count == 0)
            Console.WriteLine("  Nothing new to collect in this run.");
        foreach (var s in available.OrderBy(s => s.Where))
            Console.WriteLine($"    + {s.Item.DisplayName} — {s.Where}");
        foreach (var s in statuses.Where(s => s.Status == RunItemStatus.NeedsPrerequisite).OrderBy(s => s.Where))
            Console.WriteLine($"    ? {s.Item.DisplayName} — {s.Where} — {s.Reason}");
    }

    Console.WriteLine();
    foreach (var group in db.Items.GroupBy(i => i.Category).OrderBy(g => g.Key))
        Console.WriteLine($"{group.Key}: {group.Count(i => character.OwnedProfileIds.Contains(i.ProfileId))} / {group.Count()} owned");

    if (!listItems) continue;
    foreach (var item in db.Items.Where(i => character.OwnedProfileIds.Contains(i.ProfileId)).OrderBy(i => i.Category).ThenBy(i => i.DisplayName))
        Console.WriteLine($"  {item.Category}: {item.DisplayName}");
}

return 0;

static string? FindDefaultProfile()
{
    string root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Remnant2", "Steam");
    if (!Directory.Exists(root)) return null;
    return Directory.EnumerateFiles(root, "profile.sav", SearchOption.AllDirectories).FirstOrDefault();
}
