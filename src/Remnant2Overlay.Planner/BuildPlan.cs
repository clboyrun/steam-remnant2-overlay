namespace Remnant2Overlay.Planner;

/// <summary>What the linked character's save says about one item.</summary>
/// <param name="Owned">Null when the save reader doesn't track this kind of item (archetypes, relic fragments).</param>
/// <param name="RunStatus">Status in the run being played: Available, NeedsPrerequisite, MissedThisRun, NotRolled,
/// WorldNotInRun, or null without run info.</param>
/// <param name="World">Friendly world name the item drops in, e.g. Losomn or "Any / Ward 13".</param>
/// <param name="HowToGet">Drop type and source plus the save analyzer's note, e.g. "Vendor: Cass".</param>
/// <param name="Where">Exact location in the current run.</param>
public sealed record ItemState(bool? Owned, string? RunStatus, string? World, string? HowToGet, string? Prerequisite, string? Where, bool IsVendor);

/// <param name="Upgrade">For weapons with an upgrade level: where and with what to upgrade.</param>
public sealed record Requirement(string Part, PlannerItem Item, ItemState State, string? Note = null, string? Upgrade = null)
{
    public bool Done => State.Owned == true;
}

public sealed record RouteStep(string Title, string Why, IReadOnlyList<Requirement> Items);

/// <summary>Turns a build into the items a character still needs and an order to go get them.</summary>
public static class BuildPlan
{
    public static IReadOnlyList<Requirement> Requirements(Build build, PlannerDatabase db, Func<PlannerItem, ItemState> state)
    {
        var list = new List<Requirement>();
        void Add(string part, string? id, string? note = null, string? upgrade = null, bool alwaysOwned = false)
        {
            if (db.Find(id) is not { } item || list.Any(r => r.Item.Id == item.Id)) return;
            var itemState = state(item);
            list.Add(new Requirement(part, item, alwaysOwned ? itemState with { Owned = true } : itemState, note, upgrade));
        }

        for (int i = 0; i < build.Archetypes.Length; i++)
        {
            string? skill = db.Find(build.Skills.ElementAtOrDefault(i))?.Name;
            Add(i == 0 ? "Primary archetype" : "Secondary archetype", build.Archetypes[i], skill is null ? null : $"Skill: {skill} (unlocks by levelling the archetype)");
        }
        foreach (var slot in build.Weapons)
        {
            if (db.Find(slot.Weapon) is not { } weapon) continue;
            string? upgrade = slot.Level <= 0 ? null
                : $"Upgrade to +{slot.Level} at McCabe in Ward 13 with {(weapon.IsSpecialWeapon ? "Simulacrum" : "Lumenite Crystals")} and Scrap.";
            Add(Cap(slot.Slot), slot.Weapon, upgrade: upgrade);
            Add("Mod", slot.Mod);
            Add("Mutator", slot.Mutator, "Bonuses assume the mutator at level 10.");
        }
        Add("Helm", build.Helm);
        Add("Chest", build.Torso);
        Add("Legs", build.Legs);
        Add("Gloves", build.Gloves);
        Add("Amulet", build.Amulet);
        foreach (string? ring in build.Rings) Add("Ring", ring);
        Add("Relic", build.Relic);
        foreach (string? fragment in build.Fragments) Add("Relic fragment", fragment);
        // Points the archetypes give need nothing; core traits are unlocked from the start, the rest drop as items.
        foreach (var trait in BuildCalculator.Traits(build, db).Where(t => t.Spent > 0))
        {
            string points = $"Spend {trait.Spent} point{(trait.Spent == 1 ? "" : "s")}"
                            + (trait.FromArchetypes > 0 ? $" on top of the {trait.FromArchetypes} from archetypes." : ".");
            Add(trait.Trait.Sub == "core" ? "Core trait" : "Trait", trait.Trait.Id, points, alwaysOwned: trait.Trait.Sub is "core" or "archetype");
        }
        return list;
    }

    /// <summary>
    /// Groups what's missing into trips: grab what the current run has, unlock what it gates, shop in Ward 13, then
    /// one reroll per world, biggest haul first. Items the save can't see go last as things to check.
    /// </summary>
    public static IReadOnlyList<RouteStep> Route(IReadOnlyList<Requirement> requirements)
    {
        var missing = requirements.Where(r => !r.Done).ToList();
        var steps = new List<RouteStep>();
        var used = new HashSet<Requirement>();
        void Step(string title, string why, IEnumerable<Requirement> items)
        {
            var list = items.Where(used.Add).ToList();
            if (list.Count > 0) steps.Add(new RouteStep(title, why, list));
        }

        var tracked = missing.Where(r => r.State.Owned is not null).ToList();
        Step("Grab in your current run", "Rolled and ready to pick up now.", tracked.Where(r => r.State.RunStatus == "Available"));
        Step("Unlock in your current run", "Rolled, but behind a quest step or an item first.", tracked.Where(r => r.State.RunStatus == "NeedsPrerequisite"));
        Step("Buy or craft in Ward 13", "Vendors and crafting don't depend on the roll.", tracked.Where(r => r.State.IsVendor));
        foreach (var world in tracked.Where(r => !used.Contains(r)).GroupBy(r => r.State.World ?? "Unknown world").OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            bool any = world.Key is "Any / Ward 13" or "Unknown world";
            Step(any ? "Anywhere: world drops and other sources" : $"Roll an adventure in {world.Key}",
                any ? "Not tied to one world." : $"{world.Count()} build item{(world.Count() == 1 ? "" : "s")} drop there. Reroll until the zones listed roll.",
                world);
        }
        Step("Check these yourself", "The save reader doesn't track archetypes or relic fragments; tick them off in game.", missing.Where(r => r.State.Owned is null));

        var upgrades = requirements.Where(r => r.Upgrade is not null).Select(r => r with { Note = r.Upgrade }).ToList();
        if (upgrades.Count > 0) steps.Add(new RouteStep("Upgrade weapons", "Once you have them.", upgrades));
        var points = requirements.Where(r => r.Item.Kind == "trait" && r.Done).ToList();
        if (points.Count > 0) steps.Add(new RouteStep("Spend trait points", "Unlocked already; level up to fill them.", points));
        return steps;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
