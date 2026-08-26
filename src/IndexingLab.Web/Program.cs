using IndexingLab.Core;
using IndexingLab.Web;
using IndexingLab.Web.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Sessions live in one folder so they can be copied, diffed and committed next to a report.
string sessionsPath = builder.Configuration["Lab:SessionsPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "sessions");

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
