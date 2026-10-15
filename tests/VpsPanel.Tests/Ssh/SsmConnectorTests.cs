using System.Text;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Ssh;

[Collection("ssm")]
public sealed class SsmConnectorTests(SsmFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "Server behind SSM => the command runs over the tunnel")]
    public async Task Exec_Should_RunCommand_When_ServerUsesSsm()
    {
        // Arrange
        FakeAws.UseMode("ok");
        var server = fixture.Server.SsmServer();
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options());
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var result = await session.Exec("echo out; exit 3", CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new ExecResult(3, "out\n", ""));
    }

    [Fact(DisplayName = "Command prints nothing longer than the SSM idle timeout => the connection stays open")]
    public async Task Exec_Should_KeepConnection_When_CommandIsSilentLongerThanIdleTimeout()
    {
        // Arrange
        FakeAws.UseMode("ok", idleTimeout: TimeSpan.FromSeconds(2));
        var server = fixture.Server.SsmServer();
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options(keepAliveInterval: TimeSpan.FromMilliseconds(500)));
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var result = await session.Exec("sleep 4; echo done", CancellationToken.None);

        // Assert
        result.Stdout.Should().Be("done\n");
    }

    [Fact(DisplayName = "Server behind SSM => a file is read over SFTP through the same tunnel")]
    public async Task OpenRead_Should_ReturnFileBytes_When_ServerUsesSsm()
    {
        // Arrange
        FakeAws.UseMode("ok");
        await fixture.Server.Run("printf 'over ssm' > /tmp/ssm-read-test");
        var server = fixture.Server.SsmServer();
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options());
        await using var session = await connector.Connect(server, CancellationToken.None);
        using var copy = new MemoryStream();

        // Act
        await using (var stream = await session.OpenRead("/tmp/ssm-read-test", CancellationToken.None))
            await stream.CopyToAsync(copy);

        // Assert
        Encoding.UTF8.GetString(copy.ToArray()).Should().Be("over ssm");
    }

    [Fact(DisplayName = "Previous session closed => the next connection starts its own tunnel")]
    public async Task Connect_Should_StartNewTunnel_When_PreviousSessionClosed()
    {
        // Arrange
        FakeAws.UseMode("ok");
        var server = fixture.Server.SsmServer();
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options());
        await (await connector.Connect(server, CancellationToken.None)).DisposeAsync();
        var firstPid = FakeAws.LastPid();

        // Act
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Assert
        FakeAws.IsRunning(firstPid).Should().BeFalse();
        FakeAws.LastPid().Should().NotBe(firstPid);
        (await session.Exec("echo ok", CancellationToken.None)).Stdout.Should().Be("ok\n");
    }

    [Fact(DisplayName = "Session closed => aws is stopped")]
    public async Task DisposeAsync_Should_StopAws_When_ServerUsesSsm()
    {
        // Arrange
        FakeAws.UseMode("ok");
        var server = fixture.Server.SsmServer();
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options());
        var session = await connector.Connect(server, CancellationToken.None);
        var pid = FakeAws.LastPid();

        // Act
        await session.DisposeAsync();

        // Assert
        FakeAws.IsRunning(pid).Should().BeFalse();
    }

    [Fact(DisplayName = "Key fingerprint changed behind SSM => key mismatch error, aws is stopped")]
    public async Task Connect_Should_StopAws_When_SshFailsAfterTunnelStarted()
    {
        // Arrange
        FakeAws.UseMode("ok");
        var server = fixture.Server.SsmServer("Aws");
        server.HostKeyFingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options());

        // Act
        var act = () => connector.Connect(server, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HostKeyMismatchException>()
            .WithMessage("The key for server 'Aws' has changed, new fingerprint SHA256:*. If the server was reinstalled, reset the fingerprint in the server settings.");
        FakeAws.IsRunning(FakeAws.LastPid()).Should().BeFalse();
    }

    [Fact(DisplayName = "No key file behind SSM => key error, aws is not started")]
    public async Task Connect_Should_NotStartAws_When_KeyFileMissing()
    {
        // Arrange
        FakeAws.UseMode("ok");
        File.Delete(FakeAws.PidFile);
        var server = fixture.Server.SsmServer();
        server.KeyPath = Path.Combine(_dir.Path, "missing.pem");
        var connector = new SshConnector(TestSettings.Store(_dir, server), FakeAws.Options());

        // Act
        var act = () => connector.Connect(server, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PanelException>().WithMessage($"Key file not found: {server.KeyPath}");
        File.Exists(FakeAws.PidFile).Should().BeFalse();
    }
}