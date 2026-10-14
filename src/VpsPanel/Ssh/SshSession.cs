using System.Text;
using Renci.SshNet;

namespace VpsPanel.Ssh;

internal sealed class SshSession(SshClient ssh, Func<CancellationToken, Task<SftpClient>> connectSftp) : ISshSession
{
    private SftpClient? _sftp;

    public async Task<ExecResult> Exec(string command, CancellationToken ct)
    {
        using var cmd = ssh.CreateCommand(command);
        await cmd.ExecuteAsync(ct);
        return new ExecResult(cmd.ExitStatus, cmd.Result, cmd.Error);
    }

    public async Task<int?> ExecStreaming(string command, Func<string, Task> onLine, CancellationToken ct)
    {
        using var cmd = ssh.CreateCommand(command);
        var execution = cmd.ExecuteAsync(ct);
        using (var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8))
        {
            while (await reader.ReadLineAsync(ct) is { } line)
                await onLine(line);
        }
        await execution;
        return cmd.ExitStatus;
    }

    public async Task<ExecResult> ExecToStream(string command, Stream stdout, CancellationToken ct)
    {
        using var cmd = ssh.CreateCommand(command);
        var execution = cmd.ExecuteAsync(ct);
        await cmd.OutputStream.CopyToAsync(stdout, ct);
        await execution;
        return new ExecResult(cmd.ExitStatus, "", cmd.Error);
    }

    public async Task<Stream> OpenRead(string path, CancellationToken ct) =>
        await (await Sftp(ct)).OpenAsync(path, FileMode.Open, FileAccess.Read, ct);

    public async Task<Stream> OpenWrite(string path, bool createNew, CancellationToken ct) =>
        await (await Sftp(ct)).OpenAsync(path, createNew ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, ct);

    public ValueTask DisposeAsync()
    {
        _sftp?.Dispose();
        ssh.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SftpClient> Sftp(CancellationToken ct) => _sftp ??= await connectSftp(ct);
}