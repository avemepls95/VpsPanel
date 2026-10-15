using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Ssh;

[Collection("ssh")]
public sealed class HostKeyTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "First connection => key fingerprint is saved in the settings")]
    public async Task Connect_Should_SaveFingerprint_When_FirstConnection()
    {
        // Arrange
        var server = fixture.Server.Server();
        var store = TestSettings.Store(_dir, server);
        var connector = new SshConnector(store, SsmOptions.Default);

        // Act
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Assert
        store.Read().Servers.Single().HostKeyFingerprint.Should().MatchRegex("^SHA256:[A-Za-z0-9+/]{43}$");
    }

    [Fact(DisplayName = "Key fingerprint changed => key mismatch error")]
    public async Task Connect_Should_Throw_When_FingerprintChanged()
    {
        // Arrange
        var server = fixture.Server.Server("Main");
        server.HostKeyFingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);

        // Act
        var act = () => connector.Connect(server, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HostKeyMismatchException>())
            .WithMessage("The key for server 'Main' has changed, new fingerprint SHA256:*. If the server was reinstalled, reset the fingerprint in the server settings.")
            .Which.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "Port is closed => SSH unavailable error")]
    public async Task Connect_Should_Throw_When_PortClosed()
    {
        // Arrange
        var server = fixture.Server.Server();
        server.Host = "127.0.0.1";
        server.Port = TestNetwork.FreeTcpPort();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);

        // Act
        var act = () => connector.Connect(server, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<SshUnavailableException>())
            .WithMessage("Could not connect over SSH: *")
            .Which.StatusCode.Should().Be(502);
    }

    [Fact(DisplayName = "No key file => error with the key path")]
    public async Task Connect_Should_Throw_When_KeyFileMissing()
    {
        // Arrange
        var server = fixture.Server.Server();
        server.KeyPath = Path.Combine(_dir.Path, "missing.pem");
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);

        // Act
        var act = () => connector.Connect(server, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage($"Key file not found: {server.KeyPath}")
            .Which.StatusCode.Should().Be(400);
    }
}