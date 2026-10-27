using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Companion.Services;

/// <summary>
/// The phone's copy of the sync file: cached in the browser, merged with Drive on every sync. The phone only
/// changes manual marks and the adventure pick; the save snapshot always comes from the overlay.
/// </summary>
public sealed class SyncService
{
    private const string DocKey = "r2c.doc";

    private readonly LocalCache _cache;
    private readonly GoogleAuth _auth;
    private readonly DriveAppDataStore _store;
    private bool _syncing;

    public SyncService(LocalCache cache, GoogleAuth auth, HttpClient http)
    {
        _cache = cache;
        _auth = auth;
        _store = new DriveAppDataStore(http, auth.GetTokenAsync);
    }

    public SyncDocument Doc { get; private set; } = new();
    public DateTimeOffset? LastSynced { get; private set; }
    public string Status { get; private set; } = "";
    public bool Syncing => _syncing;

    /// <summary>Raised whenever Doc or Status changes.</summary>
    public event Action? Changed;

    public async Task LoadCachedAsync()
    {
        if (await _cache.GetAsync(DocKey) is { } json)
        {
            try { Doc = SyncDocument.Parse(json); }
            catch (Exception e) { Console.WriteLine($"Ignoring unreadable cached data: {e.Message}"); }
        }
        Changed?.Invoke();
    }

    /// <summary>Pulls the overlay's latest data and pushes the phone's changes. Needs a signed-in token.</summary>
    public async Task SyncAsync()
    {
        if (_syncing) return;
        _syncing = true;
        ShowStatus("Syncing…");
        try
        {
            var remote = await _store.DownloadAsync();
            var merged = SyncMerge.Merge(Doc, remote);
            if (SyncMerge.DiffersFrom(merged, remote))
            {
                merged.UpdatedAt = DateTimeOffset.UtcNow;
                merged.UpdatedBy = "phone";
                await _store.UploadAsync(merged);
            }
            Doc = merged;
            LastSynced = DateTimeOffset.Now;
            await _cache.SetAsync(DocKey, Doc.ToJson());
            ShowStatus(remote is null && Doc.Save is null
                ? "Signed in. Nothing from the overlay yet: turn on Phone sync in the overlay on your PC."
                : $"Synced at {LastSynced:t}");
        }
        catch (SignInRequiredException e)
        {
            ShowStatus(e.Message);
        }
        catch (DriveSyncException e) when (e.NeedsSignIn)
        {
            await _auth.ForgetTokenAsync();
            ShowStatus("Google sign-in expired. Tap Sign in.");
        }
        catch (Exception e)
        {
            ShowStatus($"Sync failed: {e.Message}");
        }
        finally
        {
            _syncing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Marks an item owned (true), missing (false), or back to following the save (null).</summary>
    public async Task SetMarkAsync(int character, string asset, bool? owned)
    {
        if (!Doc.ManualOverrides.TryGetValue(character, out var marks))
            Doc.ManualOverrides[character] = marks = [];
        marks[asset] = new OverrideMark(owned, DateTimeOffset.UtcNow);
        await SaveAndPushAsync();
    }

    public async Task SetAdventureWorldAsync(string? world)
    {
        if (Doc.AdventureWorld?.Value == world) return;
        Doc.AdventureWorld = new Stamped<string?>(world, DateTimeOffset.UtcNow);
        await SaveAndPushAsync();
    }

    public async Task ForgetAsync()
    {
        Doc = new SyncDocument();
        LastSynced = null;
        await _cache.RemoveAsync(DocKey);
        ShowStatus("Signed out.");
    }

    private async Task SaveAndPushAsync()
    {
        await _cache.SetAsync(DocKey, Doc.ToJson());
        Changed?.Invoke();
        // Without a token the change waits in the cache and goes up with the next sync.
        if (_auth.HasToken) await SyncAsync();
        else ShowStatus("Change saved on this phone. Tap Sign in to send it to the overlay.");
    }

    public void ShowStatus(string status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
