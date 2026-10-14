using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;
using static VpsPanel.Ssh.ShellQuote;

namespace VpsPanel.Tests.Files;

[Collection("ssh")]
public sealed class FilesDownloadTests(SshServerFixture fixture) : IDisposable
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

    private static DefaultHttpContext Http()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static async Task<List<string>> ArchiveEntries(Stream body)
    {
        body.Position = 0;
        await using var gzip = new GZipStream(body, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);
        var names = new List<string>();
        while (await tar.GetNextEntryAsync() is { } entry)
            names.Add(entry.Name.TrimEnd('/'));
        return names;
    }

    [Fact(DisplayName = "Folder with a space and a file with a leading hyphen => tar.gz with all contents")]
    public async Task Download_Should_StreamArchive_When_PathIsDirectory()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/dl && mkdir -p '/tmp/dl/my dir' && printf a > '/tmp/dl/my dir/a.txt' && printf b > '/tmp/dl/my dir/-b.txt'");
        var (service, server) = Create();
        var http = Http();

        // Act
        await service.Download(server.Id, "/tmp/dl/my dir", http, CancellationToken.None);

        // Assert
        http.Response.ContentType.Should().Be("application/gzip");
        http.Response.Headers.ContentDisposition.ToString().Should().Contain("filename*=UTF-8''my%20dir.tar.gz");
        (await ArchiveEntries(http.Response.Body)).Should().BeEquivalentTo("my dir", "my dir/a.txt", "my dir/-b.txt");
    }

    [Fact(DisplayName = "File => file bytes and the name in the attachment")]
    public async Task Download_Should_StreamBytes_When_PathIsFile()
    {
        // Arrange
        await fixture.Server.Run("printf 'file-bytes' > /tmp/dl-file.bin");
        var (service, server) = Create();
        var http = Http();

        // Act
        await service.Download(server.Id, "/tmp/dl-file.bin", http, CancellationToken.None);

        // Assert
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal("file-bytes"u8.ToArray());
        http.Response.Headers.ContentDisposition.ToString().Should().Contain("dl-file.bin");
    }

    [Fact(DisplayName = "Download root as an archive => 400")]
    public async Task Download_Should_Throw400_When_PathIsRoot()
    {
        // Arrange
        var (service, server) = Create();

        // Act
        var act = () => service.Download(server.Id, "/", Http(), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The server root cannot be downloaded as an archive.")
            .Which.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "No file => 404")]
    public async Task Download_Should_Throw404_When_PathDoesNotExist()
    {
        // Arrange
        var (service, server) = Create();

        // Act
        var act = () => service.Download(server.Id, "/tmp/dl-missing-file", Http(), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("Not found: /tmp/dl-missing-file")
            .Which.StatusCode.Should().Be(404);
    }

    private const string SpecialName = "-a b'c$d\ne";

    [Fact(DisplayName = "File with a space, quote, dollar sign, leading hyphen and newline in the name => file bytes")]
    public async Task Download_Should_StreamBytes_When_FileNameHasSpecialCharacters()
    {
        // Arrange
        await fixture.Server.Run($"rm -rf /tmp/dl-special && mkdir -p /tmp/dl-special && printf special > {Q("/tmp/dl-special/" + SpecialName)}");
        var (service, server) = Create();
        var http = Http();

        // Act
        await service.Download(server.Id, "/tmp/dl-special/" + SpecialName, http, CancellationToken.None);

        // Assert
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal("special"u8.ToArray());
        var disposition = http.Response.Headers.ContentDisposition.ToString();
        disposition.Should().Contain("filename*=UTF-8''-a%20b%27c$d%0Ae");
        disposition.Should().NotContainAny("\n", "\r");
    }

    [Fact(DisplayName = "Folder with a space, quote, dollar sign, leading hyphen and newline in the name => tar.gz with all contents")]
    public async Task Download_Should_StreamArchive_When_DirectoryNameHasSpecialCharacters()
    {
        // Arrange
        var directory = Q("/tmp/dl-special-dir/" + SpecialName);
        await fixture.Server.Run($"rm -rf /tmp/dl-special-dir && mkdir -p {directory} && printf x > {directory}/f.txt");
        var (service, server) = Create();
        var http = Http();

        // Act
        await service.Download(server.Id, "/tmp/dl-special-dir/" + SpecialName, http, CancellationToken.None);

        // Assert
        (await ArchiveEntries(http.Response.Body)).Should().BeEquivalentTo(SpecialName, SpecialName + "/f.txt");
    }

    [Theory(DisplayName = "Folder name ends with a space or a newline => archive of exactly this folder")]
    [InlineData("dir ")]
    [InlineData("dir\n")]
    public async Task Download_Should_StreamArchive_When_DirectoryNameEndsWithWhitespace(string name)
    {
        // Arrange
        var directory = Q("/tmp/dl-trailing/" + name);
        var sibling = Q("/tmp/dl-trailing/dir");
        await fixture.Server.Run($"rm -rf /tmp/dl-trailing && mkdir -p {directory} {sibling} && printf x > {directory}/inside.txt && printf y > {sibling}/other.txt");
        var (service, server) = Create();
        var http = Http();

        // Act
        await service.Download(server.Id, "/tmp/dl-trailing/" + name, http, CancellationToken.None);

        // Assert
        (await ArchiveEntries(http.Response.Body)).Should().BeEquivalentTo(name, name + "/inside.txt");
    }
}