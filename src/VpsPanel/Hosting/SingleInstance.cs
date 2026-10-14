using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace VpsPanel.Hosting;

internal enum SingleInstanceResult
{
    PortFree,
    AlreadyRunning,
    PortBusy
}

internal static class SingleInstance
{
    public static async Task<SingleInstanceResult> Check(int port)
    {
        if (IsPortFree(port))
            return SingleInstanceResult.PortFree;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var ping = await http.GetFromJsonAsync<PingResponse>($"http://127.0.0.1:{port}/api/ping");
            return ping?.App == "VpsPanel" ? SingleInstanceResult.AlreadyRunning : SingleInstanceResult.PortBusy;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return SingleInstanceResult.PortBusy;
        }
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed record PingResponse(string? App);
}