using VpsPanel.Settings;

namespace VpsPanel.Ssh;

internal interface ISshConnector
{
    Task<ISshSession> Connect(ServerSettings server, CancellationToken ct, TimeSpan? timeout = null);
}