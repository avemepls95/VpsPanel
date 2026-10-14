using VpsPanel.Settings;
using VpsPanel.Ssh;

namespace VpsPanel.Overview;

internal sealed class OverviewService(ISettingsStore settings, ISshConnector connector)
{
    public async Task<ServerOverview> Get(string serverId, CancellationToken ct)
    {
        var server = settings.Read().GetServer(serverId);
        try
        {
            await using var session = await connector.Connect(server, ct);
            var result = await session.Exec(OverviewScript.Command, ct);
            var overview = OverviewBuilder.Build(server, OverviewOutputParser.Parse(result.Stdout));
            RememberComposeLabels(serverId, overview);
            return overview;
        }
        catch (HostKeyMismatchException e)
        {
            return ServerOverview.Failed(serverId, e.Message, hostKeyMismatch: true);
        }
        catch (PanelException e)
        {
            return ServerOverview.Failed(serverId, e.Message, hostKeyMismatch: false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ServerOverview.Failed(serverId, $"Could not get server data: {e.Message}", hostKeyMismatch: false);
        }
    }

    public async Task<ComposeProjectView> GetComposeProject(ISshSession session, ServerSettings server, string project, CancellationToken ct)
    {
        var filter = ShellQuote.Q("label=com.docker.compose.project=" + project);
        var result = await session.Exec($"docker ps -a --filter {filter} --format {ShellQuote.Q(OverviewScript.DockerFormat)}", ct);
        var containers = OverviewOutputParser.ParseDockerLines(result.Stdout).Where(c => c.Project == project).ToList();
        if (containers.Count > 0)
            return OverviewBuilder.FromContainers(project, containers, server);
        if (server.ComposeProjects.TryGetValue(project, out var remembered))
            return OverviewBuilder.Stopped(project, remembered);
        throw new PanelException(404, $"Compose project {project} was not found on the server.");
    }

    private void RememberComposeLabels(string serverId, ServerOverview overview)
    {
        var running = overview.ComposeProjects.Where(p => !p.Stopped).ToDictionary(p => p.Name);
        var stored = settings.Read().Servers.FirstOrDefault(s => s.Id == serverId);
        if (stored is null || !stored.ComposeProjects.Any(p => running.TryGetValue(p.Key, out var view) && Differs(p.Value, view)))
            return;

        settings.Update(s =>
        {
            var server = s.Servers.FirstOrDefault(x => x.Id == serverId);
            if (server is null)
                return;
            foreach (var (name, remembered) in server.ComposeProjects)
            {
                if (!running.TryGetValue(name, out var view))
                    continue;
                remembered.WorkingDir = view.WorkingDir;
                remembered.ConfigFiles = view.ConfigFiles.ToList();
                remembered.EnvironmentFile = view.EnvironmentFile;
            }
        });
    }

    private static bool Differs(ComposeProjectSettings remembered, ComposeProjectView view) =>
        remembered.WorkingDir != view.WorkingDir
        || remembered.EnvironmentFile != view.EnvironmentFile
        || !remembered.ConfigFiles.SequenceEqual(view.ConfigFiles);
}