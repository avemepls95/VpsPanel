using NSubstitute;
using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Files;

public sealed class FileSaveFailureTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "Write to the file is interrupted after the backup => 500 with the backup path")]
    public async Task Save_Should_Throw500WithBackupPath_When_WriteFails()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var store = TestSettings.Store(_dir, server);
        var paths = new SettingsPaths(_dir.Path);
        var session = Substitute.For<ISshSession>();
        session.OpenRead("/tmp/save.env", Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream>(new MemoryStream("old"u8.ToArray())));
        var target = Substitute.For<Stream>();
        target.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new IOException("broken")));
        session.OpenWrite("/tmp/save.env", false, Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));
        var connector = Substitute.For<ISshConnector>();
        connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>()).Returns(Task.FromResult(session));
        var service = new FileService(store, connector, new BackupStore(paths, TimeProvider.System));
        var request = new SaveFileRequest("/tmp/save.env", "new text", TextFileCodec.Sha256("old"u8.ToArray()), false, false);

        // Act
        var act = () => service.Save(server.Id, request, CancellationToken.None);

        // Assert
        var error = (await act.Should().ThrowAsync<PanelException>()).Which;
        var backup = Directory.EnumerateFiles(paths.BackupsDir, "*", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        error.Message.Should().Be($"The write was interrupted, file /tmp/save.env may be incomplete. The previous version is saved to {backup}");
        error.StatusCode.Should().Be(500);
    }
}