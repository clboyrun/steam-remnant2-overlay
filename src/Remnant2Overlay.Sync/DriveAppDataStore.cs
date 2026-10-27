using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remnant2Overlay.Sync;

/// <summary>
/// Reads and writes the sync file in Google Drive's hidden app data folder (scope drive.appdata). Only OAuth
/// clients in the same Google Cloud project can see that folder, and the user can't browse it in Drive.
/// Talks to the Drive v3 REST API directly so it runs anywhere HttpClient does, including in the browser.
/// </summary>
public sealed class DriveAppDataStore(HttpClient http, Func<CancellationToken, Task<string>> getAccessToken)
{
    public const string Scope = "https://www.googleapis.com/auth/drive.appdata";

    private const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files";

    private string? _fileId;

    /// <summary>Downloads the sync file, or returns null if it hasn't been created yet.</summary>
    public async Task<SyncDocument?> DownloadAsync(CancellationToken ct = default)
    {
        string? id = await FindFileIdAsync(ct);
        if (id is null) return null;

        using var response = await SendAsync(HttpMethod.Get, $"{FilesUrl}/{id}?alt=media", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _fileId = null;
            return null;
        }
        await EnsureSuccessAsync(response, ct);
        return SyncDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Creates or replaces the sync file.</summary>
    public async Task UploadAsync(SyncDocument doc, CancellationToken ct = default)
    {
        var body = new StringContent(doc.ToJson(), Encoding.UTF8, "application/json");
        string? id = await FindFileIdAsync(ct);
        if (id is not null)
        {
            using var response = await SendAsync(HttpMethod.Patch, $"{UploadUrl}/{id}?uploadType=media", body, ct);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(response, ct);
                return;
            }
            // Deleted since we looked it up (e.g. the user removed the app's data in Drive settings): create it again.
            _fileId = null;
            body = new StringContent(doc.ToJson(), Encoding.UTF8, "application/json");
        }

        string metadata = JsonSerializer.Serialize(new { name = SyncDocument.FileName, parents = new[] { "appDataFolder" } });
        var multipart = new MultipartContent("related")
        {
            new StringContent(metadata, Encoding.UTF8, "application/json"),
            body,
        };
        using var created = await SendAsync(HttpMethod.Post, $"{UploadUrl}?uploadType=multipart&fields=id", multipart, ct);
        await EnsureSuccessAsync(created, ct);
        _fileId = JsonSerializer.Deserialize<FileRef>(await created.Content.ReadAsStringAsync(ct))?.Id;
    }

    /// <summary>Deletes the sync file, for a "forget my data" action.</summary>
    public async Task DeleteAsync(CancellationToken ct = default)
    {
        string? id = await FindFileIdAsync(ct);
        if (id is null) return;
        using var response = await SendAsync(HttpMethod.Delete, $"{FilesUrl}/{id}", null, ct);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(response, ct);
        _fileId = null;
    }

    private async Task<string?> FindFileIdAsync(CancellationToken ct)
    {
        if (_fileId is not null) return _fileId;
        string q = Uri.EscapeDataString($"name = '{SyncDocument.FileName}' and trashed = false");
        using var response = await SendAsync(HttpMethod.Get,
            $"{FilesUrl}?spaces=appDataFolder&q={q}&fields=files(id)&orderBy=modifiedTime desc&pageSize=1", null, ct);
        await EnsureSuccessAsync(response, ct);
        var list = JsonSerializer.Deserialize<FileList>(await response.Content.ReadAsStringAsync(ct));
        return _fileId = list?.Files.FirstOrDefault()?.Id;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await getAccessToken(ct));
        return await http.SendAsync(request, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync(ct);
        throw new DriveSyncException(response.StatusCode, $"Google Drive returned {(int)response.StatusCode}: {detail}");
    }

    private sealed record FileRef([property: JsonPropertyName("id")] string Id);

    private sealed record FileList([property: JsonPropertyName("files")] List<FileRef> Files);
}

public sealed class DriveSyncException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>The token was rejected or lacks the app data scope; signing in again fixes it.</summary>
    public bool NeedsSignIn => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
