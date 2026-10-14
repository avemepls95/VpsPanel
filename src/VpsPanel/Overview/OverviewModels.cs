using VpsPanel.Settings;

namespace VpsPanel.Overview;

internal enum ServerStatus
{
    Ok,
    Warning,
    Error
}

internal sealed record ServerOverview(
    string ServerId,
    ServerStatus Status,
    string? Error,
    bool HostKeyMismatch,
    SystemSnapshot? System,
    bool DockerAvailable,
    IReadOnlyList<ComposeProjectView> ComposeProjects,
    IReadOnlyList<ContainerView> StandaloneContainers,
    IReadOnlyList<TimerView> Timers,
    IReadOnlyList<ManualProjectView> ManualProjects)
{
    public static ServerOverview Failed(string serverId, string error, bool hostKeyMismatch) =>
        new(serverId, ServerStatus.Error, error, hostKeyMismatch, null, false, [], [], [], []);
}

internal sealed record SystemSnapshot(
    string Hostname,
    string Os,
    double UptimeSeconds,
    int Cpus,
    long MemTotal,
    long MemUsed,
    long DiskTotal,
    long DiskUsed);

internal sealed record ContainerView(string Name, string Image, string State, string Status, string Ports);

internal sealed record ComposeProjectView(
    string Name,
    string WorkingDir,
    IReadOnlyList<string> ConfigFiles,
    string? EnvironmentFile,
    int Running,
    int Total,
    bool Stopped,
    IReadOnlyList<ContainerView> Containers,
    IReadOnlyList<CommandView> Commands);

internal sealed record TimerView(string Unit, string Service, DateTimeOffset? Next, DateTimeOffset? Last, string? Result);

internal sealed record ManualProjectView(string Id, string Name, string Folder, IReadOnlyList<CommandView> Commands)
{
    public static ManualProjectView From(ManualProject project) =>
        new(project.Id, project.Name, project.Folder, project.Commands.Select(CommandView.From).ToList());
}

internal sealed record CommandView(string Id, string Name, string Script)
{
    public static CommandView From(ProjectCommand command) => new(command.Id, command.Name, command.Script);
}

internal sealed class DockerPsLine
{
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string State { get; set; } = "";
    public string Status { get; set; } = "";
    public string Ports { get; set; } = "";
    public string Project { get; set; } = "";
    public string WorkingDir { get; set; } = "";
    public string ConfigFiles { get; set; } = "";
    public string EnvironmentFile { get; set; } = "";
}

internal sealed record TimerLine(string Unit, string Activates, long? NextMicroseconds, long? LastMicroseconds);

internal sealed record RawOverview(
    string? Hostname,
    string? Os,
    double? UptimeSeconds,
    int? Cpus,
    (long Total, long Used)? Memory,
    (long Total, long Used)? Disk,
    bool DockerAvailable,
    IReadOnlyList<DockerPsLine> Containers,
    IReadOnlyList<TimerLine> Timers,
    IReadOnlySet<string> TimerFiles,
    IReadOnlyDictionary<string, string> ServiceResults);