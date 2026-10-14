using Renci.SshNet;
using Renci.SshNet.Common;
using VpsPanel.Settings;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace VpsPanel.Ssh;

internal sealed class SshConnector(ISettingsStore settings) : ISshConnector
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<ISshSession> Connect(ServerSettings server, CancellationToken ct, TimeSpan? timeout = null)
    {
        var info = CreateConnectionInfo(server, timeout ?? DefaultTimeout);
        var verifier = new HostKeyVerifier(server, settings);
        var ssh = new SshClient(info);
        ssh.HostKeyReceived += verifier.Handle;
        try
        {
            await WithTimeout(info.Timeout, ct, token => ssh.ConnectAsync(token));
        }
        catch (Exception e)
        {
            ssh.Dispose();
            if (ct.IsCancellationRequested)
                throw;
            if (verifier.Mismatch)
                throw new HostKeyMismatchException(server.Name, verifier.ReceivedFingerprint!);
            throw new SshUnavailableException(Describe(e, info.Timeout));
        }
        return new SshSession(ssh, token => ConnectSftp(info, verifier, token));
    }

    private static ConnectionInfo CreateConnectionInfo(ServerSettings server, TimeSpan timeout)
    {
        if (!File.Exists(server.KeyPath))
            throw new PanelException(400, $"Key file not found: {server.KeyPath}");

        PrivateKeyFile key;
        try
        {
            key = new PrivateKeyFile(server.KeyPath);
        }
        catch (Exception e) when (e is SshException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new PanelException(400, $"Could not read key {server.KeyPath}: {e.Message}");
        }

        return new ConnectionInfo(server.Host, server.Port, server.User, new PrivateKeyAuthenticationMethod(server.User, key))
        {
            Timeout = timeout
        };
    }

    private static async Task<SftpClient> ConnectSftp(ConnectionInfo info, HostKeyVerifier verifier, CancellationToken ct)
    {
        var sftp = new SftpClient(info);
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