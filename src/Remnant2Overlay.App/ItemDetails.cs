using System.Windows.Media;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.App;

/// <summary>One line in the details panel's requirement list, with a ✓ / ✗ / ? mark.</summary>
public sealed record RequirementRow(string Label, bool? Met)
{
    public string Mark => Met switch { true => "✓", false => "✗", null => "?" };
}

/// <summary>Everything the details panel shows for the selected item: requirements, quest and where to get it.</summary>
public sealed class ItemDetails
{
    public ItemDetails(ItemTileViewModel tile, CharacterRuns? character, RunInfo? run)
    {
        var item = tile.Item;
        Tile = tile;
        Name = item.DisplayName;
        Subtitle = $"{tile.CategoryName} · {item.WorldName}";
        Status = tile.DisplayStatus;
        Reason = tile.IsManual ? $"{tile.Reason} (marked {(tile.Owned ? "owned" : "missing")} manually)" : tile.Reason;
        Where = tile.Where;
        Note = item.Note;

        if (item.DropType is not null)
        {
            string reference = item.DropReference is null ? "" : RunAnalysis.NameOf(item.DropReference);
            Source = item.DropType switch
            {
                "Vendor" => $"Sold by {reference}",
                "World" => $"Random drop in {reference.Replace("World_", "")}",
                "Global" => "Random drop anywhere",
                "Start" => "Starting gear",
                _ => reference.Length == 0 ? item.DropType : $"{item.DropType}: {reference}",
            };
            // Events and quests: say whether the character has already finished it.
            if (item.DropReference is { } quest && quest.StartsWith("Quest_", StringComparison.Ordinal) && character?.CompletedQuestIds is { } done)
                QuestState = done.Contains(quest) ? "You have completed this quest/event before." : "Not completed yet on this character.";
        }

        Requirements = RunAnalysis.CheckPrerequisites(item.Prerequisite, character, run)
            .Select(r => new RequirementRow(r.Label, r.Met)).ToList();
        if (item.Material is { } material)
            Requirements.Add(new RequirementRow(
                $"Craft from {RunAnalysis.NameOf(material)}",
                RunAnalysis.CheckPrerequisites(material, character, run).FirstOrDefault()?.Met));

        var flags = new List<string>();
        if (item.Difficulty is { } difficulty) flags.Add($"{difficulty} difficulty or higher");
        if (item.Hardcore) flags.Add("Hardcore only");
        if (item.Coop) flags.Add("Needs co-op");
        if (item.WeaponOrMod is { } linked) flags.Add($"Comes with {RunAnalysis.NameOf(linked)}");
        Extras = flags;
    }

    public ItemTileViewModel Tile { get; }
    public ImageSource? Icon => Tile.Icon;
    public string Name { get; }
    public string Subtitle { get; }
    public RunItemStatus Status { get; }
    public string Reason { get; }
    public string? Where { get; }
    public string? Source { get; }
    public string? QuestState { get; }
    public List<RequirementRow> Requirements { get; }
    public bool HasRequirements => Requirements.Count > 0;
    public IReadOnlyList<string> Extras { get; }
    public string? Note { get; }
}

/// <summary>Hides an element when its bound value is null or empty.</summary>
public sealed class NullToCollapsed : System.Windows.Data.IValueConverter
{
    public static readonly NullToCollapsed Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
