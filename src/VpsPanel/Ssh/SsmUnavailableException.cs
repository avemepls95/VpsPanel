namespace VpsPanel.Ssh;

internal sealed class SsmUnavailableException(string detail)
    : PanelException(502, $"Could not start the AWS SSM session: {detail}");