namespace VpsPanel.Ssh;

internal sealed class SshUnavailableException(string detail)
    : PanelException(502, $"Could not connect over SSH: {detail}");