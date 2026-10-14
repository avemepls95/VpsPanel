using VpsPanel.Overview;
using VpsPanel.Projects;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Projects;

[Collection("ssh")]
public sealed class ProjectRunsTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static ProjectService CreateService(SettingsStore store)
    {
        var connector = new SshConnector(store);
        return new ProjectService(store, connector, new OverviewService(store, connector), new RunManager(TimeProvider.System));
    }

    private static IEnumerable<string> Lines(Run run) => run.ReadAfter(0).Lines.Select(l => l.Text);

    [Fact(DisplayName = "Manual project command with quotes => runs in the project folder with a space")]
    public async Task RunCommand_Should_RunScriptInFolder_When_ManualProject()
    {
        // Arrange
        await fixture.Server.Run("mkdir -p '/tmp/my app'");
        var server = fixture.Server.Server();
        server.ManualProjects.Add(new ManualProject
        {
            Id = "p1",
            Name = "app",
            Folder = "/tmp/my app",
            Commands = [new ProjectCommand { Id = "c1", Name = "Check", Script = "echo \"it's $((1+1))\"\npwd" }]
        });
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var run = service.RunCommand(server.Id, "c1");

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        Lines(run).Should().Equal("it's 2", "/tmp/my app");
    }

    [Fact(DisplayName = "Project folder is missing => run fails, the script is not executed")]
    public async Task RunCommand_Should_FailWithoutScript_When_FolderMissing()
    {
        // Arrange
        await fixture.Server.Run("rm -f /tmp/should-not-exist");
        var server = fixture.Server.Server();
        server.ManualProjects.Add(new ManualProject
        {
            Id = "p1",
            Name = "app",
            Folder = "/tmp/missing-folder",
            Commands = [new ProjectCommand { Id = "c1", Name = "Create", Script = "touch /tmp/should-not-exist" }]
        });
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var run = service.RunCommand(server.Id, "c1");

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Failed);
        run.ExitCode.Should().Be(1);
        await fixture.Server.Run("test ! -e /tmp/should-not-exist");
    }

    [Fact(DisplayName = "Compose project update => pull and up -d with parameters from labels")]
    public async Task UpdateCompose_Should_RunPullAndUp_When_ProjectRuns()
    {
        // Arrange
        var server = fixture.Server.Server();
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var run = service.UpdateCompose(server.Id, "demo");

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        Lines(run).Should().Equal(
            "docker compose -p demo -f /srv/demo/compose.yml pull",
            "docker compose -p demo -f /srv/demo/compose.yml up -d");
    }

    [Fact(DisplayName = "Long command is stopped => run finishes with status stopped")]
    public async Task Stop_Should_EndRun_When_CommandRunsLong()
    {
        // Arrange
        var server = fixture.Server.Server();
        server.ManualProjects.Add(new ManualProject
        {
            Id = "p1",
            Name = "app",
            Folder = "/tmp",
            Commands = [new ProjectCommand { Id = "c1", Name = "Long", Script = "while true; do echo tick; sleep 1; done" }]
        });
        var service = CreateService(TestSettings.Store(_dir, server));
        var run = service.RunCommand(server.Id, "c1");
        while (run.LastLineId == 0)
            await run.ReadAfter(0).Changed.WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        run.Stop();

        // Assert
        await run.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        run.Status.Should().Be(RunStatus.Stopped);
    }
}