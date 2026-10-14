namespace VpsPanel.Tests.Infrastructure;

public sealed class TwoSshServersFixture : IAsyncLifetime
{
    internal SshServerContainer First { get; private set; } = null!;

    internal SshServerContainer Second { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        First = await SshServerContainer.Start(dockerStub: false);
        Second = await SshServerContainer.Start(dockerStub: false);
    }

    public async Task DisposeAsync()
    {
        await First.DisposeAsync();
        await Second.DisposeAsync();
    }
}

[CollectionDefinition("two-ssh")]
public sealed class TwoSshCollection : ICollectionFixture<TwoSshServersFixture>;