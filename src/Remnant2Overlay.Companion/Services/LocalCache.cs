using Microsoft.JSInterop;

namespace Remnant2Overlay.Companion.Services;

/// <summary>The browser's localStorage, so the app opens with the last synced data even offline.</summary>
public sealed class LocalCache(IJSRuntime js)
{
    public ValueTask<string?> GetAsync(string key) => js.InvokeAsync<string?>("localCache.get", key);
    public ValueTask SetAsync(string key, string value) => js.InvokeVoidAsync("localCache.set", key, value);
    public ValueTask RemoveAsync(string key) => js.InvokeVoidAsync("localCache.remove", key);
}
