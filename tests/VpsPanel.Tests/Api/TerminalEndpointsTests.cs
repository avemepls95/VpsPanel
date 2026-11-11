using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using VpsPanel.Settings;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

[Collection("ssh")]
public sealed class TerminalEndpointsTests(SshServerFixture fixture) : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private ServerSettings AddServer()
    {
        var server = fixture.Server.Server();
        _app.Settings.Update(s => s.Servers.Add(server));
        return server;
    }

    private async Task<TerminalClient> Connect(string serverId, string query = "", string origin = "http://localhost")
    {
        var client = _app.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Origin = origin;
        var socket = await client.ConnectAsync(new Uri($"ws://localhost/api/servers/{serverId}/terminal{query}"), CancellationToken.None);
        return new TerminalClient(socket);
    }

    private static async Task<TerminalClient> Ready(Task<TerminalClient> connecting)
    {
        var terminal = await connecting;
        (await terminal.ReceiveText()).Should().Be("""{"type":"ready","message":null}""");
        return terminal;
    }

    [Fact(DisplayName = "Command typed in the terminal => its output arrives")]
    public async Task Terminal_Should_StreamOutput_When_CommandTyped()
    {
        // Arrange
        var server = AddServer();
        using var terminal = await Ready(Connect(server.Id));

        // Act
        await terminal.Type("echo result-$((6*7))\n");

        // Assert
        (await terminal.ReadUntil("result-42")).Success.Should().BeTrue();
    }

    [Fact(DisplayName = "Size passed on connect and changed later => the shell sees the new size")]
    public async Task Terminal_Should_ResizeShell_When_ResizeMessageSent()
    {
        // Arrange
        var server = AddServer();
        using var terminal = await Ready(Connect(server.Id, "?cols=100&rows=30"));
        await terminal.Type("echo size-$(stty size | tr ' ' x)\n");
        await terminal.ReadUntil("size-30x100");

        // Act
        await terminal.SendText("""{"type":"resize","cols":120,"rows":40}""");

        // Assert
        await terminal.Type("echo size-$(stty size | tr ' ' x)\n");
        (await terminal.ReadUntil("size-40x120")).Success.Should().BeTrue();
    }

    [Fact(DisplayName = "Shell exits => the panel closes the connection")]
    public async Task Terminal_Should_CloseSocket_When_ShellExits()
    {
        // Arrange
        var server = AddServer();
        using var terminal = await Ready(Connect(server.Id));

        // Act
        await terminal.Type("exit\n");

        // Assert
        await terminal.ReadUntilClosed();
        terminal.Socket.CloseStatus.Should().Be(WebSocketCloseStatus.NormalClosure);
        terminal.Socket.CloseStatusDescription.Should().Be("Session ended");
    }

    [Fact(DisplayName = "Browser closes the connection => the shell on the server ends")]
    public async Task Terminal_Should_EndShell_When_BrowserCloses()
    {
        // Arrange
        var server = AddServer();
        using var terminal = await Ready(Connect(server.Id));
        await terminal.Type("echo pid-$$-end\n");
        var pid = (await terminal.ReadUntil(@"pid-(\d+)-end")).Groups[1].Value;

        // Act
        await terminal.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

        // Assert
        var alive = true;
        for (var deadline = DateTime.UtcNow + Wait; alive && DateTime.UtcNow < deadline; await Task.Delay(100))
            alive = (await fixture.Server.Run($"kill -0 {pid} 2>/dev/null && echo alive || true")).Trim() == "alive";
        alive.Should().BeFalse();
    }

    [Fact(DisplayName = "Unknown server => error message and closed connection")]
    public async Task Terminal_Should_SendError_When_ServerUnknown()
    {
        // Arrange
        using var terminal = await Connect("missing");

        // Act
        var message = await terminal.ReceiveText();

        // Assert
        message.Should().Be("""{"type":"error","message":"Server not found."}""");
        await terminal.ReadUntilClosed();
        terminal.Socket.CloseStatus.Should().Be(WebSocketCloseStatus.NormalClosure);
    }

    [Fact(DisplayName = "Page of another site opens the terminal => 403")]
    public async Task Terminal_Should_Reject_When_OriginForeign()
    {
        // Arrange
        var server = AddServer();

        // Act
        var act = () => Connect(server.Id, origin: "http://evil.example.com");

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{(int)HttpStatusCode.Forbidden}*");
    }

    private sealed class TerminalClient(WebSocket socket) : IDisposable
    {
        private readonly StringBuilder _output = new();

        public WebSocket Socket => socket;

        public Task Type(string text) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Binary, true, CancellationToken.None);

        public Task SendText(string text) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

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
}