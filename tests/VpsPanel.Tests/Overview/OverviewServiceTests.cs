using NSubstitute;
using VpsPanel.Overview;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Overview;

[Collection("ssh")]
public sealed class OverviewServiceTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "Server with Docker => compose project and a container without compose, yellow indicator")]
    public async Task Get_Should_ReturnContainers_When_DockerPresent()
    {
        // Arrange
        var server = fixture.Server.Server();
        var store = TestSettings.Store(_dir, server);
        var service = new OverviewService(store, new SshConnector(store));

        // Act
        var overview = await service.Get(server.Id, CancellationToken.None);

        // Assert
        overview.Should().BeEquivalentTo(new
        {
            Status = ServerStatus.Warning,
            Error = (string?)null,
            DockerAvailable = true,
            ComposeProjects = new[] { new { Name = "demo", WorkingDir = "/srv/demo", Running = 2, Total = 3, Stopped = false } },
            StandaloneContainers = new[] { new { Name = "vpn", State = "running" } }
        });
    }

    [Fact(DisplayName = "Server without Docker and systemd => empty blocks, green indicator, system snapshot present")]
    public async Task Get_Should_ReturnEmptyBlocks_When_NoDockerAndSystemd()
    {
        // Arrange
        await using var bare = await SshServerContainer.Start(dockerStub: false);
        var server = bare.Server();
        var store = TestSettings.Store(_dir, server);
        var service = new OverviewService(store, new SshConnector(store));

        // Act
        var overview = await service.Get(server.Id, CancellationToken.None);

        // Assert
        overview.Should().BeEquivalentTo(new
        {
            Status = ServerStatus.Ok,
            Error = (string?)null,
            DockerAvailable = false,
            ComposeProjects = Array.Empty<object>(),
            StandaloneContainers = Array.Empty<object>(),
            Timers = Array.Empty<object>()
        });
        overview.System!.Hostname.Should().NotBeEmpty();
        overview.System.MemTotal.Should().BePositive();
    }

    [Fact(DisplayName = "Server unavailable => overview with a connection error and a red indicator")]
    public async Task Get_Should_ReturnFailedOverview_When_ServerUnreachable()
    {
        // Arrange
        var server = fixture.Server.Server();
        server.Host = "127.0.0.1";
        server.Port = TestNetwork.FreeTcpPort();
        var store = TestSettings.Store(_dir, server);
        var service = new OverviewService(store, new SshConnector(store));

        // Act
        var overview = await service.Get(server.Id, CancellationToken.None);

        // Assert
        overview.Status.Should().Be(ServerStatus.Error);
        overview.HostKeyMismatch.Should().BeFalse();
        overview.Error.Should().StartWith("Could not connect over SSH: ");
    }

    [Fact(DisplayName = "Connection dropped while running a command => overview with an error and a red indicator")]
    public async Task Get_Should_ReturnFailedOverview_When_ExecFailsAfterConnect()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var store = TestSettings.Store(_dir, server);
        var session = Substitute.For<ISshSession>();
        session.Exec(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ExecResult>(new IOException("connection reset")));
        var connector = Substitute.For<ISshConnector>();
        connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>()).Returns(session);
        var service = new OverviewService(store, connector);

        // Act
        var overview = await service.Get(server.Id, CancellationToken.None);

        // Assert
        overview.Status.Should().Be(ServerStatus.Error);
        overview.HostKeyMismatch.Should().BeFalse();
        overview.Error.Should().Be("Could not get server data: connection reset");
    }

    [Fact(DisplayName = "Key fingerprint does not match => overview with the key mismatch flag")]
    public async Task Get_Should_FlagHostKeyMismatch_When_FingerprintDiffers()
    {
        // Arrange
        var server = fixture.Server.Server("Main");
        server.HostKeyFingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var store = TestSettings.Store(_dir, server);
        var service = new OverviewService(store, new SshConnector(store));

        // Act
        var overview = await service.Get(server.Id, CancellationToken.None);

        // Assert
        overview.Status.Should().Be(ServerStatus.Error);
        overview.HostKeyMismatch.Should().BeTrue();
        overview.Error.Should().StartWith("The key for server 'Main' has changed");
    }

    [Fact(DisplayName = "Remembered project is run with a new folder => folder in the settings updated")]
    public async Task Get_Should_UpdateRememberedLabels_When_ProjectRuns()
    {
        // Arrange
        var server = fixture.Server.Server();
        server.ComposeProjects["demo"] = new ComposeProjectSettings
        {
            WorkingDir = "/old/demo",
            ConfigFiles = ["/old/demo/compose.yml"],
            Commands = [new ProjectCommand { Id = "c1", Name = "Rebuild", Script = "make" }]
        };
        var store = TestSettings.Store(_dir, server);
        var service = new OverviewService(store, new SshConnector(store));

        // Act
        await service.Get(server.Id, CancellationToken.None);

        // Assert
        store.Read().Servers.Single().ComposeProjects["demo"].Should().BeEquivalentTo(new
        {
            WorkingDir = "/srv/demo",
            ConfigFiles = new[] { "/srv/demo/compose.yml" },
            EnvironmentFile = (string?)null
        });
    }

    [Fact(DisplayName = "Compose project requested by name => project from container labels")]
    public async Task GetComposeProject_Should_ReadLabels_When_ProjectRuns()
    {
        // Arrange
        var server = fixture.Server.Server();
        var store = TestSettings.Store(_dir, server);
        var connector = new SshConnector(store);
        var service = new OverviewService(store, connector);
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var project = await service.GetComposeProject(session, server, "demo", CancellationToken.None);

        // Assert
        project.Should().BeEquivalentTo(new { Name = "demo", WorkingDir = "/srv/demo", ConfigFiles = new[] { "/srv/demo/compose.yml" } });
    }

    [Fact(DisplayName = "Compose project is neither on the server nor in the settings => 404")]
    public async Task GetComposeProject_Should_Throw404_When_ProjectUnknown()
    {
        // Arrange
        var server = fixture.Server.Server();
        var store = TestSettings.Store(_dir, server);
        var connector = new SshConnector(store);
        var service = new OverviewService(store, connector);
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var act = () => service.GetComposeProject(session, server, "missing", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("Compose project missing was not found on the server.")
            .Which.StatusCode.Should().Be(404);
    }
}