using System.Text.Json;

namespace Remnant2Overlay.Planner;

/// <summary>A point a trait gets for free from an archetype, e.g. Alchemist gives Potency 10.</summary>
public sealed record LinkedTrait(string Name, int Points);

/// <summary>One item from planner.json (built by tools/build_planner_data.py from the Remnant 2 Toolkit).</summary>
public sealed record PlannerItem
{
    /// <summary>Asset name as it appears in the save (Weapon_TommyGun), or "kind:Name" when the toolkit has none.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>weapon, helm, torso, legs, gloves, ring, amulet, relic, fragment, mutator, mod, archetype, skill,
    /// trait or concoction.</summary>
    public required string Kind { get; init; }

    /// <summary>Weapon type (long gun, hand gun, melee), mutator type (gun, melee), fragment color, trait type
    /// (archetype, core, trait).</summary>
    public string? Sub { get; init; }
    public string? Set { get; init; }
    public string? Dlc { get; init; }
    public string? Desc { get; init; }
    public string? MaxLevelBonus { get; init; }
    public List<string>? Tags { get; init; }
    public string? Where { get; init; }
    public Dictionary<string, double> Stats { get; init; } = [];
    public double[]? WeightThresholds { get; init; }
    public string? LinkedMod { get; init; }
    public string? LinkedArchetype { get; init; }
    public List<string>? LinkedSkills { get; init; }
    public List<LinkedTrait>? LinkedTraits { get; init; }

    public double Stat(string key) => Stats.GetValueOrDefault(key);

    /// <summary>Boss and other special weapons come with a fixed mod and upgrade to +10 (with Simulacrum).</summary>
    public bool IsSpecialWeapon => Kind == "weapon" && LinkedMod is not null;

    public int MaxLevel => Kind == "weapon" ? IsSpecialWeapon ? 10 : 20 : 0;

    public bool IsMelee => Sub == "melee";

    /// <summary>True when the save reader can tell whether a character has it (items.json tracks it).</summary>
    public bool HasAssetName => !Id.Contains(':');
}

public sealed class PlannerDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, PlannerItem> _byId;
    private readonly Dictionary<(string Kind, string Name), PlannerItem> _byName;

    public string Source { get; }
    public IReadOnlyList<PlannerItem> Items { get; }

    public PlannerDatabase(string source, IEnumerable<PlannerItem> items)
    {
        Source = source;
        Items = items.ToList();
        _byId = Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        _byName = Items.GroupBy(i => (i.Kind, i.Name.ToLowerInvariant())).ToDictionary(g => g.Key, g => g.First());
    }

    public static PlannerDatabase Parse(string json)
    {
        var file = JsonSerializer.Deserialize<PlannerFile>(json, JsonOptions) ?? throw new InvalidDataException("Empty planner data.");
        return new PlannerDatabase(file.Source ?? "", file.Items ?? []);
    }

    public PlannerItem? Find(string? id) => id is null ? null : _byId.GetValueOrDefault(id);

    public PlannerItem? Named(string kind, string? name) =>
        name is null ? null : _byName.GetValueOrDefault((kind, name.ToLowerInvariant()));

    public IEnumerable<PlannerItem> Of(string kind) => Items.Where(i => i.Kind == kind).OrderBy(i => i.Name);

    private sealed record PlannerFile(string? Source, List<PlannerItem>? Items);
}
