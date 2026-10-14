using NSubstitute;
using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Files;

public sealed class FileGuardTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory(DisplayName = "Root deletion => 400 without connecting to the server")]
    [InlineData("/", "")]
    [InlineData("//", null)]
    [InlineData("/root/..", "")]
    public async Task Delete_Should_Throw400_When_PathIsRoot(string path, string? confirmName)
    {
        // Arrange
        var store = TestSettings.Store(_dir, TestServers.Create("Main"));
        var connector = Substitute.For<ISshConnector>();
        var service = new FileService(store, connector, new BackupStore(new SettingsPaths(_dir.Path), TimeProvider.System));

        // Act
        var act = () => service.Delete("any", path, confirmName, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The server root cannot be deleted.")
            .Which.StatusCode.Should().Be(400);
        await connector.DidNotReceiveWithAnyArgs().Connect(default!, default);
    }
}