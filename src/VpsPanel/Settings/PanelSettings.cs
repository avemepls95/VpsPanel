namespace VpsPanel.Settings;

internal sealed class PanelSettings
{
    public List<ServerSettings> Servers { get; set; } = [];
}

internal sealed class ServerSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "root";
    public string KeyPath { get; set; } = "";
    public string? HostKeyFingerprint { get; set; }
    public ProviderInfo? Provider { get; set; }
    public Dictionary<string, ComposeProjectSettings> ComposeProjects { get; set; } = new();
    public List<ManualProject> ManualProjects { get; set; } = [];
}

internal sealed record ProviderInfo(string Name, bool IsManual);

/// <summary>
/// A compose project with its own commands. The folder and files are needed to manage the project after docker compose down.
/// </summary>
internal sealed class ComposeProjectSettings
{
    public string WorkingDir { get; set; } = "";
    public List<string> ConfigFiles { get; set; } = [];
    public string? EnvironmentFile { get; set; }
    public List<ProjectCommand> Commands { get; set; } = [];
}

internal sealed class ManualProject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<ProjectCommand> Commands { get; set; } = [];
}

internal sealed class ProjectCommand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Script { get; set; } = "";
}