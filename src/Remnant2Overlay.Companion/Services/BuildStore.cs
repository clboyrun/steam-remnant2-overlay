using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Remnant2Overlay.Planner;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Companion.Services;

/// <summary>Planned builds, kept in this browser like the console characters.</summary>
public sealed class BuildStore(LocalCache cache)
{
    private const string BookKey = "r2c.builds";

    private BuildBook _book = new();

    public IReadOnlyList<Build> Builds => _book.Builds;

    public Build? Active => _book.Builds.FirstOrDefault(b => b.Id == _book.ActiveId) ?? _book.Builds.FirstOrDefault();

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (await cache.GetAsync(BookKey) is { } json)
        {
            try { _book = JsonSerializer.Deserialize<BuildBook>(json, SyncDocument.JsonOptions) ?? new(); }
            catch (JsonException e) { Console.WriteLine($"Ignoring unreadable saved builds: {e.Message}"); }
        }
        Changed?.Invoke();
    }

    public Task CreateAsync(string name, string? linkedCharacter)
    {
        var build = new Build { Name = string.IsNullOrWhiteSpace(name) ? $"Build {_book.Builds.Count + 1}" : name.Trim(), LinkedCharacter = linkedCharacter };
        _book.Builds.Add(build);
        _book.ActiveId = build.Id;
        return SaveAsync();
    }

    public Task SwitchAsync(string id)
    {
        _book.ActiveId = id;
        return SaveAsync();
    }

    public Task DeleteAsync(string id)
    {
        _book.Builds.RemoveAll(b => b.Id == id);
        if (_book.ActiveId == id) _book.ActiveId = _book.Builds.FirstOrDefault()?.Id;
        return SaveAsync();
    }

    /// <summary>Call after changing the active build in place.</summary>
    public Task SaveAsync()
    {
        if (Active is { } build) build.UpdatedAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
        return cache.SetAsync(BookKey, JsonSerializer.Serialize(_book, SyncDocument.JsonOptions)).AsTask();
    }
}

public sealed class BuildBook
{
    public int Schema { get; set; } = 1;
    public string? ActiveId { get; set; }
    public List<Build> Builds { get; set; } = [];
}

/// <summary>planner.json, fetched the first time the planner opens (it isn't needed for the item lists).</summary>
public sealed class PlannerData(HttpClient http, NavigationManager nav)
{
    private Task<PlannerDatabase>? _load;

    public Task<PlannerDatabase> LoadAsync() => _load ??= LoadCoreAsync();

    private async Task<PlannerDatabase> LoadCoreAsync()
    {
        try
        {
            return PlannerDatabase.Parse(await http.GetStringAsync(new Uri(new Uri(nav.BaseUri), "data/planner.json")));
        }
        catch
        {
            _load = null; // try again next time, e.g. after coming back online
            throw;
        }
    }
}
