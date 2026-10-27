using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Remnant2Overlay.Companion;
using Remnant2Overlay.Companion.Services;
using Remnant2Overlay.SaveReader;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Talks to Google's APIs only, so no base address.
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton(_ => ItemDatabase.LoadEmbedded());
builder.Services.AddSingleton<LocalCache>();
builder.Services.AddSingleton<GoogleAuth>();
builder.Services.AddSingleton<SyncService>();

await builder.Build().RunAsync();
