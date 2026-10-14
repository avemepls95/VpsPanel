using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VpsPanel.Servers;

internal sealed class IpInfoProviderLookup(HttpClient http) : IProviderLookup
{
    public async Task<string?> Find(string host, CancellationToken ct)
    {
        try
        {
            var ip = await ResolveIpv4(host, ct);
            if (ip is null)
                return null;

            using var document = JsonDocument.Parse(await http.GetStringAsync($"https://ipinfo.io/{ip}/json", ct));
            return document.RootElement.TryGetProperty("org", out var org) && org.ValueKind == JsonValueKind.String
                ? ProviderName.FromOrg(org.GetString()!)
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or SocketException)
        {
            return null;
        }
    }

    private static async Task<IPAddress?> ResolveIpv4(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var address))
            return address.AddressFamily == AddressFamily.InterNetwork ? address : null;
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
    }
}