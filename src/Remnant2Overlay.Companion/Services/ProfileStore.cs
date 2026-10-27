using System.Text.Json;
using Remnant2Overlay.SaveReader;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Companion.Services;

/// <summary>
/// Characters kept on this device for players without the PC overlay (consoles, or a PC without it). Each one has
/// its own owned items, ticked off by hand or imported from a PC profile.sav. Stored in the browser only.
/// </summary>
public sealed class ProfileStore(LocalCache cache, ItemDatabase db)
{
    private const string BookKey = "r2c.profiles";

    private ProfileBook _book = new();

    public IReadOnlyList<LocalProfile> Profiles => _book.Profiles;

    public LocalProfile? Active => _book.Profiles.FirstOrDefault(p => p.Id == _book.ActiveId) ?? _book.Profiles.FirstOrDefault();

    /// <summary>Raised whenever a profile or the active one changes.</summary>
    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (await cache.GetAsync(BookKey) is { } json)
        {
            try { _book = JsonSerializer.Deserialize<ProfileBook>(json, SyncDocument.JsonOptions) ?? new(); }
            catch (JsonException e) { Console.WriteLine($"Ignoring unreadable saved characters: {e.Message}"); }
        }
        Changed?.Invoke();
    }

    public async Task<LocalProfile> CreateAsync(string name, IEnumerable<string>? owned = null, string? source = null)
    {
        var profile = new LocalProfile
        {
            Name = UniqueName(string.IsNullOrWhiteSpace(name) ? "Character" : name.Trim()),
            Owned = owned?.ToHashSet(StringComparer.Ordinal) ?? [],
            Source = source,
        };
        _book.Profiles.Add(profile);
        _book.ActiveId = profile.Id;
        await SaveAsync();
        return profile;
    }

    public Task SwitchAsync(string id)
    {
        _book.ActiveId = id;
        return SaveAsync();
    }

    public Task RenameAsync(string id, string name)
    {
        if (Find(id) is not { } profile || string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        profile.Name = name.Trim();
        return TouchAndSaveAsync(profile);
    }

    public Task DeleteAsync(string id)
    {
        _book.Profiles.RemoveAll(p => p.Id == id);
        if (_book.ActiveId == id) _book.ActiveId = _book.Profiles.FirstOrDefault()?.Id;
        return SaveAsync();
    }

    public Task SetOwnedAsync(string asset, bool owned)
    {
        if (Active is not { } profile) return Task.CompletedTask;
        if (owned) profile.Owned.Add(asset);
        else profile.Owned.Remove(asset);
        return TouchAndSaveAsync(profile);
    }

    public Task SetAdventureWorldAsync(string? world)
    {
        if (Active is not { } profile || profile.AdventureWorld == world) return Task.CompletedTask;
        profile.AdventureWorld = world;
        return TouchAndSaveAsync(profile);
    }

    /// <summary>The active profile as a character snapshot, so the overlay-sync views can show it unchanged.</summary>
    public CharacterSnapshot? ActiveCharacter => Active is { } p
        ? new CharacterSnapshot { Description = p.Name, Owned = [.. p.Owned] }
        : null;

    /// <summary>
    /// Adds a profile.sav (one new profile per character in it) or a backup made by <see cref="ExportJson"/>.
    /// Returns a line for the status bar.
    /// </summary>
    public async Task<string> ImportAsync(string fileName, byte[] data)
    {
        if (data.Length > 0 && data[0] == (byte)'{') return await ImportBackupAsync(data);

        IReadOnlyList<CharacterInventory> characters;
        try
        {
            characters = ProfileReader.Read(SaveFile.Decompress(data));
        }
        catch (InvalidDataException)
        {
            return $"{fileName} isn't a Remnant 2 save or a backup from this app.";
        }
        if (characters.Count == 0)
            return $"No characters in {fileName}. Pick profile.sav (save_N.sav files hold the world, not your items).";

        // Steam's file is always profile.sav, which makes a poor character name.
        string baseName = Path.GetFileNameWithoutExtension(fileName) is var n && !n.StartsWith("profile", StringComparison.OrdinalIgnoreCase)
            ? n
            : "Steam character";
        foreach (var character in characters)
        {
            var owned = character.Items.Select(i => i.AssetName).Where(a => db.Find(a) is not null);
            await CreateAsync(characters.Count == 1 ? baseName : $"{baseName} #{character.Index + 1}", owned, fileName);
        }
        return characters.Count == 1
            ? $"Added {Active!.Name} with {Active.Owned.Count} items from {fileName}."
            : $"Added {characters.Count} characters from {fileName}.";
    }

    /// <summary>All profiles as JSON, to move them to another phone or browser.</summary>
    public string ExportJson() => JsonSerializer.Serialize(_book, SyncDocument.JsonOptions);

    private async Task<string> ImportBackupAsync(byte[] data)
    {
        ProfileBook? backup;
        try { backup = JsonSerializer.Deserialize<ProfileBook>(data, SyncDocument.JsonOptions); }
        catch (JsonException) { backup = null; }
        if (backup is null || backup.Profiles.Count == 0) return "That file has no characters in it.";

        // Same id: the newer copy wins, so importing an older backup doesn't undo ticks made since.
        int added = 0, updated = 0;
        foreach (var incoming in backup.Profiles)
        {
            int at = _book.Profiles.FindIndex(p => p.Id == incoming.Id);
            if (at < 0) { _book.Profiles.Add(incoming); added++; }
            else if (incoming.UpdatedAt > _book.Profiles[at].UpdatedAt) { _book.Profiles[at] = incoming; updated++; }
        }
        _book.ActiveId ??= backup.ActiveId;
        await SaveAsync();
        return $"Backup imported: {added} added, {updated} updated.";
    }

    private LocalProfile? Find(string id) => _book.Profiles.FirstOrDefault(p => p.Id == id);

    private string UniqueName(string name)
    {
        string candidate = name;
        for (int n = 2; _book.Profiles.Any(p => p.Name == candidate); n++) candidate = $"{name} ({n})";
        return candidate;
    }

    private Task TouchAndSaveAsync(LocalProfile profile)
    {
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        await cache.SetAsync(BookKey, ExportJson());
        Changed?.Invoke();
    }
}

public sealed class ProfileBook
{
    public int Schema { get; set; } = 1;
    public string? ActiveId { get; set; }
    public List<LocalProfile> Profiles { get; set; } = [];
}

public sealed class LocalProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Asset names (Ring_AkariWarBand) of the items this character owns.</summary>
    public HashSet<string> Owned { get; set; } = new(StringComparer.Ordinal);

    /// <summary>World picked as the rolled adventure (e.g. World_Fae); null means all worlds.</summary>
    public string? AdventureWorld { get; set; }

    /// <summary>File it was imported from, if any.</summary>
    public string? Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
