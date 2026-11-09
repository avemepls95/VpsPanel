using VpsPanel.Projects;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;

namespace VpsPanel.Containers;

internal sealed class ContainerService(ISettingsStore settings, ISshConnector connector, RunManager runs)
{
    public Run Run(string serverId, string name, DockerAction action)
    {
        var server = settings.Read().GetServer(serverId);
        var key = $"container:{name}";
        return runs.Start(
            new RunStart(serverId, key, $"{action} · {name}", server.Name, $"{serverId}:{key}",
                "An action is already running for this container. Wait for it to finish."),
            async run =>
            {
                await using var session = await connector.Connect(server, run.Token);
                return await session.ExecStreaming(CommandBuilder.ContainerAction(name, action), line =>
                {
                    run.Append(line);
                    return Task.CompletedTask;
                }, run.Token);
            });
    }
}