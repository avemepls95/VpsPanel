using Renci.SshNet;
using Renci.SshNet.Common;
using VpsPanel.Settings;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace VpsPanel.Ssh;

internal sealed class SshConnector(ISettingsStore settings, SsmOptions ssmOptions) : ISshConnector
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<ISshSession> Connect(ServerSettings server, CancellationToken ct, TimeSpan? timeout = null)
    {
        var key = ReadKey(server.KeyPath);
        var tunnel = server.Ssm is { } ssm ? await SsmTunnel.Start(ssm, server.Port, ssmOptions, ct) : null;
        var info = new ConnectionInfo(
            tunnel is null ? server.Host : "127.0.0.1",
            tunnel?.LocalPort ?? server.Port,
            server.User,
            new PrivateKeyAuthenticationMethod(server.User, key))
        {
            Timeout = timeout ?? DefaultTimeout
        };
        // SSM ends a session after its idle timeout, so a command that prints nothing for a long time would lose the connection.
        var keepAlive = tunnel is null ? Timeout.InfiniteTimeSpan : ssmOptions.KeepAliveInterval;
        var verifier = new HostKeyVerifier(server, settings);
        var ssh = new SshClient(info) { KeepAliveInterval = keepAlive };
        ssh.HostKeyReceived += verifier.Handle;
        try
        {
            await WithTimeout(info.Timeout, ct, token => ssh.ConnectAsync(token));
        }
        catch (Exception e)
        {
            ssh.Dispose();
            if (tunnel is not null)
                await tunnel.DisposeAsync();
            if (ct.IsCancellationRequested)
                throw;
            if (verifier.Mismatch)
                throw new HostKeyMismatchException(server.Name, verifier.ReceivedFingerprint!);
            throw new SshUnavailableException(Describe(e, info.Timeout));
        }
        return new SshSession(ssh, token => ConnectSftp(info, keepAlive, verifier, token), tunnel);
    }

    private static PrivateKeyFile ReadKey(string keyPath)
    {
        if (!File.Exists(keyPath))
            throw new PanelException(400, $"Key file not found: {keyPath}");

        try
        {
            return new PrivateKeyFile(keyPath);
        }
        catch (Exception e) when (e is SshException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new PanelException(400, $"Could not read key {keyPath}: {e.Message}");
        }
    }

    private static async Task<SftpClient> ConnectSftp(ConnectionInfo info, TimeSpan keepAlive, HostKeyVerifier verifier, CancellationToken ct)
    {
        var sftp = new SftpClient(info) { KeepAliveInterval = keepAlive };
        sftp.HostKeyReceived += verifier.Handle;
        try
        {
            await WithTimeout(info.Timeout, ct, token => sftp.ConnectAsync(token));
            return sftp;
        }
        catch (Exception e)
        {
            sftp.Dispose();
            if (ct.IsCancellationRequested)
                throw;
            throw new SshUnavailableException(Describe(e, info.Timeout));
        }
    }

    // The connection timeout is set by a linked cancellation token.
    private static async Task WithTimeout(TimeSpan timeout, CancellationToken ct, Func<CancellationToken, Task> connect)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        await connect(limit.Token);
    }

    private static string Describe(Exception e, TimeSpan timeout) => e switch
    {
        OperationCanceledException or SshOperationTimeoutException or TimeoutException =>
            $"the server did not respond within {timeout.TotalSeconds:0} s",
        SshAuthenticationException => "the server rejected the key",
        _ => e.Message
    };
}