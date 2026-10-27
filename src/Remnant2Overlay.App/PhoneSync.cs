using System.IO;
using System.Net.Http;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.App;

/// <summary>
/// Google sign-in for the overlay and the sync round trip with the phone app through the user's Drive app data
/// folder. Sign-in uses the Desktop OAuth client the user downloads into <see cref="ClientFile"/> (see README).
/// </summary>
public sealed class PhoneSync
{
    public static string ClientFile => Path.Combine(Settings.DataDir, "google-client.json");
    private static string TokenDir => Path.Combine(Settings.DataDir, "google-token");

    private static readonly HttpClient Http = new();

    private UserCredential? _credential;
    private DriveAppDataStore? _store;

    public static bool HasClientFile => File.Exists(ClientFile);

    public bool SignedIn => _credential is not null;

    /// <summary>
    /// Signs in, opening the browser the first time (or after sign-out); later starts reuse the saved token
    /// without asking.
    /// </summary>
    public async Task SignInAsync(CancellationToken ct = default)
    {
        var secrets = GoogleClientSecrets.FromFile(ClientFile).Secrets;
        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, [DriveAppDataStore.Scope], "user", ct, new FileDataStore(TokenDir, fullPath: true));
        _store = new DriveAppDataStore(Http, token => _credential.GetAccessTokenForRequestAsync(cancellationToken: token));
    }

    /// <summary>Forgets the saved token and revokes it with Google. The sync file stays in Drive.</summary>
    public async Task SignOutAsync()
    {
        try
        {
            if (_credential is not null) await _credential.RevokeTokenAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Token revoke failed: {e.Message}");
        }
        await new FileDataStore(TokenDir, fullPath: true).ClearAsync();
        _credential = null;
        _store = null;
    }

    /// <summary>
    /// Pulls the phone's changes, merges them with <paramref name="local"/>, and uploads the result when the phone
    /// doesn't have it yet. Returns the merged file for the overlay to apply.
    /// </summary>
    public async Task<SyncDocument> SyncAsync(SyncDocument local, CancellationToken ct = default)
    {
        if (_store is null) throw new InvalidOperationException("Not signed in.");
        var remote = await _store.DownloadAsync(ct);
        var merged = SyncMerge.Merge(local, remote);
        if (SyncMerge.DiffersFrom(merged, remote))
        {
            merged.UpdatedAt = DateTimeOffset.UtcNow;
            merged.UpdatedBy = "overlay";
            await _store.UploadAsync(merged, ct);
        }
        return merged;
    }
}
