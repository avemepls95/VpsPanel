using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;

namespace VpsPanel.Reboot;

internal sealed class RebootService(ISettingsStore settings, ISshConnector connector, RunManager runs, RebootTiming timing)
{
    private const string BootIdCommand = "cat /proc/sys/kernel/random/boot_id";

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public Run Start(string serverId)
    {
        var server = settings.Read().GetServer(serverId);
        return runs.Start(
            new RunStart(serverId, null, "Reboot", server.Name, $"{serverId}:reboot",
                "This server is already rebooting."),
            run => Execute(server, run));
    }

    private async Task<int?> Execute(ServerSettings server, Run run)
    {
        string bootId;
        await using (var session = await connector.Connect(server, run.Token))
        {
            bootId = (await session.Exec(BootIdCommand, run.Token)).Stdout.Trim();
            run.Append("Sending the reboot command");
            try
            {
                await session.Exec("systemctl reboot", run.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The server drops the connection when it starts rebooting.
            }
        }

        run.Append("Waiting for the server to respond again");

        // Connect and Exec can hang, so the whole wait is limited by a single deadline.
        using var limit = new CancellationTokenSource(timing.PollInterval * timing.MaxAttempts, Time);
        using var polling = CancellationTokenSource.CreateLinkedTokenSource(run.Token, limit.Token);
        for (var attempt = 0; attempt < timing.MaxAttempts && !limit.IsCancellationRequested; attempt++)
        {
            try
            {
                await Task.Delay(timing.PollInterval, Time, polling.Token);
                await using var session = await connector.Connect(server, polling.Token, timing.ConnectTimeout);
                var current = (await session.Exec(BootIdCommand, polling.Token)).Stdout.Trim();
                if (current.Length > 0 && current != bootId)
                {
                    run.Append("The server is responding again");
                    return 0;
                }
            }
            catch (Exception e) when (e is not HostKeyMismatchException && !run.StopRequested)
            {
                // The server is still booting or the connection dropped during the reboot.
            }
        }

        run.Append("The server did not respond after the reboot");
        return 1;
    }
}