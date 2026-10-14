namespace VpsPanel.Tests.Infrastructure;

public sealed class SshServerFixture : IAsyncLifetime
{
    internal SshServerContainer Server { get; private set; } = null!;

    public async Task InitializeAsync() => Server = await SshServerContainer.Start(dockerStub: true);

    public async Task DisposeAsync() => await Server.DisposeAsync();
}

[CollectionDefinition("ssh")]
public sealed class SshCollection : ICollectionFixture<SshServerFixture>;