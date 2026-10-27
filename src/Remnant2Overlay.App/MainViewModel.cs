using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using lib.remnant2.analyzer.Enums;
using Remnant2Overlay.Icons;
using Remnant2Overlay.SaveReader;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.App;

public sealed record CategoryFilter(string Label, ItemCategory[] Categories);
public sealed record WorldFilter(string Label, string? World);
public sealed record RunChoice(string Label, WorldSlot Slot);

/// <summary>A row in the run tree: world, location, drop source, or item.</summary>
public sealed class RunNode
{
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public RunItemStatus? Status { get; init; }
    /// <summary>Item id for item rows, so selecting one opens its details.</summary>
    public string? ItemId { get; init; }
    public bool IsExpanded { get; set; }
    public List<RunNode> Children { get; init; } = [];
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly RunItemStatus[] Collectable = [RunItemStatus.Available, RunItemStatus.NeedsPrerequisite, RunItemStatus.MissedThisRun];

    private readonly Settings _settings;
    private readonly ItemDatabase _db = ItemDatabase.LoadEmbedded();
    private readonly IconCache _iconCache = new(Settings.IconDir);
    private CancellationTokenSource? _iconSync;
    private IReadOnlyList<CharacterRuns> _characters = [];
    private IReadOnlyList<CharacterInventory> _fallbackInventories = [];

    private CategoryFilter _category;
    private WorldFilter _world;
    private string _show = "Missing";
    private string _search = "";
    private bool _manualMode;
    private int _characterIndex;
    private RunChoice? _run;
    private bool _userPickedRun;
    private string _status = "";
    private string _iconStatus = "";
    private string _runSummary = "";
    private string _questStatus = "";
    private bool _nothingLeft;
    private bool _showRunView;
    private ItemDetails? _details;
    private DateTimeOffset _saveReadAt;
    private string _syncStatus = "";

    public MainViewModel(Settings settings)
    {
        _settings = settings;
        Tiles = new ObservableCollection<ItemTileViewModel>(_db.Items
            .OrderBy(i => i.Category).ThenBy(i => i.DisplayName)
            .Select(i => new ItemTileViewModel(i, _iconCache.PathFor(i.AssetName))));
        // Set the filter state before attaching the filter: the view evaluates it immediately.
        _category = Categories[0];
        _world = Worlds.FirstOrDefault(w => w.World == settings.AdventureWorld) ?? Worlds[0];
        _characterIndex = settings.CharacterIndex;

        View = CollectionViewSource.GetDefaultView(Tiles);
        View.Filter = o => Matches((ItemTileViewModel)o);
    }

    public ObservableCollection<ItemTileViewModel> Tiles { get; }
    public ICollectionView View { get; }
    public ObservableCollection<RunNode> RunTree { get; } = [];
    public ObservableCollection<RunChoice> Runs { get; } = [];

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

    public IReadOnlyList<string> ShowOptions { get; } = ["Missing", "Available this run", "Needs quest", "Owned", "All"];

    public IReadOnlyList<string> CharacterNames =>
        _characters.Count > 0
            ? _characters.Select(c => $"{c.Index + 1}: {c.Description}").ToList()
            : _fallbackInventories.Select(c => $"Character {c.Index + 1}").ToList();

    public CategoryFilter Category { get => _category; set { if (Set(ref _category, value)) Refresh(); } }

    /// <summary>World filter; in manual mode this doubles as "the adventure I rolled".</summary>
    public WorldFilter World
    {
        get => _world;
        set
        {
            if (!Set(ref _world, value)) return;
            if (_settings.AdventureWorld != value.World)
            {
                _settings.AdventureWorld = value.World;
                _settings.AdventureWorldChangedAt = DateTimeOffset.UtcNow;
                SyncNeeded?.Invoke();
            }
            Refresh();
        }
    }

    public string Show { get => _show; set { if (Set(ref _show, value)) Refresh(); } }
    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh(); } }
    public bool ManualMode { get => _manualMode; set => Set(ref _manualMode, value); }
    public bool ShowRunView { get => _showRunView; set => Set(ref _showRunView, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string IconStatus { get => _iconStatus; private set => Set(ref _iconStatus, value); }
    public string SyncStatus { get => _syncStatus; set => Set(ref _syncStatus, value); }

    /// <summary>Raised when the save reloads or a manual choice changes, so the phone sync can upload it.</summary>
    public event Action? SyncNeeded;
    public string RunSummary { get => _runSummary; private set => Set(ref _runSummary, value); }
    public string QuestStatus { get => _questStatus; private set => Set(ref _questStatus, value); }
    public bool NothingLeft { get => _nothingLeft; private set => Set(ref _nothingLeft, value); }

    /// <summary>The item opened in the details panel, or null when it's closed.</summary>
    public ItemDetails? Details { get => _details; private set { if (Set(ref _details, value)) Changed(nameof(HasDetails)); } }
    public bool HasDetails => _details is not null;

    public void ShowDetails(ItemTileViewModel tile) => Details = new ItemDetails(tile, CurrentCharacter, CurrentRun);

    public void ShowDetails(string itemId)
    {
        if (Tiles.FirstOrDefault(t => t.Item.Id == itemId) is { } tile) ShowDetails(tile);
    }

    public void CloseDetails() => Details = null;

    // Statuses change with the save, run or manual marks; keep the open panel current.
    private void RefreshDetails()
    {
        if (_details is { } d) ShowDetails(d.Tile);
    }

    public RunChoice? Run
    {
        get => _run;
        set
        {
            if (value is null || !Set(ref _run, value)) return;
            _userPickedRun = true;
            ApplyCharacter();
        }
    }

    public int CharacterIndex
    {
        get => _characterIndex;
        set
        {
            if (value < 0 || !Set(ref _characterIndex, value)) return;
            _settings.CharacterIndex = value;
            _userPickedRun = false;
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

    public void LoadSave(string? path)
    {
        path ??= _settings.ProfilePath ?? FindDefaultProfile();
        if (path is null || !File.Exists(path))
        {
            _characters = [];
            _fallbackInventories = [];
            Status = "No save found. Use Open save… or Manual mode.";
            ApplyCharacter();
            return;
        }

        try
        {
            // Full analysis: inventory plus rolled campaign/adventure worlds.
            _characters = RunAnalysis.Analyze(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _fallbackInventories = [];
            _settings.ProfilePath = path;
            Status = $"Loaded {Path.GetFileName(path)} at {DateTime.Now:t}";
        }
        catch (Exception analysisError)
        {
            // The game may be mid-write, or a game patch may have changed the world save.
            // Fall back to the simple inventory reader so owned/missing still works.
            try
            {
                _fallbackInventories = ProfileReader.ReadFile(path);
                _characters = [];
                _settings.ProfilePath = path;
                Status = $"Run info unavailable ({analysisError.Message}); showing inventory only.";
            }
            catch (Exception e)
            {
                Status = $"Could not read save: {e.Message}";
            }
        }

        // The newest save file's write time, so an overlay restart doesn't count as a newer save for the phone.
        _saveReadAt = Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(path))!, "*.sav")
            .Select(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f)))
            .DefaultIfEmpty(DateTimeOffset.UtcNow).Max();

        Changed(nameof(CharacterNames));
        if (_characterIndex >= Math.Max(_characters.Count, _fallbackInventories.Count)) _characterIndex = 0;
        Changed(nameof(CharacterIndex));
        ApplyCharacter();
        SyncNeeded?.Invoke();
    }

    /// <summary>
    /// Extracts any icons missing from the local cache, in the background, and fills tiles in as they land.
    /// Items known to have no icon are skipped until the game files change, so this is cheap after the first run.
    /// </summary>
    public async Task SyncIconsAsync(bool force = false)
    {
        _iconSync?.Cancel();
        var cts = _iconSync = new CancellationTokenSource();

        // Everything goes to a log next to the cache, for diagnosing misses. Opened first so even
        // "game not found" and start-up failures are recorded.
        Directory.CreateDirectory(Settings.IconDir);
        string logPath = Path.Combine(Settings.IconDir, "extract.log");
        using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        void Log(string message) { lock (log) log.WriteLine(message); }
        Log($"{DateTime.Now:u} icon sync (force={force}), cache={Settings.IconDir}");

        try
        {
            string? paks = _settings.GamePaksPath is { } custom
                ? GameLocator.NormalizePaksFolder(custom, Log)
                : GameLocator.FindPaksFolder(Log);
            if (paks is null)
            {
                Log("Remnant 2 not found.");
                IconStatus = "Icons: Remnant 2 not found. Use Game folder… to point at it.";
                return;
            }

            string? usmap = _settings.UsmapPath ?? GameLocator.FindMappingsFile(paks, Settings.DataDir);
            var options = new IconExtractorOptions(paks, _settings.UnrealVersion, usmap);
            Log($"paks={options.PaksDir} version={options.UnrealVersion ?? "default"} usmap={options.UsmapPath ?? "none"}");
            var tilesByAsset = Tiles.ToDictionary(t => t.Item.AssetName);

            // Created on the UI thread, so reports arrive on the UI thread.
            var progress = new Progress<IconSyncProgress>(p =>
            {
                if (cts.IsCancellationRequested) return;
                if (p.IconPath is not null && tilesByAsset.TryGetValue(p.AssetName, out var tile)) tile.ReloadIcon(p.IconPath);
                IconStatus = $"Extracting icons {p.Done}/{p.Total}…";
            });
            var stage = new Progress<string>(message =>
            {
                if (!cts.IsCancellationRequested) IconStatus = "Icons: " + message;
            });

            IconStatus = "Checking icons…";
            var result = await Task.Run(() =>
            {
                if (force) _iconCache.Clear();
                return IconSync.Run(_iconCache, _db.Items, options, progress, Log, cts.Token, stage);
            }, cts.Token);
            Log($"Done: {result.Extracted} extracted, {result.NotFound} not found");

            if (force)
                foreach (var t in Tiles) t.ReloadIcon(_iconCache.PathFor(t.Item.AssetName));

            int have = Tiles.Count(t => t.HasIcon);
            if (result.NeedsMappings)
            {
                IconStatus = $"Icons need a mappings file (.usmap). Put one in {Settings.DataDir} and click Re-extract icons. See README.";
                return;
            }
            IconStatus = result.Extracted + result.NotFound == 0
                ? $"Icons: {have}/{Tiles.Count} cached"
                : $"Icons: {have}/{Tiles.Count} cached ({result.Extracted} new, {result.NotFound} not found in game files)";
        }
        catch (OperationCanceledException)
        {
            Log("Cancelled.");
        }
        catch (Exception e)
        {
            Log(e.ToString());
            IconStatus = $"Icon extraction failed: {e.Message} (details in {logPath})";
        }
    }

    public void CancelIconSync() => _iconSync?.Cancel();

    public void SetGameFolder(string folder)
    {
        _settings.GamePaksPath = folder;
        _ = SyncIconsAsync();
    }

    public void Toggle(ItemTileViewModel tile)
    {
        if (!_manualMode) return;
        var overrides = OverridesForCharacter();
        bool ownedInSave = tile.Status == RunItemStatus.Owned;

        // Cycle: follow save -> opposite of save -> follow save.
        if (tile.ManualOverride is null)
        {
            tile.ManualOverride = !ownedInSave;
            overrides[tile.Item.AssetName] = tile.ManualOverride.Value;
        }
        else
        {
            tile.ManualOverride = null;
            overrides.Remove(tile.Item.AssetName);
        }
        OverrideTimesForCharacter()[tile.Item.AssetName] = DateTimeOffset.UtcNow;

        Refresh();
        BuildRunTree();
        RefreshDetails();
        SyncNeeded?.Invoke();
    }

    public void ClearManual()
    {
        var overrides = OverridesForCharacter();
        var times = OverrideTimesForCharacter();
        var now = DateTimeOffset.UtcNow;
        foreach (string asset in overrides.Keys) times[asset] = now;
        overrides.Clear();
        foreach (var t in Tiles) t.ManualOverride = null;
        Refresh();
        BuildRunTree();
        RefreshDetails();
        SyncNeeded?.Invoke();
    }

    /// <summary>This overlay's side of the phone sync file: the save as read, plus manual marks with their times.</summary>
    public SyncDocument BuildSyncDocument()
    {
        SaveSnapshot? save = _characters.Count > 0 ? SyncSnapshot.FromRuns(_db, _characters, _saveReadAt, Status)
            : _fallbackInventories.Count > 0 ? SyncSnapshot.FromInventories(_db, _fallbackInventories, _saveReadAt, Status)
            : null;

        var marks = new Dictionary<int, Dictionary<string, OverrideMark>>();
        foreach (int character in _settings.ManualOverrides.Keys.Union(_settings.ManualOverrideTimes.Keys))
        {
            var overrides = _settings.ManualOverrides.GetValueOrDefault(character) ?? [];
            var times = _settings.ManualOverrideTimes.GetValueOrDefault(character) ?? [];
            var forCharacter = overrides.Keys.Union(times.Keys).ToDictionary(
                asset => asset,
                // Marks made before sync existed have no time, so any change from the phone wins over them.
                asset => new OverrideMark(overrides.TryGetValue(asset, out bool owned) ? owned : null, times.GetValueOrDefault(asset)));
            if (forCharacter.Count > 0) marks[character] = forCharacter;
        }

        return new SyncDocument
        {
            Save = save,
            ManualOverrides = marks,
            AdventureWorld = _settings.AdventureWorld is null && _settings.AdventureWorldChangedAt is null
                ? null
                : new Stamped<string?>(_settings.AdventureWorld, _settings.AdventureWorldChangedAt ?? default),
        };
    }

    /// <summary>Takes the manual marks and adventure pick from a merged sync file (changes made on the phone).</summary>
    public void ApplySynced(SyncDocument merged)
    {
        _settings.ManualOverrides = merged.ManualOverrides.ToDictionary(kv => kv.Key, kv => SyncMerge.ActiveOverrides(merged, kv.Key));
        _settings.ManualOverrideTimes = merged.ManualOverrides.ToDictionary(
            kv => kv.Key, kv => kv.Value.ToDictionary(m => m.Key, m => m.Value.UpdatedAt));

        if (merged.AdventureWorld is { } adventure && adventure.Value != _settings.AdventureWorld)
        {
            _settings.AdventureWorld = adventure.Value;
            _settings.AdventureWorldChangedAt = adventure.UpdatedAt;
            _world = Worlds.FirstOrDefault(w => w.World == adventure.Value) ?? Worlds[0];
            Changed(nameof(World));
        }

        var overrides = OverridesForCharacter();
        foreach (var t in Tiles)
            t.ManualOverride = overrides.TryGetValue(t.Item.AssetName, out bool o) ? o : null;
        Refresh();
        BuildRunTree();
        RefreshDetails();
    }

    private CharacterRuns? CurrentCharacter => _characterIndex < _characters.Count ? _characters[_characterIndex] : null;

    private RunInfo? CurrentRun => _run is null ? null : CurrentCharacter?.Get(_run.Slot);

    private void ApplyCharacter()
    {
        var character = CurrentCharacter;
        UpdateRunChoices(character);
        var run = CurrentRun;

        if (character is not null && run is not null)
        {
            var byId = RunAnalysis.Evaluate(_db, character, run).ToDictionary(s => s.Item.AssetName);
            foreach (var t in Tiles)
            {
                var s = byId[t.Item.AssetName];
                t.SetStatus(s.Status, s.Reason, s.Where);
            }
        }
        else
        {
            // Inventory-only (fallback reader) or no save at all.
            var inventory = _characterIndex < _fallbackInventories.Count ? _fallbackInventories[_characterIndex] : null;
            var owned = inventory?.Items.Select(i => i.AssetName).ToHashSet() ?? [];
            foreach (var t in Tiles)
            {
                bool has = owned.Contains(t.Item.AssetName);
                t.SetStatus(has ? RunItemStatus.Owned : RunItemStatus.Unknown, has ? "Owned" : "Run info unavailable", null);
            }
        }

        var overrides = OverridesForCharacter();
        foreach (var t in Tiles)
            t.ManualOverride = overrides.TryGetValue(t.Item.AssetName, out bool o) ? o : null;

        Refresh();
        BuildRunTree();
        RefreshDetails();
    }

    private void UpdateRunChoices(CharacterRuns? character)
    {
        var choices = new List<RunChoice>();
        if (character is not null)
        {
            foreach (var run in new[] { character.Campaign, character.Adventure })
            {
                if (run is null) continue;
                string worlds = string.Join(", ", run.Zones.Where(z => z.World != "Ward 13").Select(z => z.World));
                string active = run.Slot == character.ActiveSlot ? " (playing)" : "";
                choices.Add(new RunChoice($"{run.Slot}{active}: {worlds}", run.Slot));
            }
        }

        // Keep the user's pick when the save reloads; otherwise follow the run being played.
        var wanted = _userPickedRun && _run is not null ? _run.Slot : character?.ActiveSlot;
        Runs.Clear();
        foreach (var c in choices) Runs.Add(c);
        _run = choices.FirstOrDefault(c => c.Slot == wanted) ?? choices.FirstOrDefault();
        Changed(nameof(Run));
    }

    private void BuildRunTree()
    {
        RunTree.Clear();
        var run = CurrentRun;
        if (run is null)
        {
            RunSummary = _manualMode || _characters.Count == 0
                ? "No run detected. Pick the world you rolled in the world filter."
                : "";
            QuestStatus = "";
            NothingLeft = false;
            return;
        }

        // Missing tracked items by db Id, after manual overrides.
        var tilesById = Tiles.ToDictionary(t => t.Item.Id);
        bool Wanted(RunItem item) => tilesById.TryGetValue(item.Id, out var t) && !t.Owned && Collectable.Contains(item.Status);

        int available = 0, gated = 0;
        foreach (var zone in run.Zones)
        {
            var locationNodes = new List<RunNode>();
            foreach (var location in zone.Locations)
            {
                var sourceNodes = new List<RunNode>();
                foreach (var source in location.Sources)
                {
                    var items = source.Items.Where(Wanted).ToList();
                    if (items.Count == 0) continue;
                    available += items.Count(i => i.Status == RunItemStatus.Available);
                    gated += items.Count(i => i.Status == RunItemStatus.NeedsPrerequisite);
                    sourceNodes.Add(new RunNode
                    {
                        Title = source.Name == location.Name ? "Around the area" : source.Name,
                        Detail = source.Name == location.Name ? null : source.Kind,
                        IsExpanded = true,
                        Children = items.Select(i => new RunNode
                        {
                            Title = i.Name,
                            Detail = i.Status switch
                            {
                                RunItemStatus.NeedsPrerequisite => $"needs {i.Prerequisite ?? "a quest step"}",
                                RunItemStatus.MissedThisRun => "already taken or skipped this run",
                                _ => i.HasRequiredMaterial ? "ready to craft" : null,
                            },
                            Status = i.Status,
                            ItemId = i.Id,
                        }).ToList(),
                    });
                }

                int count = sourceNodes.Sum(s => s.Children.Count(c => c.Status != RunItemStatus.MissedThisRun));
                string stones = location.WorldStones.Count > 0 ? $"  ·  stones: {string.Join(", ", location.WorldStones)}" : "";
                locationNodes.Add(new RunNode
                {
                    Title = location.Name,
                    Detail = (count == 0 ? "nothing new" : $"{count} to collect") + stones,
                    IsExpanded = count > 0,
                    Children = sourceNodes,
                });
            }

            int zoneCount = locationNodes.Sum(l => l.Children.Sum(s => s.Children.Count(c => c.Status != RunItemStatus.MissedThisRun)));
            string story = zone.World == "Ward 13" ? "" : $"{zone.Story}: {(zone.StoryFinished ? "story done" : "story in progress")}  ·  ";
            RunTree.Add(new RunNode
            {
                Title = zone.World,
                Detail = story + (zoneCount == 0 ? "nothing new" : $"{zoneCount} to collect"),
                IsExpanded = zoneCount > 0 && zone.World != "Ward 13",
                Children = locationNodes,
            });
        }

        NothingLeft = available == 0 && gated == 0;
        RunSummary = NothingLeft
            ? $"Nothing new to collect in this {run.Slot.ToString().ToLowerInvariant()}. Time to reroll."
            : $"{run.Slot} ({run.Difficulty}): {available} available now, {gated} need a quest or unlock first.";

        var questLines = run.Zones.Where(z => z.World != "Ward 13")
            .Select(z => $"{z.World}: {z.Story} ({(z.StoryFinished ? "done" : "in progress")})").ToList();
        if (run.QuestItems.Count > 0) questLines.Add($"Quest items held: {string.Join(", ", run.QuestItems)}");
        if (run.RespawnPoint is not null) questLines.Add($"Last checkpoint: {run.RespawnPoint}");
        QuestStatus = string.Join(Environment.NewLine, questLines);
    }

    private Dictionary<string, bool> OverridesForCharacter()
    {
        if (!_settings.ManualOverrides.TryGetValue(_characterIndex, out var overrides))
            _settings.ManualOverrides[_characterIndex] = overrides = [];
        return overrides;
    }

    private Dictionary<string, DateTimeOffset> OverrideTimesForCharacter()
    {
        if (!_settings.ManualOverrideTimes.TryGetValue(_characterIndex, out var times))
            _settings.ManualOverrideTimes[_characterIndex] = times = [];
        return times;
    }

    private bool Matches(ItemTileViewModel t)
    {
        if (_category == null) return true;
        if (!_category.Categories.Contains(t.Item.Category!.Value)) return false;
        if (_world.World is not null && t.Item.World != _world.World) return false;
        bool show = _show switch
        {
            "Missing" => !t.Owned,
            "Available this run" => !t.Owned && t.Status == RunItemStatus.Available,
            "Needs quest" => !t.Owned && t.Status == RunItemStatus.NeedsPrerequisite,
            "Owned" => t.Owned,
            _ => true,
        };
        if (!show) return false;
        return _search.Length == 0
               || t.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
               || (t.Item.DropReference?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false)
               || (t.Where?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false);
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
