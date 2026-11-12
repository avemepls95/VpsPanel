using System.Net;
using System.Net.WebSockets;
using VpsPanel.Settings;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

[Collection("ssh")]
public sealed class TerminalEndpointsTests(SshServerFixture fixture) : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private ServerSettings AddServer()
    {
        var server = fixture.Server.Server();
        _app.Settings.Update(s => s.Servers.Add(server));
        return server;
    }

    private Task<TerminalClient> Connect(string serverId, string query = "", string origin = "http://localhost") =>
        TerminalClient.Connect(_app, serverId, query, origin);

    private static async Task<TerminalClient> Ready(Task<TerminalClient> connecting)
    {
        var terminal = await connecting;
        await terminal.WaitReady();
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
        for (var deadline = DateTime.UtcNow + TerminalClient.Wait; alive && DateTime.UtcNow < deadline; await Task.Delay(100))
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
}