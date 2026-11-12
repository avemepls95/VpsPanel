using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;

namespace VpsPanel.Tests.Infrastructure;

/// <summary>
/// A browser side of the terminal connection.
/// </summary>
internal sealed class TerminalClient(WebSocket socket) : IDisposable
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly StringBuilder _output = new();

    public static async Task<TerminalClient> Connect(TestApp app, string serverId, string query = "", string origin = "http://localhost")
    {
        var client = app.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Origin = origin;
        var socket = await client.ConnectAsync(new Uri($"ws://localhost/api/servers/{serverId}/terminal{query}"), CancellationToken.None);
        return new TerminalClient(socket);
    }

    public WebSocket Socket => socket;

    public Task Type(string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Binary, true, CancellationToken.None);

    public Task SendText(string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    public async Task WaitReady() =>
        (await ReceiveText()).Should().Be("""{"type":"ready","message":null}""");

    public async Task<string> ReceiveText()
    {
        var (type, data) = await Receive();
        type.Should().Be(WebSocketMessageType.Text);
        return data;
    }

    public async Task<Match> ReadUntil(string pattern)
    {
        Match match;
        while (!(match = Regex.Match(_output.ToString(), pattern)).Success)
        {
            var (type, data) = await Receive();
            type.Should().Be(WebSocketMessageType.Binary, $"the output so far: {_output}");
            _output.Append(data);
        }
        return match;
    }

    public async Task ReadUntilClosed()
    {
        while ((await Receive()).Type != WebSocketMessageType.Close)
        {
        }
    }

    private async Task<(WebSocketMessageType Type, string Data)> Receive()
    {
        using var timeout = new CancellationTokenSource(Wait);
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return (result.MessageType, Encoding.UTF8.GetString(message.ToArray()));
    }

    public void Dispose() => socket.Dispose();
}