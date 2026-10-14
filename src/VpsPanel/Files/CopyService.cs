using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using static VpsPanel.Ssh.ShellQuote;

namespace VpsPanel.Files;

internal sealed class CopyService(ISettingsStore settings, ISshConnector connector, RunManager runs, BackupStore backups)
{
    public async Task<Run> Start(CopyRequest request, CancellationToken ct)
    {
        var all = settings.Read();
        var source = all.GetServer(request.SourceServerId);
        var target = all.GetServer(request.TargetServerId);
        var sourcePath = RemotePath.Normalize(request.SourcePath);
        if (sourcePath == "/")
            throw new PanelException(400, "The server root cannot be copied.");
        var targetDir = RemotePath.Normalize(request.TargetDir);
        var name = RemotePath.Name(sourcePath);
        var targetPath = RemotePath.Combine(targetDir, name);

        char sourceType;
        await using (var session = await connector.Connect(source, ct))
            sourceType = await RemoteFs.Lstat(session, sourcePath, ct) ?? throw new PanelException(404, $"Not found: {sourcePath}");

        if (source.Id == target.Id && sourceType == 'd' && RemotePath.IsInside(targetDir, sourcePath))
            throw new PanelException(400, "A folder cannot be copied into itself.");
        if (source.Id == target.Id && sourceType != 'd' && targetPath == sourcePath)
            throw new PanelException(400, "The file is already in this folder.");

        char? existing;
        await using (var session = await connector.Connect(target, ct))
        {
            if (await RemoteFs.StatFollow(session, targetDir, ct) != 'd')
                throw new PanelException(400, $"Destination folder not found: {targetDir}");
            existing = await RemoteFs.Lstat(session, targetPath, ct);
        }
        if (existing == 'd')
            throw new PanelException(409, $"The destination folder already has a folder named {name}.");
        if (existing is not null && (sourceType != 'f' || !request.Overwrite))
            throw new PanelException(409, $"The destination folder already has a file named {name}.");

        return runs.Start(
            new RunStart(target.Id, null, $"Copy · {name}", $"{source.Name} → {target.Name}", null, ""),
            run => Copy(source, target, sourcePath, targetPath, existing is not null, run));
    }

    private async Task<int?> Copy(ServerSettings source, ServerSettings target, string sourcePath, string targetPath, bool replace, Run run)
    {
        await using var from = await connector.Connect(source, run.Token);
        await using var to = await connector.Connect(target, run.Token);
        var entries = (await RemoteFs.Walk(from, sourcePath, run.Token))
            .Select(e => (Entry: e, Source: Join(sourcePath, e.RelativePath), Target: Join(targetPath, e.RelativePath)))
            .ToList();

        var directories = entries.Where(e => e.Entry.Type == 'd').ToList();
        await RemoteFs.RunScript(to, directories.Select(d => $"mkdir -p -- {Q(d.Target)}").ToList(), run.Token);
        foreach (var directory in directories)
            run.Append($"Created folder {directory.Target}");

        var modes = new List<string>();
        foreach (var file in entries.Where(e => e.Entry.Type == 'f'))
        {
            if (replace && file.Target == targetPath)
            {
                await using var current = await to.OpenRead(file.Target, run.Token);
                run.Append($"Previous version saved to {await backups.Save(target.Name, file.Target, current, run.Token)}");
            }
            else
            {
                modes.Add($"chmod {Q(file.Entry.Mode)} -- {Q(file.Target)}");
            }

            await using (var reader = await from.OpenRead(file.Source, run.Token))
            await using (var writer = await to.OpenWrite(file.Target, createNew: false, run.Token))
                await reader.CopyToAsync(writer, run.Token);
            run.Append($"Copied file {file.Target}");
        }

        var links = entries.Where(e => e.Entry.Type == 'l').ToList();
        await RemoteFs.RunScript(to, links.Select(l => $"ln -s -- {Q(l.Entry.LinkTarget)} {Q(l.Target)}").ToList(), run.Token);
        foreach (var link in links)
            run.Append($"Created link {link.Target} → {link.Entry.LinkTarget}");

        foreach (var skipped in entries.Where(e => e.Entry.Type is not ('d' or 'f' or 'l')))
            run.Append($"Skipped FIFO, socket or device: {skipped.Target}");

        // Folder permissions are set after the files are copied, so that a folder without write permission can still receive files.
        modes.AddRange(directories.Select(d => $"chmod {Q(d.Entry.Mode)} -- {Q(d.Target)}"));
        await RemoteFs.RunScript(to, modes, run.Token);
        return 0;
    }

    private static string Join(string root, string relative) => relative.Length == 0 ? root : root + "/" + relative;
}