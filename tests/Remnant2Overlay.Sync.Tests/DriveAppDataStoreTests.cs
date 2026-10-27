using System.Net;
using System.Text;
using Remnant2Overlay.Sync;

namespace Remnant2Overlay.Sync.Tests;

public class DriveAppDataStoreTests
{
    /// <summary>Just enough of the Drive v3 API for the store: list, download, multipart create, media update.</summary>
    private sealed class FakeDrive : HttpMessageHandler
    {
        public string? Content;
        public string? CreatedMetadata;
        public List<string> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer token-1", request.Headers.Authorization?.ToString());
            string url = request.RequestUri!.ToString();
            Calls.Add($"{request.Method} {url}");

            if (request.Method == HttpMethod.Get && url.Contains("spaces=appDataFolder"))
                return Json(Content is null ? """{"files":[]}""" : """{"files":[{"id":"f1"}]}""");
            if (request.Method == HttpMethod.Get && url.Contains("/files/f1?alt=media"))
                return Json(Content!);
            if (request.Method == HttpMethod.Post && url.Contains("uploadType=multipart"))
            {
                var parts = (MultipartContent)request.Content!;
                CreatedMetadata = await parts.First().ReadAsStringAsync(ct);
                Content = await parts.Last().ReadAsStringAsync(ct);
                return Json("""{"id":"f1"}""");
            }
            if (request.Method == HttpMethod.Patch && url.Contains("/files/f1?uploadType=media"))
            {
                Content = await request.Content!.ReadAsStringAsync(ct);
                return Json("{}");
            }
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("nope") };
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task CreatesThenUpdatesTheFileInAppData()
    {
        var drive = new FakeDrive();
        var store = new DriveAppDataStore(new HttpClient(drive), _ => Task.FromResult("token-1"));

        Assert.Null(await store.DownloadAsync());

        var doc = new SyncDocument { UpdatedBy = "overlay", AdventureWorld = new Stamped<string?>("World_Fae", DateTimeOffset.UnixEpoch) };
        await store.UploadAsync(doc);
        Assert.Contains("appDataFolder", drive.CreatedMetadata);
        Assert.Contains(SyncDocument.FileName, drive.CreatedMetadata);

        doc.UpdatedBy = "phone";
        await store.UploadAsync(doc);
        Assert.Contains(drive.Calls, c => c.StartsWith("PATCH"));

        var fresh = new DriveAppDataStore(new HttpClient(drive), _ => Task.FromResult("token-1"));
        var read = await fresh.DownloadAsync();
        Assert.Equal("phone", read!.UpdatedBy);
        Assert.Equal("World_Fae", read.AdventureWorld!.Value);
    }

    [Fact]
    public async Task RejectedTokenAsksForSignIn()
    {
        var bad = new DriveAppDataStore(new HttpClient(new RejectAll()), _ => Task.FromResult("x"));

        var e = await Assert.ThrowsAsync<DriveSyncException>(() => bad.DownloadAsync());
        Assert.True(e.NeedsSignIn);
    }

    private sealed class RejectAll : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("expired") });
    }
}
