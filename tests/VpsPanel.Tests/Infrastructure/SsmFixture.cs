namespace VpsPanel.Tests.Infrastructure;

public sealed class SsmFixture : IAsyncLifetime
{
    internal SshServerContainer Server { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Server = await SshServerContainer.Start(dockerStub: false);
        var target = Server.Server();
        Environment.SetEnvironmentVariable("FAKE_AWS_TARGET", $"{target.Host}:{target.Port}");
        Environment.SetEnvironmentVariable("FAKE_AWS_PID_FILE", FakeAws.PidFile);
        Environment.SetEnvironmentVariable("FAKE_AWS_TERMINATED_FILE", FakeAws.TerminatedFile);
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        File.Delete(FakeAws.PidFile);
        File.Delete(FakeAws.TerminatedFile);
    }
}

[CollectionDefinition("ssm")]
public sealed class SsmCollection : ICollectionFixture<SsmFixture>;