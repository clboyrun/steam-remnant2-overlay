namespace Remnant2Overlay.SaveReader;

public enum ItemCategory
{
    Weapon,
    WeaponMod,
    Head,
    Body,
    Gloves,
    Legs,
    Ring,
    Amulet,
    Mutator,
    RelicFragment,
    Relic,
    Trait,
}

public static class ItemCategories
{
    // Internal asset-name prefixes. Mutators are called "MetaGem" in the game files.
    private static readonly (string Prefix, ItemCategory Category)[] Prefixes =
    [
        ("Weapon_", ItemCategory.Weapon),
        ("Mod_", ItemCategory.WeaponMod),
        ("Armor_Head_", ItemCategory.Head),
        ("Armor_Body_", ItemCategory.Body),
        ("Armor_Gloves_", ItemCategory.Gloves),
        ("Armor_Legs_", ItemCategory.Legs),
        ("Ring_", ItemCategory.Ring),
        ("Amulet_", ItemCategory.Amulet),
        ("MetaGem_", ItemCategory.Mutator),
        ("RelicFragment_", ItemCategory.RelicFragment),
        ("Relic_Consumable_", ItemCategory.Relic),
        ("Trait_", ItemCategory.Trait),
    ];

    public static ItemCategory? FromAssetName(string assetName)
    {
        foreach (var (prefix, category) in Prefixes)
            if (assetName.StartsWith(prefix, StringComparison.Ordinal))
                return category;
        return null;
    }
}
