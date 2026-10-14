using System.Diagnostics;

namespace VpsPanel.Hosting;

internal sealed class BrowserLauncher(IHostApplicationLifetime lifetime, IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue("VpsPanel:OpenBrowser", true))
            lifetime.ApplicationStarted.Register(() => Open(PanelUrls.Browser));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}