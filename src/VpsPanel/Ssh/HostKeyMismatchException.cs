namespace VpsPanel.Ssh;

internal sealed class HostKeyMismatchException(string serverName, string fingerprint) : PanelException(409,
    $"The key for server '{serverName}' has changed, new fingerprint {fingerprint}. If the server was reinstalled, reset the fingerprint in the server settings.");