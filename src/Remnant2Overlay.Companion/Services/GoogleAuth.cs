using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Companion.Services;

/// <summary>
/// Google sign-in through Google Identity Services (js/google-auth.js). Access tokens last about an hour; a new one
/// needs a tap, because browsers only open Google's popup in response to one.
/// </summary>
public sealed class GoogleAuth(IJSRuntime js, IConfiguration config, LocalCache cache, HttpClient http)
{
    private const string Scopes = DriveAppDataStore.Scope + " openid email";
    private const string ClientIdKey = "r2c.clientId", TokenKey = "r2c.token", EmailKey = "r2c.email";

    private string? _token;
    private DateTimeOffset _expires;
    private bool _initialized;

    /// <summary>OAuth Web client ID: set when the site is built, or entered in the app.</summary>
    public string? ClientId { get; private set; }

    /// <summary>The Google account last signed in, used to skip the account picker.</summary>
    public string? Email { get; private set; }

    public bool HasToken => _token is not null && DateTimeOffset.UtcNow < _expires.AddMinutes(-1);

    public async Task LoadAsync()
    {
        string? configured = config["GoogleClientId"];
        ClientId = string.IsNullOrWhiteSpace(configured) ? await cache.GetAsync(ClientIdKey) : configured;
        Email = await cache.GetAsync(EmailKey);
        if (await cache.GetAsync(TokenKey) is { } saved)
        {
            try
            {
                var token = JsonSerializer.Deserialize<SavedToken>(saved);
                if (token is not null) (_token, _expires) = (token.AccessToken, token.Expires);
            }
            catch (JsonException) { }
        }
    }

    public async Task SetClientIdAsync(string clientId)
    {
        ClientId = clientId.Trim();
        _initialized = false;
        await cache.SetAsync(ClientIdKey, ClientId);
    }

    /// <summary>Gets a fresh token. Call from a tap; the first time Google asks to allow access.</summary>
    public async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId)) throw new InvalidOperationException("No Google client ID set.");
        if (!_initialized)
        {
            await js.InvokeVoidAsync("googleAuth.init", ClientId, Scopes);
            _initialized = true;
        }

        var result = await js.InvokeAsync<TokenResult>("googleAuth.requestToken", "", Email);
        _token = result.AccessToken;
        _expires = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn);
        await cache.SetAsync(TokenKey, JsonSerializer.Serialize(new SavedToken(_token, _expires)));

        if (Email is null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var response = await http.SendAsync(request);
                Email = (await response.Content.ReadFromJsonAsync<UserInfo>())?.Email;
                if (Email is not null) await cache.SetAsync(EmailKey, Email);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Couldn't read the Google account email: {e.Message}");
            }
        }
    }

    public Task<string> GetTokenAsync(CancellationToken ct) =>
        HasToken ? Task.FromResult(_token!) : throw new SignInRequiredException();

    /// <summary>Called when Google rejects the token (expired early or revoked).</summary>
    public async Task ForgetTokenAsync()
    {
        _token = null;
        await cache.RemoveAsync(TokenKey);
    }

    public async Task SignOutAsync()
    {
        if (_token is not null && _initialized) await js.InvokeVoidAsync("googleAuth.revoke", _token);
        _token = null;
        Email = null;
        await cache.RemoveAsync(TokenKey);
        await cache.RemoveAsync(EmailKey);
    }

    private sealed record TokenResult(string AccessToken, int ExpiresIn);
    private sealed record SavedToken(string AccessToken, DateTimeOffset Expires);
    private sealed record UserInfo(string? Email);
}

public sealed class SignInRequiredException() : Exception("Tap Sign in to sync with Google Drive.");
