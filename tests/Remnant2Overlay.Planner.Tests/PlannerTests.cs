using Remnant2Overlay.Planner;

namespace Remnant2Overlay.Planner.Tests;

public class PlannerTests
{
    private static readonly PlannerDatabase Db = PlannerDatabase.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "planner.json")));

    private static PlannerItem Named(string kind, string name) => Db.Named(kind, name) ?? throw new KeyNotFoundException(name);

    private static PlannerItem Item(string kind, string desc) => new() { Id = "Test_Item", Name = "Test", Kind = kind, Desc = desc };

    [Fact]
    public void Data_HasEveryPartOfABuild()
    {
        foreach (string kind in new[] { "weapon", "helm", "torso", "legs", "gloves", "ring", "amulet", "relic", "fragment", "mutator", "mod", "archetype", "skill", "trait" })
            Assert.True(Db.Of(kind).Any(), kind);
        var gun = Named("weapon", "Chicago Typewriter");
        Assert.Equal("Weapon_TommyGun", gun.Id);
        Assert.True(gun.Stat("Damage") > 0 && gun.Stat("Rps") > 0);
    }

    [Theory]
    [InlineData("Increases Ranged Damage by 15%.", EffectStat.RangedDamage, 0.15, false)]
    [InlineData("Increases Critical Chance by 10% and Critical Damage by 15%.", EffectStat.CritChance, 0.10, false)]
    [InlineData("Grants 15% increase to all damage.", EffectStat.AllDamage, 0.15, false)]
    [InlineData("Increases Crit Damage by 20%.", EffectStat.CritDamage, 0.20, false)]
    [InlineData("Increases all damage dealt by 30% when the wearer's Health is below 50%.", EffectStat.AllDamage, 0.30, true)]
    [InlineData("Increases Damage Reduction by 10%.", EffectStat.DamageReduction, 0.10, false)]
    public void Parser_ReadsPlainBonuses(string desc, EffectStat stat, double value, bool conditional)
    {
        var effect = EffectParser.Parse(Item("ring", desc)).First();
        Assert.Equal(stat, effect.Stat);
        Assert.Equal(value, effect.Value, 3);
        Assert.Equal(conditional, effect.Conditional);
    }

    [Theory]
    [InlineData("Increases Explosive damage by 15%.")]
    [InlineData("Increases Mod Critical Chance by 7.5%.")]
    [InlineData("Increases all incoming damage by 10%.")]
    [InlineData("Increases ACID damage by 15% and ACID Resistance by 20%.")]
    public void Parser_SkipsOtherKindsOfDamage(string desc) =>
        Assert.Empty(EffectParser.Parse(Item("ring", desc)));

    [Fact]
    public void Parser_ScalesTraitsWithPoints()
    {
        var effect = Assert.Single(EffectParser.Parse(Item("trait", "Reduces all incoming damage by 1 -> 10%."), points: 5));
        Assert.Equal(EffectStat.DamageReduction, effect.Stat);
        Assert.Equal(0.05, effect.Value, 3);
    }

    [Fact]
    public void Damage_FollowsTheWikiFormula()
    {
        var gun = Named("weapon", "Chicago Typewriter");
        var build = new Build { Rings = [Named("ring", "Empowering Loop").Id, Named("ring", "Probability Cord").Id, null, null] };
        build.Weapons[0].Weapon = gun.Id;
        build.Weapons[0].Level = 20;

        var dmg = BuildCalculator.Compute(build, Db).Weapons.Single();

        double body = gun.Stat("Damage") * 3 * 1.15;
        Assert.Equal(gun.Stat("Damage") * 3, dmg.BaseAtLevel, 3);
        Assert.Equal(body, dmg.HitMin, 3);
        Assert.Equal(body * (1.5 + 0.2) * (1 + gun.Stat("Weakspot") / 100), dmg.HitMax, 3);
        Assert.Equal(body * (1 + gun.Stat("Crit") / 100 * 0.7), dmg.HitExpected, 3);
        Assert.Equal(body * gun.Stat("Rps"), dmg.DpsMin!.Value, 3);
    }

    [Fact]
    public void Damage_SpecialWeaponsReachTripleAtPlusTen()
    {
        var gun = Db.Of("weapon").First(w => w.IsSpecialWeapon && !w.IsMelee);
        var build = new Build();
        build.Weapons[0].Weapon = gun.Id;
        build.Weapons[0].Level = 10;
        Assert.Equal(gun.Stat("Damage") * 3, BuildCalculator.Compute(build, Db).Weapons.Single().BaseAtLevel, 3);
    }

    [Fact]
    public void ConditionalEffects_CountOnlyWhenTurnedOn()
    {
        var ring = Named("ring", "Singed Ring");
        var build = new Build { Rings = [ring.Id, null, null, null] };
        build.Weapons[0].Weapon = Named("weapon", "Chicago Typewriter").Id;

        var off = BuildCalculator.Compute(build, Db);
        build.FlippedEffects.Add(off.Effects.Single().Effect.Key);
        var on = BuildCalculator.Compute(build, Db);

        Assert.Equal(0, off.Weapons[0].DamageBonus, 3);
        Assert.Equal(0.12, on.Weapons[0].DamageBonus, 3);
    }

    [Fact]
    public void Archetypes_GiveTheirTraitPoints()
    {
        var build = new Build { Archetypes = [Named("archetype", "Challenger").Id, null] };
        build.Traits[Named("trait", "Vigor").Id] = 9;

        var stats = BuildCalculator.Compute(build, Db);

        var vigor = stats.Traits.Single(t => t.Trait.Name == "Vigor");
        Assert.Equal(3, vigor.FromArchetypes);
        Assert.Equal(10, vigor.Total);
        Assert.Equal(100 + 30, stats.Health, 3);
        Assert.Equal(15, stats.WeightThreshold, 3); // Strong Back at 10
    }

    [Fact]
    public void Defense_UsesArmorOverArmorPlus200()
    {
        var build = new Build { Torso = Db.Of("torso").First(t => t.Stat("Armor") > 0).Id };
        var stats = BuildCalculator.Compute(build, Db);
        Assert.Equal(stats.Armor / (stats.Armor + 200), stats.TotalReduction, 5);
    }

    [Fact]
    public void Route_PutsTheCurrentRunFirstAndBiggestWorldNext()
    {
        var a = Named("ring", "Empowering Loop");
        var b = Named("ring", "Probability Cord");
        var c = Named("ring", "Braided Thorns");
        var d = Named("amulet", "Zero Hour");
        var build = new Build { Rings = [a.Id, b.Id, c.Id, null], Amulet = d.Id, Fragments = [Db.Of("fragment").First().Id, null, null] };
        var states = new Dictionary<string, ItemState>
        {
            [a.Id] = new(false, "Available", "Losomn", null, null, "Morrow Parish", false),
            [b.Id] = new(false, "WorldNotInRun", "Yaesha", null, null, null, false),
            [c.Id] = new(false, "WorldNotInRun", "Yaesha", null, null, null, false),
            [d.Id] = new(true, null, "N'Erud", null, null, null, false),
        };

        var reqs = BuildPlan.Requirements(build, Db, i => states.GetValueOrDefault(i.Id) ?? new ItemState(null, null, null, null, null, null, false));
        var route = BuildPlan.Route(reqs);

        Assert.Equal("Grab in your current run", route[0].Title);
        Assert.Equal(a.Id, Assert.Single(route[0].Items).Item.Id);
        Assert.Equal("Roll an adventure in Yaesha", route[1].Title);
        Assert.Equal(2, route[1].Items.Count);
        Assert.Equal("Check these yourself", route[2].Title);
        Assert.DoesNotContain(route.SelectMany(s => s.Items), r => r.Item.Id == d.Id);
    }
}
