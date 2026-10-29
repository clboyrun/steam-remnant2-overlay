namespace Remnant2Overlay.Planner;

public sealed record TraitPoints(PlannerItem Trait, int FromArchetypes, int Spent)
{
    public int Total => Math.Min(Build.MaxTraitPoints, FromArchetypes + Spent);
}

public sealed record ActiveEffect(Effect Effect, bool On);

public sealed record WeaponDamage(
    WeaponSlot Slot,
    PlannerItem Weapon,
    double BaseAtLevel,
    double DamageBonus,
    double CritChance,
    double CritMultiplier,
    double WeakspotMultiplier,
    double FireRateBonus,
    double? ShotsPerSecond,
    double HitMin,
    double HitMax,
    double HitExpected)
{
    /// <summary>Body shots, no crits.</summary>
    public double? DpsMin => ShotsPerSecond * HitMin;

    /// <summary>Every shot a critical weakspot hit.</summary>
    public double? DpsMax => ShotsPerSecond * HitMax;

    /// <summary>Crit chance and the build's weakspot hit rate factored in.</summary>
    public double? DpsExpected => ShotsPerSecond * HitExpected;
}

public sealed record BuildStats(
    double Health,
    double Stamina,
    double Armor,
    double ArmorReduction,
    double FlatReduction,
    double TotalReduction,
    double Weight,
    double WeightThreshold,
    string WeightClass,
    IReadOnlyDictionary<string, double> Resistances,
    IReadOnlyList<TraitPoints> Traits,
    int TraitPointsUsed,
    IReadOnlyList<ActiveEffect> Effects,
    IReadOnlyList<WeaponDamage> Weapons);

/// <summary>
/// Totals for a build. Defense follows the Remnant 2 Toolkit's sums; damage follows remnant2.wiki.gg/wiki/Damage:
/// base × (1 + Σ damage) × (1 + 50% + Σ crit damage) × (1 + weapon weakspot + Σ weakspot), with buckets adding
/// inside and multiplying across. Upgrades add 10% of base per level (+20 max) or 20% for special weapons (+10 max).
/// </summary>
public static class BuildCalculator
{
    public const double BaseHealth = 100, BaseStamina = 100, BaseCritDamage = 0.5, MaxReduction = 0.8;
    public static readonly string[] Elements = ["Bleed", "Fire", "Shock", "Blight", "Toxin"];

    public static BuildStats Compute(Build build, PlannerDatabase db)
    {
        var gear = build.Gear.Select(db.Find).OfType<PlannerItem>().ToList();
        var traits = Traits(build, db);
        double Sum(string key) => gear.Sum(i => i.Stat(key));
        double Steps(string key) => traits.Sum(t => t.Trait.Stat(key) * t.Total);

        var effects = Effects(build, db, traits);
        double Bonus(EffectStat stat, PlannerItem? weapon = null) => effects
            .Where(e => e.On && e.Effect.Stat == stat && (!e.Effect.WeaponOnly || weapon is not null && OnWeapon(build, e.Effect.Source, weapon)))
            .Sum(e => e.Effect.Value);

        double health = (BaseHealth + Sum("Health") + Steps("HealthStep")) * (1 + Sum("HealthPercent") + Steps("HealthStepPercent"));
        foreach (var item in gear.Where(i => i.Stat("HealthCap") > 0)) health *= 1 - item.Stat("HealthCap");
        double stamina = (BaseStamina + Sum("Stamina") + Steps("StaminaStep")) * (1 + Sum("StaminaPercent") + Steps("StaminaStepPercent"));
        double armor = Sum("Armor") * (1 + Sum("ArmorPercent") + Steps("ArmorStepPercent")) + Steps("ArmorStep");
        double weight = Math.Max(0, Sum("Weight") * (1 + Sum("WeightPercent")));
        double threshold = Sum("WeightThreshold") + traits.Sum(t => t.Trait.WeightThresholds is { Length: > 0 } w && t.Total > 0 ? w[Math.Min(t.Total, w.Length) - 1] : 0);

        double armorReduction = armor <= 0 ? 0 : armor / (armor + 200);
        double flat = 1 - effects.Where(e => e.On && e.Effect.Stat == EffectStat.DamageReduction).Aggregate(1.0, (left, e) => left * (1 - e.Effect.Value));
        double total = Math.Min(MaxReduction, 1 - (1 - armorReduction) * (1 - flat));

        var resistances = Elements.ToDictionary(e => e, e => Sum(e + "Resistance") + Steps("ElementalResistanceStep"));

        var weapons = build.Weapons
            .Select(slot => (slot, item: db.Find(slot.Weapon)))
            .Where(w => w.item is not null)
            .Select(w => Damage(build, w.slot, w.item!, Bonus))
            .ToList();

        return new BuildStats(health, stamina, armor, armorReduction, flat, total, weight, threshold,
            WeightClass(weight, threshold, build, db), resistances, traits, traits.Sum(t => t.Total), effects, weapons);
    }

    public static IReadOnlyList<TraitPoints> Traits(Build build, PlannerDatabase db)
    {
        var free = new Dictionary<string, int>();
        foreach (var archetype in build.Archetypes.Select(db.Find).OfType<PlannerItem>())
            foreach (var link in archetype.LinkedTraits ?? [])
                if (db.Named("trait", link.Name) is { } trait)
                    free[trait.Id] = free.GetValueOrDefault(trait.Id) + link.Points;
        return free.Keys.Union(build.Traits.Keys)
            .Select(id => db.Find(id) is { } trait ? new TraitPoints(trait, free.GetValueOrDefault(id), build.Traits.GetValueOrDefault(id)) : null)
            .OfType<TraitPoints>()
            .Where(t => t.Total > 0)
            .OrderByDescending(t => t.FromArchetypes).ThenBy(t => t.Trait.Name)
            .ToList();
    }

    public static IReadOnlyList<ActiveEffect> Effects(Build build, PlannerDatabase db, IReadOnlyList<TraitPoints>? traits = null)
    {
        traits ??= Traits(build, db);
        var sources = build.Gear.Select(db.Find).OfType<PlannerItem>().Select(i => EffectParser.Parse(i))
            .Concat(traits.Select(t => EffectParser.Parse(t.Trait, t.Total)));
        return sources.SelectMany(e => e)
            .Select(e => new ActiveEffect(e, e.Conditional == build.FlippedEffects.Contains(e.Key)))
            .ToList();
    }

    private static bool OnWeapon(Build build, PlannerItem mutator, PlannerItem weapon) =>
        build.Weapons.Any(w => w.Weapon == weapon.Id && w.Mutator == mutator.Id);

    private static WeaponDamage Damage(Build build, WeaponSlot slot, PlannerItem weapon, Func<EffectStat, PlannerItem?, double> bonus)
    {
        int level = Math.Clamp(slot.Level, 0, weapon.MaxLevel);
        double baseAtLevel = weapon.Stat("Damage") * (1 + level * (weapon.IsSpecialWeapon ? 0.2 : 0.1));
        bool melee = weapon.IsMelee;
        double damageBonus = bonus(EffectStat.AllDamage, weapon) + bonus(melee ? EffectStat.MeleeDamage : EffectStat.RangedDamage, weapon);
        double critChance = Math.Clamp(weapon.Stat("Crit") / 100 + bonus(EffectStat.CritChance, weapon)
                                       + bonus(melee ? EffectStat.MeleeCritChance : EffectStat.RangedCritChance, weapon), 0, 1);
        double critMultiplier = 1 + BaseCritDamage + bonus(EffectStat.CritDamage, weapon);
        // A weapon without a weakspot value can't hit weakspots (e.g. flamethrowers).
        bool hasWeakspot = weapon.Stat("Weakspot") > 0;
        double weakspotMultiplier = hasWeakspot ? 1 + weapon.Stat("Weakspot") / 100 + bonus(EffectStat.WeakspotDamage, weapon) : 1;
        double fireRate = melee ? 0 : bonus(EffectStat.FireRate, weapon);
        double? rps = weapon.Stat("Rps") > 0 ? weapon.Stat("Rps") * (1 + fireRate) : null;

        double body = baseAtLevel * (1 + damageBonus);
        double weakRate = hasWeakspot ? Math.Clamp(build.WeakspotHitRate, 0, 1) : 0;
        return new WeaponDamage(slot, weapon, baseAtLevel, damageBonus, critChance, critMultiplier, weakspotMultiplier, fireRate, rps,
            HitMin: body,
            HitMax: body * critMultiplier * weakspotMultiplier,
            HitExpected: body * (1 + critChance * (critMultiplier - 1)) * (1 + weakRate * (weakspotMultiplier - 1)));
    }

    private static string WeightClass(double weight, double threshold, Build build, PlannerDatabase db)
    {
        string label = weight <= 25 + threshold ? "Light" : weight <= 50 + threshold ? "Medium" : weight <= 75 + threshold ? "Heavy" : "Ultra";
        // Dull Steel Ring lowers the class by one; its +25 threshold covers that except from Ultra, unless Burden of
        // the Mason cancels it (same rule as the toolkit).
        bool dullSteel = build.Rings.Any(r => db.Find(r)?.Name == "Dull Steel Ring");
        bool mason = build.Rings.Any(r => db.Find(r)?.Name == "Burden of the Mason");
        return label == "Ultra" && dullSteel && !mason ? "Heavy" : label;
    }

    public static string WeightClassEffect(string weightClass) => weightClass switch
    {
        "Light" => "Fast dodge, no stamina penalty",
        "Medium" => "Normal dodge, 25% stamina cost penalty",
        "Heavy" => "Slow dodge, 50% stamina cost penalty",
        _ => "Flop, 75% stamina cost penalty",
    };
}
