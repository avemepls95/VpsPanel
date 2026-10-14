using VpsPanel.Overview;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;

namespace VpsPanel.Projects;

internal sealed class ProjectService(ISettingsStore settings, ISshConnector connector, OverviewService overview, RunManager runs)
{
    private const string BusyMessage = "An action is already running in this project. Wait for it to finish.";

    public async Task<CommandView> AddComposeCommand(string serverId, string project, CommandRequest request, CancellationToken ct)
    {
        var (name, script) = request.Validate();
        var server = settings.Read().GetServer(serverId);
        ComposeProjectView view;
        await using (var session = await connector.Connect(server, ct))
            view = await overview.GetComposeProject(session, server, project, ct);

        var command = new ProjectCommand { Id = NewId(), Name = name, Script = script };
        settings.Update(s =>
        {
            var stored = s.GetServer(serverId);
            if (!stored.ComposeProjects.TryGetValue(project, out var remembered))
                stored.ComposeProjects[project] = remembered = new ComposeProjectSettings();
            remembered.WorkingDir = view.WorkingDir;
            remembered.ConfigFiles = view.ConfigFiles.ToList();
            remembered.EnvironmentFile = view.EnvironmentFile;
            remembered.Commands.Add(command);
        });
        return CommandView.From(command);
    }

    public CommandView UpdateComposeCommand(string serverId, string project, string commandId, CommandRequest request)
    {
        var (name, script) = request.Validate();
        return settings.Update(s =>
        {
            var command = FindCommand(Compose(s.GetServer(serverId), project).Commands, commandId);
            command.Name = name;
            command.Script = script;
            return CommandView.From(command);
        });
    }

    public void DeleteComposeCommand(string serverId, string project, string commandId) =>
        settings.Update(s =>
        {
            var server = s.GetServer(serverId);
            var remembered = Compose(server, project);
            remembered.Commands.Remove(FindCommand(remembered.Commands, commandId));
            if (remembered.Commands.Count == 0)
                server.ComposeProjects.Remove(project);
        });

    public ManualProjectView AddManualProject(string serverId, ManualProjectRequest request)
    {
        var (name, folder) = request.Validate();
        var project = new ManualProject { Id = NewId(), Name = name, Folder = folder };
        settings.Update(s => s.GetServer(serverId).ManualProjects.Add(project));
        return ManualProjectView.From(project);
    }

    public ManualProjectView UpdateManualProject(string serverId, string projectId, ManualProjectRequest request)
    {
        var (name, folder) = request.Validate();
        return settings.Update(s =>
        {
            var project = Manual(s.GetServer(serverId), projectId);
            project.Name = name;
            project.Folder = folder;
            return ManualProjectView.From(project);
        });
    }

    public void DeleteManualProject(string serverId, string projectId) =>
        settings.Update(s =>
        {
            var server = s.GetServer(serverId);
            server.ManualProjects.Remove(Manual(server, projectId));
        });

    public CommandView AddManualCommand(string serverId, string projectId, CommandRequest request)
    {
        var (name, script) = request.Validate();
        var command = new ProjectCommand { Id = NewId(), Name = name, Script = script };
        settings.Update(s => Manual(s.GetServer(serverId), projectId).Commands.Add(command));
        return CommandView.From(command);
    }

    public CommandView UpdateManualCommand(string serverId, string projectId, string commandId, CommandRequest request)
    {
        var (name, script) = request.Validate();
        return settings.Update(s =>
        {
            var command = FindCommand(Manual(s.GetServer(serverId), projectId).Commands, commandId);
            command.Name = name;
            command.Script = script;
            return CommandView.From(command);
        });
    }

    public void DeleteManualCommand(string serverId, string projectId, string commandId) =>
        settings.Update(s =>
        {
            var project = Manual(s.GetServer(serverId), projectId);
            project.Commands.Remove(FindCommand(project.Commands, commandId));
        });

    public Run RunCommand(string serverId, string commandId)
    {
        var server = settings.Read().GetServer(serverId);
        foreach (var (project, remembered) in server.ComposeProjects)
        {
            if (remembered.Commands.FirstOrDefault(c => c.Id == commandId) is { } command)
                return StartProjectRun(server, $"compose:{project}", $"{command.Name} · {project}",
                    async (session, ct) => (await overview.GetComposeProject(session, server, project, ct)).WorkingDir,
                    command.Script);
        }
        foreach (var manual in server.ManualProjects)
        {
            if (manual.Commands.FirstOrDefault(c => c.Id == commandId) is { } command)
                return StartProjectRun(server, $"manual:{manual.Id}", $"{command.Name} · {manual.Name}",
                    (_, _) => Task.FromResult(manual.Folder), command.Script);
        }
        throw new PanelException(404, "Command not found.");
    }

    public Run UpdateCompose(string serverId, string project)
    {
        var server = settings.Read().GetServer(serverId);
        var projectKey = $"compose:{project}";
        return runs.Start(
            new RunStart(serverId, projectKey, $"Update images · {project}", server.Name, LockKey(serverId, projectKey), BusyMessage),
            async run =>
            {
                await using var session = await connector.Connect(server, run.Token);
                var view = await overview.GetComposeProject(session, server, project, run.Token);
                return await session.ExecStreaming(
                    CommandBuilder.InFolder(view.WorkingDir, CommandBuilder.ComposeUpdate(view)), Append(run), run.Token);
            });
    }

    private Run StartProjectRun(ServerSettings server, string projectKey, string title,
        Func<ISshSession, CancellationToken, Task<string>> folder, string script) =>
        runs.Start(
            new RunStart(server.Id, projectKey, title, server.Name, LockKey(server.Id, projectKey), BusyMessage),
            async run =>
            {
                await using var session = await connector.Connect(server, run.Token);
                var directory = await folder(session, run.Token);
                return await session.ExecStreaming(CommandBuilder.InFolder(directory, script), Append(run), run.Token);
            });

    private static Func<string, Task> Append(Run run) => line =>
    {
        run.Append(line);
        return Task.CompletedTask;
    };

    private static string LockKey(string serverId, string projectKey) => $"{serverId}:{projectKey}";

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static ComposeProjectSettings Compose(ServerSettings server, string project) =>
        server.ComposeProjects.TryGetValue(project, out var remembered)
            ? remembered
            : throw new PanelException(404, "The project has no commands of its own.");

    private static ManualProject Manual(ServerSettings server, string projectId) =>
        server.ManualProjects.FirstOrDefault(p => p.Id == projectId) ?? throw new PanelException(404, "Project not found.");

    private static ProjectCommand FindCommand(List<ProjectCommand> commands, string commandId) =>
        commands.FirstOrDefault(c => c.Id == commandId) ?? throw new PanelException(404, "Command not found.");
}