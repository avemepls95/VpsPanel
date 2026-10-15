using System.Text.RegularExpressions;
using VpsPanel.Settings;

namespace VpsPanel.Servers;

internal sealed record SsmRequest(string? InstanceId, string? Profile, string? Region);

internal sealed partial record ServerRequest(string? Name, string? Host, int? Port, string? User, string? KeyPath, string? ProviderName, SsmRequest? Ssm = null)
{
    private static string DefaultKeyPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");

    public ValidServerRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new PanelException(400, "Enter the server name.");
        var ssm = ValidateSsm();
        if (ssm is null && string.IsNullOrWhiteSpace(Host))
            throw new PanelException(400, "Enter the server address.");
        var port = Port ?? 22;
        if (port is < 1 or > 65535)
            throw new PanelException(400, "The port must be from 1 to 65535.");

        return new ValidServerRequest(
            Name.Trim(),
            ssm is null ? Host!.Trim() : "",
            port,
            string.IsNullOrWhiteSpace(User) ? "root" : User.Trim(),
            string.IsNullOrWhiteSpace(KeyPath) ? DefaultKeyPath : KeyPath.Trim(),
            string.IsNullOrWhiteSpace(ProviderName) ? null : ProviderName.Trim(),
            ssm);
    }

    private SsmSettings? ValidateSsm()
    {
        if (Ssm is null)
            return null;
        if (string.IsNullOrWhiteSpace(Ssm.InstanceId))
            throw new PanelException(400, "Enter the instance ID.");
        var instanceId = Ssm.InstanceId.Trim();
        if (!InstanceIdFormat().IsMatch(instanceId))
            throw new PanelException(400, "The instance ID must look like i-0123456789abcdef0.");
        return new SsmSettings(
            instanceId,
            string.IsNullOrWhiteSpace(Ssm.Profile) ? null : Ssm.Profile.Trim(),
            string.IsNullOrWhiteSpace(Ssm.Region) ? null : Ssm.Region.Trim());
    }

    // An EC2 instance (i-) or a hybrid managed node (mi-).
    [GeneratedRegex("^(i-([0-9a-f]{8}|[0-9a-f]{17})|mi-[0-9a-f]{17})$")]
    private static partial Regex InstanceIdFormat();
}

internal sealed record ValidServerRequest(string Name, string Host, int Port, string User, string KeyPath, string? ProviderName, SsmSettings? Ssm);