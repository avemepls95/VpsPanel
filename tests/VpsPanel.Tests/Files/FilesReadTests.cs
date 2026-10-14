using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Files;

[Collection("ssh")]
public sealed class FilesReadTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private (FileService Service, ServerSettings Server) Create()
    {
        var server = fixture.Server.Server();
        var store = TestSettings.Store(_dir, server);
        var backups = new BackupStore(new SettingsPaths(_dir.Path), TimeProvider.System);
        return (new FileService(store, new SshConnector(store), backups), server);
    }

    [Fact(DisplayName = "Folder with a file, subfolder, link and FIFO => all objects with their types")]
    public async Task List_Should_ReturnTypedEntries_When_FolderHasSpecialObjects()
    {
        // Arrange
        await fixture.Server.Run("""
            rm -rf /tmp/list-test && mkdir -p /tmp/list-test/sub && cd /tmp/list-test
            printf data > "it's -file \$x"
            printf x > $'a\nb'
            ln -s sub to-sub
            mkfifo pipe
            """);
        var (service, server) = Create();

        // Act
        var listing = await service.List(server.Id, "/tmp/list-test/", CancellationToken.None);

        // Assert
        listing.Path.Should().Be("/tmp/list-test");
        listing.Entries.Select(e => (e.Name, e.Type, e.TargetType)).Should().Equal(
            ("sub", FileEntryType.Dir, (FileEntryType?)null),
            ("to-sub", FileEntryType.Link, FileEntryType.Dir),
            ("a\nb", FileEntryType.File, null),
            ("it's -file $x", FileEntryType.File, null),
            ("pipe", FileEntryType.Other, null));
    }

    [Fact(DisplayName = "Path of a link to a folder => contents of the folder the link points to")]
    public async Task List_Should_FollowStartingLink_When_PathIsLinkToDir()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/link-test && mkdir -p /tmp/link-test/real && touch /tmp/link-test/real/inside && ln -s real /tmp/link-test/alias");
        var (service, server) = Create();

        // Act
        var listing = await service.List(server.Id, "/tmp/link-test/alias", CancellationToken.None);

        // Assert
        listing.Entries.Select(e => e.Name).Should().Equal("inside");
    }

    [Fact(DisplayName = "No folder => 400 with the path")]
    public async Task List_Should_Throw400_When_FolderMissing()
    {
        // Arrange
        var (service, server) = Create();

        // Act
        var act = () => service.List(server.Id, "/tmp/no-such-folder", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("Could not open folder /tmp/no-such-folder:*")
            .Which.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "UTF-8 file with BOM and CRLF => editable text with flags")]
    public async Task ReadContent_Should_ReturnText_When_FileIsUtf8()
    {
        // Arrange
        await fixture.Server.Run(@"printf '\xef\xbb\xbfline1\r\nline2' > /tmp/bom-crlf.env");
        var (service, server) = Create();

        // Act
        var content = await service.ReadContent(server.Id, "/tmp/bom-crlf.env", CancellationToken.None);

        // Assert
        content.Should().BeEquivalentTo(new
        {
            Path = "/tmp/bom-crlf.env",
            Size = 15L,
            Editable = true,
            Reason = (FileNotEditableReason?)null,
            Text = "line1\r\nline2",
            HasBom = true,
            LineSeparator = LineSeparator.Crlf
        });
        content.Sha256.Should().HaveLength(64);
    }

    [Fact(DisplayName = "File larger than 1 MB => not editable because of size, no text")]
    public async Task ReadContent_Should_RefuseText_When_FileTooLarge()
    {
        // Arrange
        await fixture.Server.Run("head -c 2097152 /dev/zero | tr '\\0' 'a' > /tmp/big.txt");
        var (service, server) = Create();

        // Act
        var content = await service.ReadContent(server.Id, "/tmp/big.txt", CancellationToken.None);

        // Assert
        content.Should().BeEquivalentTo(new
        {
            Size = 2097152L,
            Editable = false,
            Reason = FileNotEditableReason.TooLarge,
            Text = (string?)null,
            Sha256 = (string?)null
        });
    }

    [Fact(DisplayName = "Open a folder as a file => 400")]
    public async Task ReadContent_Should_Throw400_When_PathIsDirectory()
    {
        // Arrange
        var (service, server) = Create();

        // Act
        var act = () => service.ReadContent(server.Id, "/tmp", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("Only a regular file can be opened.")
            .Which.StatusCode.Should().Be(400);
    }
}