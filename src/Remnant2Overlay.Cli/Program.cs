using Remnant2Overlay.SaveReader;

// Usage: Remnant2Overlay.Cli [path\to\profile.sav]
// With no argument, uses the first Steam save folder under %USERPROFILE%\Saved Games\Remnant2\Steam.
string? path = args.Length > 0 ? args[0] : FindDefaultProfile();
if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("Could not find profile.sav. Pass its path as the first argument.");
    return 1;
}

var characters = ProfileReader.ReadFile(path);
var db = ItemDatabase.LoadEmbedded();
Console.WriteLine($"{path}: {characters.Count} character(s)");

foreach (var character in characters)
{
    Console.WriteLine();
    Console.WriteLine($"== Character {character.Index + 1} ({character.Items.Count} items) ==");
    foreach (var group in db.Compare(character).GroupBy(e => e.Item.Category).OrderBy(g => g.Key))
        Console.WriteLine($"{group.Key}: {group.Count(e => e.Owned)} / {group.Count()} owned");
    Console.WriteLine();
    foreach (var group in character.Items.GroupBy(i => i.Category).OrderBy(g => g.Key))
    {
        Console.WriteLine($"{group.Key} ({group.Count()}):");
        foreach (var item in group.OrderBy(i => i.AssetName))
            Console.WriteLine($"  {item.AssetName}");
    }
}

return 0;

static string? FindDefaultProfile()
{
    string root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Remnant2", "Steam");
    if (!Directory.Exists(root)) return null;
    return Directory.EnumerateFiles(root, "profile.sav", SearchOption.AllDirectories).FirstOrDefault();
}
