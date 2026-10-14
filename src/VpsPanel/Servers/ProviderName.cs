using System.Text.RegularExpressions;

namespace VpsPanel.Servers;

internal static partial class ProviderName
{
    public static string FromOrg(string org) => AsNumber().Replace(org, "").Trim();

    [GeneratedRegex(@"^AS\d+\s+")]
    private static partial Regex AsNumber();
}