using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.App;

public sealed record CategoryFilter(string Label, ItemCategory[] Categories);
public sealed record WorldFilter(string Label, string? World);

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Settings _settings;
    private readonly ItemDatabase _db = ItemDatabase.LoadEmbedded();
    private IReadOnlyList<CharacterInventory> _characters = [];

    private CategoryFilter _category;
    private WorldFilter _world;
    private string _show = "Missing";
    private string _search = "";
    private bool _manualMode;
    private int _characterIndex;
    private string _status = "";

    public MainViewModel(Settings settings)
    {
        _settings = settings;
        Tiles = new ObservableCollection<ItemTileViewModel>(_db.Items
            .OrderBy(i => i.Category).ThenBy(i => i.DisplayName)
            .Select(i => new ItemTileViewModel(i, Settings.IconDir)));
        View = CollectionViewSource.GetDefaultView(Tiles);
        View.Filter = o => Matches((ItemTileViewModel)o);

        _category = Categories[0];
        _world = Worlds.FirstOrDefault(w => w.World == settings.AdventureWorld) ?? Worlds[0];
        _characterIndex = settings.CharacterIndex;
    }

    public ObservableCollection<ItemTileViewModel> Tiles { get; }
    public ICollectionView View { get; }

    public IReadOnlyList<CategoryFilter> Categories { get; } =
    [
        new("All", Enum.GetValues<ItemCategory>()),
        new("Weapons", [ItemCategory.Weapon]),
        new("Mods", [ItemCategory.WeaponMod]),
        new("Armor", [ItemCategory.Head, ItemCategory.Body, ItemCategory.Gloves, ItemCategory.Legs]),
        new("Rings", [ItemCategory.Ring]),
        new("Amulets", [ItemCategory.Amulet]),
        new("Mutators", [ItemCategory.Mutator]),
        new("Relics", [ItemCategory.Relic]),
    ];

    public IReadOnlyList<WorldFilter> Worlds { get; } =
    [
        new("All worlds", null),
        new("Ward 13 / any", "World_Base"),
        new("Losomn", "World_Fae"),
        new("Yaesha", "World_Jungle"),
        new("N'Erud", "World_Nerud"),
        new("Root Earth", "World_Root"),
        new("Labyrinth", "World_Labyrinth"),
        new("The Awakened King", "World_DLC1"),
        new("The Forgotten Kingdom", "World_DLC2"),
        new("The Dark Horizon", "World_DLC3"),
    ];

    public IReadOnlyList<string> ShowOptions { get; } = ["Missing", "Owned", "All"];

    public IReadOnlyList<string> CharacterNames =>
        _characters.Select(c => $"Character {c.Index + 1}").ToList();

    public CategoryFilter Category { get => _category; set { if (Set(ref _category, value)) Refresh(); } }

    /// <summary>World filter; in manual mode this doubles as "the adventure I rolled".</summary>
    public WorldFilter World
    {
        get => _world;
        set
        {
            if (!Set(ref _world, value)) return;
            _settings.AdventureWorld = value.World;
            Refresh();
        }
    }

    public string Show { get => _show; set { if (Set(ref _show, value)) Refresh(); } }
    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh(); } }
    public bool ManualMode { get => _manualMode; set => Set(ref _manualMode, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public int CharacterIndex
    {
        get => _characterIndex;
        set
        {
            if (value < 0 || !Set(ref _characterIndex, value)) return;
            _settings.CharacterIndex = value;
            ApplyCharacter();
        }
    }

    public string Summary
    {
        get
        {
            var inCategory = Tiles.Where(t => _category.Categories.Contains(t.Item.Category!.Value)
                                              && (_world.World is null || t.Item.World == _world.World)).ToList();
            return $"{inCategory.Count(t => t.Owned)} / {inCategory.Count} owned";
        }
    }

    public string? ProfilePath => _settings.ProfilePath;

    public void LoadSave(string? path)
    {
        path ??= _settings.ProfilePath ?? FindDefaultProfile();
        if (path is null || !File.Exists(path))
        {
            _characters = [];
            Status = "No save found. Use Open save… or Manual mode.";
            ApplyCharacter();
            return;
        }

        try
        {
            _characters = ProfileReader.ReadFile(path);
            _settings.ProfilePath = path;
            Status = $"Loaded {Path.GetFileName(path)} at {DateTime.Now:t}";
        }
        catch (Exception e)
        {
            // The game may be mid-write; keep the last good data.
            Status = $"Could not read save: {e.Message}";
        }

        Changed(nameof(CharacterNames));
        if (_characterIndex >= _characters.Count) _characterIndex = 0;
        Changed(nameof(CharacterIndex));
        ApplyCharacter();
    }

    public void Toggle(ItemTileViewModel tile)
    {
        if (!_manualMode) return;
        var overrides = OverridesForCharacter();

        // Cycle: follow save -> opposite of save -> follow save.
        if (tile.ManualOverride is null)
        {
            tile.ManualOverride = !tile.OwnedInSave;
            overrides[tile.Item.AssetName] = tile.ManualOverride.Value;
        }
        else
        {
            tile.ManualOverride = null;
            overrides.Remove(tile.Item.AssetName);
        }

        Refresh();
    }

    public void ClearManual()
    {
        OverridesForCharacter().Clear();
        foreach (var t in Tiles) t.ManualOverride = null;
        Refresh();
    }

    private void ApplyCharacter()
    {
        var character = _characterIndex < _characters.Count ? _characters[_characterIndex] : null;
        var owned = character?.Items.Select(i => i.AssetName).ToHashSet() ?? [];
        var overrides = OverridesForCharacter();
        foreach (var t in Tiles)
        {
            t.OwnedInSave = owned.Contains(t.Item.AssetName);
            t.ManualOverride = overrides.TryGetValue(t.Item.AssetName, out bool o) ? o : null;
        }
        Refresh();
    }

    private Dictionary<string, bool> OverridesForCharacter()
    {
        if (!_settings.ManualOverrides.TryGetValue(_characterIndex, out var overrides))
            _settings.ManualOverrides[_characterIndex] = overrides = [];
        return overrides;
    }

    private bool Matches(ItemTileViewModel t)
    {
        if (!_category.Categories.Contains(t.Item.Category!.Value)) return false;
        if (_world.World is not null && t.Item.World != _world.World) return false;
        if (_show == "Missing" && t.Owned) return false;
        if (_show == "Owned" && !t.Owned) return false;
        return _search.Length == 0
               || t.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
               || (t.Item.DropReference?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void Refresh()
    {
        View.Refresh();
        Changed(nameof(Summary));
    }

    private static string? FindDefaultProfile()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Remnant2", "Steam");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "profile.sav", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name!);
        return true;
    }
}
