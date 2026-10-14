using VpsPanel.Settings;

namespace VpsPanel.Overview;

internal static class OverviewBuilder
{
    public static ServerOverview Build(ServerSettings server, RawOverview raw)
    {
        var system = raw.Hostname is null
            ? null
            : new SystemSnapshot(raw.Hostname, raw.Os ?? "", raw.UptimeSeconds ?? 0, raw.Cpus ?? 0,
                raw.Memory?.Total ?? 0, raw.Memory?.Used ?? 0, raw.Disk?.Total ?? 0, raw.Disk?.Used ?? 0);

        var compose = raw.Containers
            .Where(c => c.Project.Length > 0)
            .GroupBy(c => c.Project)
            .Select(g => FromContainers(g.Key, g.ToList(), server))
            .ToList();
        foreach (var (name, remembered) in server.ComposeProjects)
        {
            if (compose.All(p => p.Name != name))
                compose.Add(Stopped(name, remembered));
        }

        var overview = new ServerOverview(
            server.Id,
            ServerStatus.Ok,
            null,
            false,
            system,
            raw.DockerAvailable,
            compose.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            raw.Containers.Where(c => c.Project.Length == 0).Select(ToView).ToList(),
            raw.Timers
                .Where(t => raw.TimerFiles.Contains(t.Unit))
                .Select(t => new TimerView(t.Unit, t.Activates, FromMicroseconds(t.NextMicroseconds),
                    FromMicroseconds(t.LastMicroseconds), raw.ServiceResults.GetValueOrDefault(t.Activates)))
                .OrderBy(t => t.Unit, StringComparer.Ordinal)
                .ToList(),
            server.ManualProjects.Select(ManualProjectView.From).ToList());
        return overview with { Status = StatusIndicator.Compute(overview) };
    }

    public static ComposeProjectView FromContainers(string project, IReadOnlyList<DockerPsLine> containers, ServerSettings server)
    {
        var first = containers[0];
        var commands = server.ComposeProjects.TryGetValue(project, out var remembered)
            ? remembered.Commands.Select(CommandView.From).ToList()
            : [];
        return new ComposeProjectView(
            project,
            first.WorkingDir,
            first.ConfigFiles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            first.EnvironmentFile.Length == 0 ? null : first.EnvironmentFile,
            containers.Count(c => c.State == "running"),
            containers.Count,
            false,
            containers.Select(ToView).ToList(),
            commands);
    }

    public static ComposeProjectView Stopped(string project, ComposeProjectSettings remembered) =>
        new(project, remembered.WorkingDir, remembered.ConfigFiles.ToList(), remembered.EnvironmentFile, 0, 0, true, [],
            remembered.Commands.Select(CommandView.From).ToList());

    private static ContainerView ToView(DockerPsLine line) => new(line.Name, line.Image, line.State, line.Status, line.Ports);

    private static DateTimeOffset? FromMicroseconds(long? microseconds) =>
        microseconds is null or <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(microseconds.Value / 1000);
}