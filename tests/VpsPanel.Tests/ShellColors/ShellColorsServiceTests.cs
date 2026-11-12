using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.ShellColors;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.ShellColors;

/// <summary>
/// The tests change ~/.bashrc and the login shell of root, so they get a server of their own.
/// </summary>
public sealed class ShellColorsServiceTests(SshServerFixture fixture) : IClassFixture<SshServerFixture>, IAsyncLifetime
{
    private const string OriginalBashrc = "# original\nalias ll='ls -l'\n";

    private readonly TempDirectory _dir = new();

    public Task InitializeAsync() => fixture.Server.Run($"""
        printf '%s' {ShellQuote.Q(OriginalBashrc)} > /root/.bashrc
        sed -i 's#^\(root:.*:\)[^:]*$#\1/bin/bash#' /etc/passwd
        """);

    public Task DisposeAsync()
    {
        _dir.Dispose();
        return Task.CompletedTask;
    }

    private (ShellColorsService Service, ServerSettings Server, SshConnector Connector) Create()
    {
        var server = fixture.Server.Server("Main");
        var store = TestSettings.Store(_dir, server);
        var connector = new SshConnector(store, SsmOptions.Default);
        var backups = new BackupStore(new SettingsPaths(_dir.Path), TimeProvider.System);
        return (new ShellColorsService(store, connector, backups), server, connector);
    }

    private Task UseShell(string shell) =>
        fixture.Server.Run($"sed -i 's#^\\(root:.*:\\)[^:]*$#\\1{shell}#' /etc/passwd");

    private string[] Backups() =>
        Directory.Exists(Path.Combine(_dir.Path, "backups"))
            ? Directory.GetFiles(Path.Combine(_dir.Path, "backups"), "*", SearchOption.AllDirectories)
            : [];

    [Fact(DisplayName = "No panel block in .bashrc => off")]
    public async Task Get_Should_ReturnOff_When_BlockMissing()
    {
        // Arrange
        var (service, server, _) = Create();

        // Act
        var result = await service.Get(server.Id, CancellationToken.None);

        // Assert
        result.State.Should().Be(ShellColorsState.Off);
    }

    [Fact(DisplayName = "Login shell is not bash => unavailable")]
    public async Task Get_Should_ReturnUnavailable_When_LoginShellNotBash()
    {
        // Arrange
        var (service, server, _) = Create();
        await UseShell("/bin/sh");

        // Act
        var result = await service.Get(server.Id, CancellationToken.None);

        // Assert
        result.State.Should().Be(ShellColorsState.Unavailable);
    }

    [Fact(DisplayName = "Colors off => block appended, old .bashrc in the backup")]
    public async Task Enable_Should_AppendBlock_When_ColorsOff()
    {
        // Arrange
        var (service, server, _) = Create();

        // Act
        var result = await service.Enable(server.Id, CancellationToken.None);

        // Assert
        result.State.Should().Be(ShellColorsState.On);
        var bashrc = await fixture.Server.Run("cat /root/.bashrc");
        bashrc.Should().StartWith(OriginalBashrc + ShellColorsService.BlockStart + "\n").And.EndWith(ShellColorsService.BlockEnd + "\n");
        Backups().Should().ContainSingle().Which.Should().Match(path => File.ReadAllText(path) == OriginalBashrc);
    }

    [Fact(DisplayName = ".bashrc without a final line break => the block starts on a new line")]
    public async Task Enable_Should_StartBlockOnNewLine_When_BashrcLacksFinalLineBreak()
    {
        // Arrange
        var (service, server, _) = Create();
        await fixture.Server.Run("printf 'alias ll=1' > /root/.bashrc");

        // Act
        await service.Enable(server.Id, CancellationToken.None);

        // Assert
        (await fixture.Server.Run("cat /root/.bashrc")).Should().StartWith("alias ll=1\n" + ShellColorsService.BlockStart + "\n");
    }

    [Fact(DisplayName = "Login shell is not bash => 409, .bashrc untouched")]
    public async Task Enable_Should_Throw409_When_LoginShellNotBash()
    {
        // Arrange
        var (service, server, _) = Create();
        await UseShell("/bin/sh");

        // Act
        var act = () => service.Enable(server.Id, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("Colors can be turned on only when the login shell on the server is bash.")
            .Which.StatusCode.Should().Be(409);
        (await fixture.Server.Run("cat /root/.bashrc")).Should().Be(OriginalBashrc);
    }

    [Fact(DisplayName = "Colors on => .bashrc as before")]
    public async Task Disable_Should_RestoreBashrc_When_ColorsOn()
    {
        // Arrange
        var (service, server, _) = Create();
        await service.Enable(server.Id, CancellationToken.None);

        // Act
        var result = await service.Disable(server.Id, CancellationToken.None);

        // Assert
        result.State.Should().Be(ShellColorsState.Off);
        (await fixture.Server.Run("cat /root/.bashrc")).Should().Be(OriginalBashrc);
    }

    [Fact(DisplayName = "Colors on => panel commands get the same output")]
    public async Task Exec_Should_ReturnPlainOutput_When_ColorsOn()
    {
        // Arrange
        var (service, server, connector) = Create();
        await service.Enable(server.Id, CancellationToken.None);
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var result = await session.Exec("echo ok", CancellationToken.None);

        // Assert
        result.Should().Be(new ExecResult(0, "ok\n", ""));
    }

    [Fact(DisplayName = "Colors turned on from the page => the terminal shell runs the panel block")]
    public async Task Terminal_Should_RunPanelBlock_When_ColorsEnabledThroughApi()
    {
        // Arrange
        using var app = new TestApp();
        var server = fixture.Server.Server("Main");
        app.Settings.Update(s => s.Servers.Add(server));
        using var client = app.CreatePanelClient();
        (await client.PostAsync($"/api/servers/{server.Id}/terminal/colors/enable", null)).EnsureSuccessStatusCode();
        (await client.GetStringAsync($"/api/servers/{server.Id}/terminal/colors")).Should().Be("""{"state":"on"}""");
        using var terminal = await TerminalClient.Connect(app, server.Id);
        await terminal.WaitReady();

        // Act
        await terminal.Type("alias ls\n");

        // Assert
        (await terminal.ReadUntil("ls --color=auto")).Success.Should().BeTrue();
    }
}