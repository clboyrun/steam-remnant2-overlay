namespace Remnant2Overlay.Planner;

/// <summary>A planned character: what it wears and wields, and how its trait points are spent. Items are
/// <see cref="PlannerItem.Id"/>s.</summary>
public sealed class Build
{
    public const int TraitPointLimit = 110;
    public const int MaxTraitPoints = 10;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New build";

    /// <summary>Primary then secondary archetype.</summary>
    public string?[] Archetypes { get; set; } = new string?[2];

    /// <summary>The skill picked for each archetype.</summary>
    public string?[] Skills { get; set; } = new string?[2];

    public WeaponSlot[] Weapons { get; set; } = [new() { Slot = "long gun" }, new() { Slot = "hand gun" }, new() { Slot = "melee" }];

    public string? Helm { get; set; }
    public string? Torso { get; set; }
    public string? Legs { get; set; }
    public string? Gloves { get; set; }
    public string? Amulet { get; set; }
    public string?[] Rings { get; set; } = new string?[4];
    public string? Relic { get; set; }
    public string?[] Fragments { get; set; } = new string?[3];

    /// <summary>Points the player spends on each trait, on top of what the archetypes give.</summary>
    public Dictionary<string, int> Traits { get; set; } = [];

    /// <summary>Which character to compare with: "overlay:N" (synced from the PC) or "device:id" (kept on this device).</summary>
    public string? LinkedCharacter { get; set; }

    /// <summary>Share of shots that hit a weakspot, 0 to 1, for expected damage.</summary>
    public double WeakspotHitRate { get; set; }

    /// <summary>Effect keys flipped from their default (conditional effects default off, the rest on).</summary>
    public HashSet<string> FlippedEffects { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public IEnumerable<string?> Armor => [Helm, Torso, Legs, Gloves];

    /// <summary>Everything worn or carried that has its own stats, weapons and their parts included.</summary>
    public IEnumerable<string> Gear =>
        Armor.Append(Amulet).Concat(Rings).Append(Relic).Concat(Fragments)
            .Concat(Weapons.SelectMany(w => new[] { w.Weapon, w.Mod, w.Mutator }))
            .OfType<string>();
}

public sealed class WeaponSlot
{
    /// <summary>long gun, hand gun or melee.</summary>
    public string Slot { get; set; } = "";
    public string? Weapon { get; set; }

    /// <summary>Upgrade level: +0 to +20, or +10 for special weapons.</summary>
    public int Level { get; set; }

    /// <summary>Only for standard ranged weapons; special weapons have theirs built in.</summary>
    public string? Mod { get; set; }
    public string? Mutator { get; set; }
}
