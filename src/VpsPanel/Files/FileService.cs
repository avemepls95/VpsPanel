using Microsoft.Net.Http.Headers;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using static VpsPanel.Ssh.ShellQuote;

namespace VpsPanel.Files;

internal sealed class FileService(ISettingsStore settings, ISshConnector connector, BackupStore backups)
{
    public async Task<DirectoryListing> List(string serverId, string path, CancellationToken ct)
    {
        var directory = RemotePath.Normalize(path);
        await using var session = await Connect(serverId, ct);
        return new DirectoryListing(directory, await RemoteFs.List(session, directory, ct));
    }

    public async Task<FileContentView> ReadContent(string serverId, string path, CancellationToken ct)
    {
        var file = RemotePath.Normalize(path);
        await using var session = await Connect(serverId, ct);
        var (type, size) = await RemoteFs.StatFollowWithSize(session, file, ct)
                           ?? throw new PanelException(404, $"File not found: {file}");
        if (type != 'f')
            throw new PanelException(400, "Only a regular file can be opened.");
        if (size > TextFileCodec.MaxEditableBytes)
            return new FileContentView(file, size, false, FileNotEditableReason.TooLarge, null, null, false, LineSeparator.Lf);

        var bytes = await RemoteFs.ReadAll(session, file, ct);
        var inspection = TextFileCodec.Inspect(bytes);
        return new FileContentView(file, bytes.Length, inspection.Editable, inspection.Reason, inspection.Text,
            inspection.Editable ? TextFileCodec.Sha256(bytes) : null, inspection.HasBom, inspection.LineSeparator);
    }

    public async Task<SaveFileResult> Save(string serverId, SaveFileRequest request, CancellationToken ct)
    {
        var file = RemotePath.Normalize(request.Path);
        var server = settings.Read().GetServer(serverId);
        await using var session = await connector.Connect(server, ct);

        var current = await RemoteFs.ReadAll(session, file, ct);
        if (!request.Force && TextFileCodec.Sha256(current) != request.Sha256)
            throw new PanelException(409, "The file changed on the server after it was opened.");

        var backup = await backups.Save(server.Name, file, current, ct);
        var bytes = TextFileCodec.Encode(request.Text, request.HasBom);
        var stream = await session.OpenWrite(file, createNew: false, ct);
        try
        {
            await using (stream)
                await stream.WriteAsync(bytes, ct);
        }
        catch (Exception e) when (e is not PanelException and not OperationCanceledException)
        {
            throw new PanelException(500, $"The write was interrupted, file {file} may be incomplete. The previous version is saved to {backup}");
        }
        return new SaveFileResult(TextFileCodec.Sha256(bytes), backup);
    }

    public async Task<UploadResult> Upload(string serverId, string directory, string fileName, bool overwrite, Stream content, CancellationToken ct)
    {
        RemotePath.ValidateFileName(fileName);
        var target = RemotePath.Combine(RemotePath.Normalize(directory), fileName);
        var server = settings.Read().GetServer(serverId);
        await using var session = await connector.Connect(server, ct);

        var existing = await RemoteFs.Lstat(session, target, ct);
        if (existing == 'd')
            throw new PanelException(409, $"The folder already has a folder named {fileName}.");
        if (existing is not null && !overwrite)
            throw new PanelException(409, $"File {fileName} already exists in the folder.");

        string? backup = null;
        if (existing is not null)
        {
            await using var current = await session.OpenRead(target, ct);
            backup = await backups.Save(server.Name, target, current, ct);
        }

        var stream = await session.OpenWrite(target, createNew: existing is null, ct);
        try
        {
            await using (stream)
                await content.CopyToAsync(stream, ct);
        }
        catch (Exception e) when (e is not PanelException)
        {
            if (backup is null)
                await RemoveQuietly(session, target);
            if (e is OperationCanceledException)
                throw;
            throw new PanelException(500, backup is not null
                ? $"The upload was interrupted, file {target} may be incomplete. The previous version is saved to {backup}"
                : $"The upload was interrupted, the incomplete file {target} was deleted.");
        }
        return new UploadResult(target, backup);
    }

    public async Task Delete(string serverId, string path, string? confirmName, CancellationToken ct)
    {
        var target = RemotePath.Normalize(path);
        if (target == "/")
            throw new PanelException(400, "The server root cannot be deleted.");

        await using var session = await Connect(serverId, ct);
        var type = await RemoteFs.Lstat(session, target, ct) ?? throw new PanelException(404, $"Not found: {target}");
        if (type == 'd')
        {
            if (string.IsNullOrEmpty(confirmName) || confirmName != RemotePath.Name(target))
                throw new PanelException(400, "The folder name entered for confirmation does not match.");
            await RemoteFs.Run(session, $"rm -rf -- {Q(target)}", ct);
        }
        else
        {
            await RemoteFs.Run(session, $"rm -f -- {Q(target)}", ct);
        }
    }

    public async Task Download(string serverId, string path, HttpContext http, CancellationToken ct)
    {
        var target = RemotePath.Normalize(path);
        await using var session = await Connect(serverId, ct);
        var type = await RemoteFs.StatFollow(session, target, ct) ?? throw new PanelException(404, $"Not found: {target}");

        if (type == 'd')
        {
            if (target == "/")
                throw new PanelException(400, "The server root cannot be downloaded as an archive.");
            // For a link to a folder, the folder it points to is archived.
            var real = (await session.Exec($"readlink -fz -- {Q(target)}", ct)).Stdout.TrimEnd('\0');
            var source = real.Length > 0 ? real : target;
            SetAttachment(http, RemotePath.Name(target) + ".tar.gz", "application/gzip");
            var result = await session.ExecToStream(
                $"tar czf - -C {Q(RemotePath.Parent(source))} -- {Q(RemotePath.Name(source))}", http.Response.Body, ct);
            // tar returns 1 if a file changed while being read. This is not an error.
            if (result.ExitCode is null or >= 2)
                Fail(http, $"The archive was not created: {result.Stderr.Trim()}");
            return;
        }

        if (type != 'f')
            throw new PanelException(400, "Only a file or a folder can be downloaded.");
        SetAttachment(http, RemotePath.Name(target), "application/octet-stream");
        try
        {
            await using var stream = await session.OpenRead(target, ct);
            await stream.CopyToAsync(http.Response.Body, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException && http.Response.HasStarted)
        {
            http.Abort();
        }
    }

    private static void SetAttachment(HttpContext http, string fileName, string contentType)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        http.Response.ContentType = contentType;
        http.Response.Headers.ContentDisposition = disposition.ToString();
    }

    // If bytes have already been sent, the connection drop prevents the browser from saving a truncated file as complete.
    private static void Fail(HttpContext http, string message)
    {
        if (!http.Response.HasStarted)
            throw new PanelException(500, message);
        http.Abort();
    }

    private static async Task RemoveQuietly(ISshSession session, string path)
    {
        try
        {
            await session.Exec($"rm -f -- {Q(path)}", CancellationToken.None);
        }
        catch (Exception)
        {
            // The connection may already be dropped, in which case there is nobody to delete the file.
        }
    }

    private async Task<ISshSession> Connect(string serverId, CancellationToken ct) =>
        await connector.Connect(settings.Read().GetServer(serverId), ct);
}