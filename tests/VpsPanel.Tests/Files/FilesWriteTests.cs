using System.Text;
using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Files;

[Collection("ssh")]
public sealed class FilesWriteTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private (FileService Service, ServerSettings Server) Create()
    {
        var server = fixture.Server.Server("Main");
        var store = TestSettings.Store(_dir, server);
        var backups = new BackupStore(new SettingsPaths(_dir.Path), TimeProvider.System);
        return (new FileService(store, new SshConnector(store, SsmOptions.Default), backups), server);
    }

    private static MemoryStream Text(string value) => new(Encoding.UTF8.GetBytes(value));

    [Fact(DisplayName = "Hash matches => new text written, same permissions, old version in the backup")]
    public async Task Save_Should_WriteTextAndBackup_When_HashMatches()
    {
        // Arrange
        await fixture.Server.Run("printf old > /tmp/save.env && chmod 600 /tmp/save.env");
        var (service, server) = Create();
        var request = new SaveFileRequest("/tmp/save.env", "new text", TextFileCodec.Sha256("old"u8.ToArray()), false, false);

        // Act
        var result = await service.Save(server.Id, request, CancellationToken.None);

        // Assert
        (await fixture.Server.Run("cat /tmp/save.env")).Should().Be("new text");
        (await fixture.Server.Run("stat -c %a /tmp/save.env")).Should().Be("600\n");
        (await File.ReadAllTextAsync(result.BackupPath)).Should().Be("old");
        result.Sha256.Should().Be(TextFileCodec.Sha256("new text"u8.ToArray()));
    }

    [Fact(DisplayName = "File changed after opening => 409, file untouched")]
    public async Task Save_Should_Throw409_When_FileChangedOnServer()
    {
        // Arrange
        await fixture.Server.Run("printf changed > /tmp/conflict.env");
        var (service, server) = Create();
        var request = new SaveFileRequest("/tmp/conflict.env", "mine", TextFileCodec.Sha256("original"u8.ToArray()), false, false);

        // Act
        var act = () => service.Save(server.Id, request, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The file changed on the server after it was opened.")
            .Which.StatusCode.Should().Be(409);
        (await fixture.Server.Run("cat /tmp/conflict.env")).Should().Be("changed");
    }

    [Fact(DisplayName = "Save with force when the file changed => file overwritten")]
    public async Task Save_Should_Overwrite_When_Forced()
    {
        // Arrange
        await fixture.Server.Run("printf changed > /tmp/force.env");
        var (service, server) = Create();
        var request = new SaveFileRequest("/tmp/force.env", "mine", "stale-hash", false, true);

        // Act
        await service.Save(server.Id, request, CancellationToken.None);

        // Assert
        (await fixture.Server.Run("cat /tmp/force.env")).Should().Be("mine");
    }

    [Fact(DisplayName = "Text with CRLF and the BOM flag => bytes on the server with BOM and CRLF")]
    public async Task Save_Should_KeepBomAndCrlf_When_Requested()
    {
        // Arrange
        await fixture.Server.Run("printf x > /tmp/bom.env");
        var (service, server) = Create();
        var request = new SaveFileRequest("/tmp/bom.env", "a\r\nb", TextFileCodec.Sha256("x"u8.ToArray()), true, false);

        // Act
        await service.Save(server.Id, request, CancellationToken.None);

        // Assert
        byte[] expected = [0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n', (byte)'b'];
        (await fixture.Server.Run("base64 -w0 /tmp/bom.env")).Should().Be(Convert.ToBase64String(expected));
    }

    [Fact(DisplayName = "New file with an unusual name => created, no backup")]
    public async Task Upload_Should_CreateFile_When_NameUnusual()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/up && mkdir -p /tmp/up");
        var (service, server) = Create();

        // Act
        var result = await service.Upload(server.Id, "/tmp/up", "-it's $x.txt", false, Text("payload"), CancellationToken.None);

        // Assert
        result.Should().Be(new UploadResult("/tmp/up/-it's $x.txt", null));
        (await fixture.Server.Run("cat -- \"/tmp/up/-it's \\$x.txt\"")).Should().Be("payload");
    }

    [Fact(DisplayName = "New file with a space and a newline in the name => created under this name")]
    public async Task Upload_Should_CreateFile_When_NameHasSpaceAndNewline()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/up-nl && mkdir -p /tmp/up-nl");
        var (service, server) = Create();

        // Act
        await service.Upload(server.Id, "/tmp/up-nl", "my file\nname.txt", false, Text("payload"), CancellationToken.None);

        // Assert
        (await fixture.Server.Run("cat -- /tmp/up-nl/my\\ file$'\\n'name.txt")).Should().Be("payload");
        (await fixture.Server.Run("find /tmp/up-nl -mindepth 1 -printf x")).Should().Be("x");
    }

    [Fact(DisplayName = "Upload cancelled while a new file is transferred => cancellation without changes, no incomplete file")]
    public async Task Upload_Should_RethrowCancellationAndRemovePartialFile_When_CancelledDuringCopy()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/up-cancel && mkdir -p /tmp/up-cancel");
        var (service, server) = Create();
        using var content = new FailingStream(new OperationCanceledException());

        // Act
        var act = () => service.Upload(server.Id, "/tmp/up-cancel", "big.bin", false, content, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await fixture.Server.Run("test ! -e /tmp/up-cancel/big.bin");
    }

    [Fact(DisplayName = "Read error while a new file is transferred => 500, no incomplete file")]
    public async Task Upload_Should_Throw500AndRemovePartialFile_When_CopyFails()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/up-fail && mkdir -p /tmp/up-fail");
        var (service, server) = Create();
        using var content = new FailingStream(new IOException("broken"));

        // Act
        var act = () => service.Upload(server.Id, "/tmp/up-fail", "big.bin", false, content, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The upload was interrupted, the incomplete file /tmp/up-fail/big.bin was deleted.")
            .Which.StatusCode.Should().Be(500);
        await fixture.Server.Run("test ! -e /tmp/up-fail/big.bin");
    }

    [Fact(DisplayName = "File exists, overwrite not confirmed => 409")]
    public async Task Upload_Should_Throw409_When_FileExistsWithoutOverwrite()
    {
        // Arrange
        await fixture.Server.Run("mkdir -p /tmp/up2 && printf old > /tmp/up2/app.env");
        var (service, server) = Create();

        // Act
        var act = () => service.Upload(server.Id, "/tmp/up2", "app.env", false, Text("new"), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("File app.env already exists in the folder.")
            .Which.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "File exists, overwrite confirmed => new content, same permissions, backup exists")]
    public async Task Upload_Should_ReplaceAndBackup_When_Overwrite()
    {
        // Arrange
        await fixture.Server.Run("mkdir -p /tmp/up3 && printf old > /tmp/up3/app.env && chmod 600 /tmp/up3/app.env");
        var (service, server) = Create();

        // Act
        var result = await service.Upload(server.Id, "/tmp/up3", "app.env", true, Text("new"), CancellationToken.None);

        // Assert
        (await fixture.Server.Run("cat /tmp/up3/app.env")).Should().Be("new");
        (await fixture.Server.Run("stat -c %a /tmp/up3/app.env")).Should().Be("600\n");
        (await File.ReadAllTextAsync(result.BackupPath!)).Should().Be("old");
    }

    [Fact(DisplayName = "File with an unusual name deleted => no file")]
    public async Task Delete_Should_RemoveFile_When_NameUnusual()
    {
        // Arrange
        await fixture.Server.Run("mkdir -p /tmp/del && printf x > \"/tmp/del/-it's \\$x\"");
        var (service, server) = Create();

        // Act
        await service.Delete(server.Id, "/tmp/del/-it's $x", null, CancellationToken.None);

        // Assert
        await fixture.Server.Run("test ! -e \"/tmp/del/-it's \\$x\"");
    }

    [Fact(DisplayName = "File with a newline in the name deleted => only this file is deleted")]
    public async Task Delete_Should_RemoveOnlyThatFile_When_NameHasNewline()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/del-nl && mkdir -p /tmp/del-nl && cd /tmp/del-nl && printf x > $'a\\nb' && printf y > a && printf z > b");
        var (service, server) = Create();

        // Act
        await service.Delete(server.Id, "/tmp/del-nl/a\nb", null, CancellationToken.None);

        // Assert
        await fixture.Server.Run("test -f /tmp/del-nl/a && test -f /tmp/del-nl/b && test ! -e \"$(printf '/tmp/del-nl/a\\nb')\"");
    }

    [Fact(DisplayName = "Link to a folder deleted => link removed, folder remains")]
    public async Task Delete_Should_RemoveLinkOnly_When_TargetIsDirectory()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/del-link && mkdir -p /tmp/del-link/real && touch /tmp/del-link/real/keep && ln -s real /tmp/del-link/alias");
        var (service, server) = Create();

        // Act
        await service.Delete(server.Id, "/tmp/del-link/alias", null, CancellationToken.None);

        // Assert
        await fixture.Server.Run("test ! -L /tmp/del-link/alias && test -f /tmp/del-link/real/keep");
    }

    [Theory(DisplayName = "Folder deleted with a wrong or empty confirmation => 400, folder remains")]
    [InlineData("old-lan")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Delete_Should_Throw400_When_ConfirmNameWrong(string? confirmName)
    {
        // Arrange
        await fixture.Server.Run("mkdir -p /tmp/del-dir/old-landing");
        var (service, server) = Create();

        // Act
        var act = () => service.Delete(server.Id, "/tmp/del-dir/old-landing", confirmName, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The folder name entered for confirmation does not match.")
            .Which.StatusCode.Should().Be(400);
        await fixture.Server.Run("test -d /tmp/del-dir/old-landing");
    }

    [Fact(DisplayName = "Folder deleted with the correct confirmation => no folder")]
    public async Task Delete_Should_RemoveFolder_When_ConfirmNameMatches()
    {
        // Arrange
        await fixture.Server.Run("mkdir -p /tmp/del-dir2/old-landing/inner && touch /tmp/del-dir2/old-landing/inner/file");
        var (service, server) = Create();

        // Act
        await service.Delete(server.Id, "/tmp/del-dir2/old-landing/", "old-landing", CancellationToken.None);

        // Assert
        await fixture.Server.Run("test ! -e /tmp/del-dir2/old-landing");
    }

    [Fact(DisplayName = "Upload over HTTP with a non-ASCII name => file created with the same name")]
    public async Task UploadEndpoint_Should_CreateFile_When_NameCyrillic()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/up-http && mkdir -p /tmp/up-http");
        var server = fixture.Server.Server();
        using var app = new TestApp();
        app.Settings.Update(s => s.Servers.Add(server));
        var client = app.CreatePanelClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("données"u8.ToArray()), "file", "Café-1.env");

        // Act
        var response = await client.PostAsync("/api/servers/" + server.Id + "/files/upload?dir=%2Ftmp%2Fup-http", form);

        // Assert
        response.EnsureSuccessStatusCode();
        (await fixture.Server.Run("cat /tmp/up-http/Café-1.env")).Should().Be("données");
    }

    [Fact(DisplayName = "Upload over HTTP with a UTF-8 name in the header without filename* => file created with the same name")]
    public async Task UploadEndpoint_Should_CreateFile_When_RawUtf8FileName()
    {
        // Arrange
        await fixture.Server.Run("rm -rf /tmp/up-raw && mkdir -p /tmp/up-raw");
        var server = fixture.Server.Server();
        using var app = new TestApp();
        app.Settings.Update(s => s.Servers.Add(server));
        var client = app.CreatePanelClient();
        using var form = new MultipartFormDataContent { HeaderEncodingSelector = (_, _) => Encoding.UTF8 };
        var part = new ByteArrayContent("données"u8.ToArray());
        part.Headers.TryAddWithoutValidation("Content-Disposition", "form-data; name=\"file\"; filename=\"résumé.txt\"");
        form.Add(part);

        // Act
        var response = await client.PostAsync("/api/servers/" + server.Id + "/files/upload?dir=%2Ftmp%2Fup-raw", form);

        // Assert
        response.EnsureSuccessStatusCode();
        (await fixture.Server.Run("cat /tmp/up-raw/résumé.txt")).Should().Be("données");
    }
}