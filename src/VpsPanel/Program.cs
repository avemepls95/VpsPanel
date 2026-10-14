using System.Text.Json;
using System.Text.Json.Serialization;
using VpsPanel.Api;
using VpsPanel.Files;
using VpsPanel.Hosting;
using VpsPanel.Overview;
using VpsPanel.Projects;
using VpsPanel.Reboot;
using VpsPanel.Runs;
using VpsPanel.Security;
using VpsPanel.Servers;
using VpsPanel.Settings;
using VpsPanel.Ssh;

// A relative VpsPanel:DataDir is resolved from the launch folder.
var launchDir = Environment.CurrentDirectory;

// A shortcut may start the exe with a different working folder, while wwwroot and appsettings.json are next to the exe.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(PanelUrls.Listen);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<PanelExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
    new SettingsPaths(Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["VpsPanel:DataDir"] ?? SettingsPaths.Default, launchDir)));
builder.Services.AddSingleton<ISettingsStore, SettingsStore>();
builder.Services.AddSingleton<ISshConnector, SshConnector>();
builder.Services.AddSingleton<OverviewService>();
builder.Services.AddHttpClient<IProviderLookup, IpInfoProviderLookup>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<RunManager>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton(RebootTiming.Default);
builder.Services.AddSingleton<RebootService>();
builder.Services.AddSingleton<BackupStore>();
builder.Services.AddSingleton<FileService>();
builder.Services.AddSingleton<CopyService>();
builder.Services.AddHostedService<BrowserLauncher>();

var app = builder.Build();

if (app.Configuration.GetValue("VpsPanel:SingleInstance", true))
{
    switch (await SingleInstance.Check(PanelUrls.Port))
    {
        case SingleInstanceResult.AlreadyRunning:
            BrowserLauncher.Open(PanelUrls.Browser);
            return;
        case SingleInstanceResult.PortBusy:
            Console.Error.WriteLine($"Port {PanelUrls.Port} is used by another program, VpsPanel is not started.");
            Environment.ExitCode = 1;
            return;
    }
}

app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/ping", () => new { app = "VpsPanel" });
app.MapGet("/api/settings/status", (ISettingsStore settings) => new { error = settings.GetParseError() });
app.MapServerEndpoints();
app.MapRunEndpoints();
app.MapProjectEndpoints();
app.MapRebootEndpoints();
app.MapFileEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;