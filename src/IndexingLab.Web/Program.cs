using IndexingLab.Core;
using IndexingLab.Web;
using IndexingLab.Web.Components;

// The content root defaults to the *current working directory*, so a published exe launched from
// anywhere other than its own folder resolves wwwroot to the wrong place and serves 404 for every
// static file -- including blazor.web.js, which leaves a UI that renders but does nothing.
// Detect publish output (wwwroot sitting next to the executable) and pin the content root to it.
// Under `dotnet run`, wwwroot lives in the project directory rather than in bin, so the default is
// already correct and must be left alone.
string publishedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
WebApplicationBuilder builder = WebApplication.CreateBuilder(
    Directory.Exists(publishedWebRoot)
        ? new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory }
        : new WebApplicationOptions { Args = args });

// Static web assets, and why this is guarded rather than called unconditionally.
//
// Blazor's client script lives in the framework's static web assets rather than in our wwwroot,
// and the host only wires those up automatically in the Development environment. `dotnet run -c
// Release` from the project directory runs as Production, so without this _framework/blazor.web.js
// 404s: no script, no SignalR circuit, no @onclick. The page still renders correctly through
// static SSR, which makes it look like a dead button rather than a hosting problem.
//
// But a *published* app has no runtime manifest -- publish copies every asset physically into
// wwwroot instead -- and calling this there replaces the web root with an empty one, 404-ing even
// app.css. So: call it exactly when the manifest that makes it work is present.
string staticWebAssetsManifest = Path.Combine(
    AppContext.BaseDirectory,
    $"{System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name}.staticwebassets.runtime.json");

if (File.Exists(staticWebAssetsManifest))
{
    builder.WebHost.UseStaticWebAssets();
}

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Sessions live in one folder so they can be copied, diffed and committed next to a report.
// Shipped defaults are relative so a fresh clone works on any machine; absolute paths in an
// appsettings.local.json (git-ignored) or the environment override them.
string sessionsPath = LabPaths.Resolve(
    Environment.GetEnvironmentVariable("INDEXING_LAB_SESSIONS")
        ?? builder.Configuration["Lab:SessionsPath"]
        ?? "sessions");

builder.Services.AddSingleton(new SessionStore(sessionsPath));
builder.Services.AddSingleton<RunnerService>();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Logger.LogInformation("Sessions folder: {Path}", sessionsPath);

app.Run();
