using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VpsPanel.Settings;

namespace VpsPanel.Tests.Infrastructure;

internal sealed class TestApp(Action<IServiceCollection>? configure = null) : WebApplicationFactory<Program>
{
    private readonly TempDirectory _dir = new();

    public string DataDir => _dir.Path;

    public ISettingsStore Settings => Services.GetRequiredService<ISettingsStore>();

    public HttpClient CreatePanelClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-VpsPanel", "1");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("VpsPanel:DataDir", _dir.Path);
        builder.UseSetting("VpsPanel:SingleInstance", "false");
        builder.UseSetting("VpsPanel:OpenBrowser", "false");
        builder.ConfigureTestServices(services => configure?.Invoke(services));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _dir.Dispose();
    }
}