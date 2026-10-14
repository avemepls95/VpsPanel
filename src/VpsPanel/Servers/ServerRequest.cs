namespace VpsPanel.Servers;

internal sealed record ServerRequest(string? Name, string? Host, int? Port, string? User, string? KeyPath, string? ProviderName)
{
    private static string DefaultKeyPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");

    public ValidServerRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new PanelException(400, "Enter the server name.");
        if (string.IsNullOrWhiteSpace(Host))
            throw new PanelException(400, "Enter the server address.");
        var port = Port ?? 22;
        if (port is < 1 or > 65535)
            throw new PanelException(400, "The port must be from 1 to 65535.");

        return new ValidServerRequest(
            Name.Trim(),
            Host.Trim(),
            port,
            string.IsNullOrWhiteSpace(User) ? "root" : User.Trim(),
            string.IsNullOrWhiteSpace(KeyPath) ? DefaultKeyPath : KeyPath.Trim(),
            string.IsNullOrWhiteSpace(ProviderName) ? null : ProviderName.Trim());
    }
}

internal sealed record ValidServerRequest(string Name, string Host, int Port, string User, string KeyPath, string? ProviderName);