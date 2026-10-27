using System.Windows;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.App;

public partial class App : Application
{
    public static string CategoryLabel(ItemCategory? category) => category switch
    {
        ItemCategory.Weapon => "Weapon",
        ItemCategory.WeaponMod => "Mod",
        ItemCategory.Head => "Helm",
        ItemCategory.Body => "Chest",
        ItemCategory.Gloves => "Gloves",
        ItemCategory.Legs => "Legs",
        ItemCategory.Ring => "Ring",
        ItemCategory.Amulet => "Amulet",
        ItemCategory.Mutator => "Mutator",
        ItemCategory.RelicFragment => "Relic fragment",
        ItemCategory.Relic => "Relic",
        _ => "Other",
    };
}
