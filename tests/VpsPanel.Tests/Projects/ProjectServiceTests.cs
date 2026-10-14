using NSubstitute;
using VpsPanel.Overview;
using VpsPanel.Projects;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    private const string DemoContainer =
        """{"name":"demo-web-1","image":"nginx","state":"running","status":"Up","ports":"","project":"demo","workingDir":"/srv/demo","configFiles":"/srv/demo/compose.yml","environmentFile":""}""";

    private readonly TempDirectory _dir = new();
    private readonly ISshConnector _connector = Substitute.For<ISshConnector>();
    private readonly ISshSession _session = Substitute.For<ISshSession>();

    public ProjectServiceTests()
    {
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>()).Returns(_session);
        _session.Exec(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ExecResult(0, DemoContainer, ""));
    }

    public void Dispose() => _dir.Dispose();

    private ProjectService CreateService(SettingsStore store) =>
        new(store, _connector, new OverviewService(store, _connector), new RunManager(TimeProvider.System));

    [Fact(DisplayName = "First command of a compose project => project remembered with the folder and files from labels")]
    public async Task AddComposeCommand_Should_RememberProjectLabels_When_FirstCommand()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var store = TestSettings.Store(_dir, server);
        var service = CreateService(store);

        // Act
        var command = await service.AddComposeCommand(server.Id, "demo", new CommandRequest("Rebuild", "make\r\n"), CancellationToken.None);

        // Assert
        store.Read().Servers.Single().ComposeProjects.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["demo"] = new
            {
                WorkingDir = "/srv/demo",
                ConfigFiles = new[] { "/srv/demo/compose.yml" },
                EnvironmentFile = (string?)null,
                Commands = new[] { new { command.Id, Name = "Rebuild", Script = "make\n" } }
            }
        });
    }

    [Fact(DisplayName = "Last command of a compose project deleted => project forgotten")]
    public void DeleteComposeCommand_Should_ForgetProject_When_LastCommandRemoved()
    {
        // Arrange
        var server = TestServers.Create("Main");
        server.ComposeProjects["demo"] = new ComposeProjectSettings
        {
            WorkingDir = "/srv/demo",
            Commands = [new ProjectCommand { Id = "c1", Name = "Rebuild", Script = "make" }]
        };
        var store = TestSettings.Store(_dir, server);
        var service = CreateService(store);

        // Act
        service.DeleteComposeCommand(server.Id, "demo", "c1");

        // Assert
        store.Read().Servers.Single().ComposeProjects.Should().BeEmpty();
    }

    [Fact(DisplayName = "Command changed => new name and script")]
    public void UpdateComposeCommand_Should_ChangeNameAndScript_When_Called()
    {
        // Arrange
        var server = TestServers.Create("Main");
        server.ComposeProjects["demo"] = new ComposeProjectSettings
        {
            WorkingDir = "/srv/demo",
            Commands = [new ProjectCommand { Id = "c1", Name = "Old", Script = "old" }]
        };
        var store = TestSettings.Store(_dir, server);
        var service = CreateService(store);

        // Act
        service.UpdateComposeCommand(server.Id, "demo", "c1", new CommandRequest("New", "new"));

        // Assert
        store.Read().Servers.Single().ComposeProjects["demo"].Commands.Should().BeEquivalentTo(new[]
        {
            new { Id = "c1", Name = "New", Script = "new" }
        });
    }

    [Fact(DisplayName = "Manual project with a path containing .. => folder saved normalized")]
    public void AddManualProject_Should_NormalizeFolder_When_PathHasExtraParts()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var store = TestSettings.Store(_dir, server);
        var service = CreateService(store);

        // Act
        service.AddManualProject(server.Id, new ManualProjectRequest("sync-job", "/root/app/../app/sync-job/"));

        // Assert
        store.Read().Servers.Single().ManualProjects.Should().BeEquivalentTo(new[]
        {
            new { Name = "sync-job", Folder = "/root/app/sync-job", Commands = Array.Empty<object>() }
        });
    }

    [Fact(DisplayName = "Manual project folder is relative => 400")]
    public void AddManualProject_Should_Throw400_When_FolderRelative()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var act = () => service.AddManualProject(server.Id, new ManualProjectRequest("sync-job", "app"));

        // Assert
        act.Should().Throw<PanelException>().WithMessage("The path must start with /: app")
            .Which.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "Command name is empty => 400")]
    public void AddManualCommand_Should_Throw400_When_NameEmpty()
    {
        // Arrange
        var server = TestServers.Create("Main");
        server.ManualProjects.Add(new ManualProject { Id = "p1", Name = "sync-job", Folder = "/srv/sync" });
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var act = () => service.AddManualCommand(server.Id, "p1", new CommandRequest(" ", "make"));

        // Assert
        act.Should().Throw<PanelException>().WithMessage("Enter the command name.")
            .Which.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "Manual project deleted => the project is not in the settings")]
    public void DeleteManualProject_Should_RemoveProject_When_Called()
    {
        // Arrange
        var server = TestServers.Create("Main");
        server.ManualProjects.Add(new ManualProject { Id = "p1", Name = "sync-job", Folder = "/srv/sync" });
        var store = TestSettings.Store(_dir, server);
        var service = CreateService(store);

        // Act
        service.DeleteManualProject(server.Id, "p1");

        // Assert
        store.Read().Servers.Single().ManualProjects.Should().BeEmpty();
    }

    [Fact(DisplayName = "A run is already in progress in the project => 409")]
    public async Task RunCommand_Should_Throw409_When_ProjectBusy()
    {
        // Arrange
        var server = TestServers.Create("Main");
        server.ManualProjects.Add(new ManualProject
        {
            Id = "p1",
            Name = "sync-job",
            Folder = "/srv/sync",
            Commands = [new ProjectCommand { Id = "c1", Name = "Rebuild", Script = "make" }]
        });
        var service = CreateService(TestSettings.Store(_dir, server));
        var gate = new TaskCompletionSource<int?>();
        _session.ExecStreaming(Arg.Any<string>(), Arg.Any<Func<string, Task>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var first = service.RunCommand(server.Id, "c1");

        // Act
        var act = () => service.RunCommand(server.Id, "c1");

        // Assert
        act.Should().Throw<PanelException>()
            .WithMessage("An action is already running in this project. Wait for it to finish.")
            .Which.StatusCode.Should().Be(409);
        gate.SetResult(0);
        await first.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        first.Status.Should().Be(RunStatus.Succeeded);
    }

    [Fact(DisplayName = "Unknown command => 404")]
    public void RunCommand_Should_Throw404_When_CommandUnknown()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var act = () => service.RunCommand(server.Id, "missing");

        // Assert
        act.Should().Throw<PanelException>().WithMessage("Command not found.")
            .Which.StatusCode.Should().Be(404);
    }
}