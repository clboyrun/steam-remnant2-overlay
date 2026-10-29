using System.Globalization;
using System.Text.RegularExpressions;

namespace Remnant2Overlay.Planner;

public enum EffectStat
{
    AllDamage,
    RangedDamage,
    MeleeDamage,
    CritChance,
    RangedCritChance,
    MeleeCritChance,
    CritDamage,
    WeakspotDamage,
    FireRate,
    DamageReduction,
}

/// <summary>
/// A damage or defense bonus read from an item's description. <see cref="Conditional"/> ones (after a kill, while
/// bleeding, per stack…) only count when the player says they're active.
/// </summary>
/// <param name="WeaponOnly">A mutator's bonus, which only applies to the weapon it's on.</param>
public sealed record Effect(string Key, PlannerItem Source, EffectStat Stat, double Value, bool Conditional, string Text, bool WeaponOnly)
{
    public string Label => EffectParser.Label(Stat);
}

/// <summary>
/// Reads "Increases Ranged Damage by 10%"-style sentences. Anything it can't read stays in the description text the
/// page shows next to the numbers.
/// </summary>
public static partial class EffectParser
{
    public static string Label(EffectStat stat) => stat switch
    {
        EffectStat.AllDamage => "All damage",
        EffectStat.RangedDamage => "Ranged damage",
        EffectStat.MeleeDamage => "Melee damage",
        EffectStat.CritChance => "Critical chance",
        EffectStat.RangedCritChance => "Ranged critical chance",
        EffectStat.MeleeCritChance => "Melee critical chance",
        EffectStat.CritDamage => "Critical damage",
        EffectStat.WeakspotDamage => "Weakspot damage",
        EffectStat.FireRate => "Fire rate",
        _ => "Damage reduction",
    };

    /// <param name="points">Trait points, for traits ("by 1% -> 10%" scales with points out of 10).</param>
    public static IReadOnlyList<Effect> Parse(PlannerItem item, int points = Build.MaxTraitPoints)
    {
        if (item.Desc is null || item.Kind is "weapon" or "mod" or "skill" or "archetype") return [];
        var effects = new List<Effect>();
        var sentences = Sentence().Split(item.Desc).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (item.MaxLevelBonus is not null) sentences.Add(item.MaxLevelBonus.Trim());
        foreach (string sentence in sentences)
        {
            bool conditional = Condition().IsMatch(sentence);
            foreach (Match m in Bonus().Matches(sentence))
            {
                if (ToStat(m.Groups["pre"].Value, m.Groups["scope"].Value, m.Groups["stat"].Value) is not { } stat) continue;
                double value = Amount(m, item, points) * (Lowered(sentence, m) ? -1 : 1);
                if (value == 0) continue;
                effects.Add(new Effect($"{item.Id}#{effects.Count}", item, stat, value, conditional, sentence, item.Kind == "mutator"));
            }
            foreach (Match m in Reduction().Matches(sentence))
            {
                double value = Amount(m, item, points);
                if (value > 0)
                    effects.Add(new Effect($"{item.Id}#{effects.Count}", item, EffectStat.DamageReduction, value, conditional, sentence, false));
            }
        }
        return effects;
    }

    /// <summary>Percent as a fraction. Ranges ("10% - 30%", "1% -> 10%") take the top, which is the mutator at
    /// level 10, or for traits the share of it that the points give.</summary>
    private static double Amount(Match m, PlannerItem item, int points)
    {
        double low = double.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
        double value = m.Groups["b"].Success ? double.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture) : low;
        if (item.Kind == "trait" && m.Groups["b"].Success) value = value * Math.Clamp(points, 0, Build.MaxTraitPoints) / Build.MaxTraitPoints;
        return value / 100;
    }

    private static readonly HashSet<string> AllowedBefore = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "increases", "increase", "grants", "grant", "gain", "gains", "and", "the", "a", "weapon's", "its", "additional",
    };

    private static EffectStat? ToStat(string pre, string scope, string stat)
    {
        pre = pre.Trim();
        scope = scope.Trim().ToLowerInvariant();
        // "15% Melee damage": the optional word before the scope took the scope.
        if (scope.Length == 0 && pre.ToLowerInvariant() is "all" or "ranged" or "range" or "melee") (scope, pre) = (pre.ToLowerInvariant(), "");
        // "Explosive Damage", "Mod Critical Chance", "incoming damage" and the like aren't weapon damage.
        if (!AllowedBefore.Contains(pre)) return null;
        stat = stat.ToLowerInvariant();
        return stat switch
        {
            "damage" or "damage bonus" => scope switch { "ranged" => EffectStat.RangedDamage, "melee" => EffectStat.MeleeDamage, _ => EffectStat.AllDamage },
            "critical chance" or "crit chance" => scope switch { "ranged" or "range" => EffectStat.RangedCritChance, "melee" => EffectStat.MeleeCritChance, _ => EffectStat.CritChance },
            "critical damage" or "crit damage" => EffectStat.CritDamage,
            "weakspot damage" => scope == "melee" ? null : EffectStat.WeakspotDamage,
            "fire rate" => scope == "melee" ? null : EffectStat.FireRate,
            _ => null,
        };
    }

    private const string Num = @"(?<a>\d+(?:\.\d+)?)\s*%?(?:\s*(?:-|→|->|to)\s*(?<b>\d+(?:\.\d+)?))?\s*%";
    private const string Stat = @"(?<stat>critical\s+chance|crit\s+chance|critical\s+damage|crit\s+damage|weakspot\s+damage|fire\s+rate|damage\s+bonus|damage)";
    private const string ScopedStat = @"(?<scope>(?:all|ranged|range|melee)\s+)?" + Stat;
    // The word before lets "Explosive Damage" and the like be told apart from plain damage.
    private const string StatWords = @"(?:(?<pre>[\w']+)\s+)?" + ScopedStat;

    // "Ranged Damage [dealt to BURNING enemies] by 10%" or "10% Fire Rate" / "15% increase to all damage". Damage
    // must not be followed by "Reduction", which is a defense stat.
    [GeneratedRegex(StatWords + @"(?!\s+reduction)(?:\s+of\s+this\s+weapon|\s+(?:dealt|to|against)\b[^.%]{0,40}?)?\s+by\s+(?:up\s+to\s+)?" + Num
                    + "|" + Num + @"\s+(?:increase\s+to\s+|increased\s+|additional\s+|bonus\s+to\s+|to\s+)?" + ScopedStat + @"(?!\s+reduction)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Bonus();

    [GeneratedRegex(@"(?:damage\s+reduction|reduces\s+all\s+incoming\s+damage)(?:\s+from\s+all\s+sources)?\s+by\s+" + Num, RegexOptions.IgnoreCase)]
    private static partial Regex Reduction();

    // Words that make a bonus situational.
    [GeneratedRegex(@"\b(when|while|after|for each|for every|per|against|if|within|dealing|to enemies|to targets|stacks?|up to|based on|wearer's|behind|grey health|bleeding|burning|exposed|corroded|overloaded|slow|for \d+(\.\d+)?s|over \d|next|on hit|killing|kill|continuously|reloading|switching|activating|consecutive|empty|missing|below|above|charged|stowed|summons?|allies|ally|illuminated|heavy weapons|critical (?:hits|strikes)|charge|also|taking|max|explosive)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Condition();

    /// <summary>"All damage dealt by the wearer is reduced by 15%", or the nearest verb before the bonus is "reduces"
    /// ("…reduces All Damage bonus by 5%", but not "increases X by 20% and reduces Y by 10%" for X).</summary>
    private static bool Lowered(string sentence, Match m) =>
        m.Value.Contains("reduced", StringComparison.OrdinalIgnoreCase)
        || Verb().Matches(sentence[..m.Index]).LastOrDefault()?.Groups[1].Value.ToLowerInvariant() is "reduce" or "decrease" or "lower";

    [GeneratedRegex(@"\b(increase|grant|gain|reduce|decrease|lower)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Verb();

    [GeneratedRegex(@"(?<=[.!])\s+|\n+")]
    private static partial Regex Sentence();
}
