using System.Net.WebSockets;
using System.Text.Json;
using Renci.SshNet;
using Renci.SshNet.Common;
using VpsPanel.Settings;
using VpsPanel.Ssh;

namespace VpsPanel.Terminal;

/// <summary>
/// Connects a browser terminal to an interactive shell on the server. Binary frames carry terminal bytes in both
/// directions, text frames carry JSON control messages.
/// </summary>
internal sealed class TerminalBridge(ISettingsStore settings, ISshConnector connector)
{
    private const int MaxSize = 1000;
    private const int MaxControlMessage = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task Run(WebSocket socket, string serverId, int columns, int rows, CancellationToken ct)
    {
        ISshSession session;
        try
        {
            session = await connector.Connect(settings.Read().GetServer(serverId), ct);
        }
        catch (PanelException e)
        {
            await Send(socket, new TerminalMessage("error", e.Message), ct);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
            return;
        }

        await using (session)
        {
            var shell = session.OpenShell(Clamp(columns), Clamp(rows));
            await Send(socket, new TerminalMessage("ready", null), ct);

            var output = PumpOutput(shell, socket, ct);
            var input = PumpInput(socket, shell, ct);
            if (await Task.WhenAny(output, input) == output)
            {
                await Ignore(output);
                // The browser answers with its own close frame, and the input pump ends on it.
                await Ignore(socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Session ended", ct));
                await Ignore(input);
                shell.Dispose();
            }
            else
            {
                await Ignore(input);
                shell.Dispose();
                await Ignore(output);
                if (socket.State == WebSocketState.CloseReceived)
                    await Ignore(socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct));
            }
        }
    }

    private static async Task PumpOutput(ShellStream shell, WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await shell.ReadAsync(buffer, ct)) > 0)
            await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, ct);
    }

    private static async Task PumpInput(WebSocket socket, ShellStream shell, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var control = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            switch (result.MessageType)
            {
                case WebSocketMessageType.Close:
                    return;
                case WebSocketMessageType.Binary:
                    await shell.WriteAsync(buffer.AsMemory(0, result.Count), ct);
                    await shell.FlushAsync(ct);
                    break;
                case WebSocketMessageType.Text:
                    if (control.Length + result.Count > MaxControlMessage)
                        throw new InvalidDataException("The control message is too long.");
                    control.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        Apply(JsonSerializer.Deserialize<TerminalControl>(control.ToArray(), Json), shell);
                        control.SetLength(0);
                    }
                    break;
            }
        }
    }

    private static void Apply(TerminalControl? control, ShellStream shell)
    {
        if (control is { Type: "resize" })
            shell.ChangeWindowSize((uint)Clamp(control.Cols), (uint)Clamp(control.Rows), 0, 0);
    }

    private static Task Send(WebSocket socket, TerminalMessage message, CancellationToken ct) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, Json), WebSocketMessageType.Text, true, ct);

    private static int Clamp(int value) => Math.Clamp(value, 1, MaxSize);

    // Either side may drop the connection at any moment, and the session is closed regardless of how a pump ended.
    private static async Task Ignore(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or OperationCanceledException
                                      or InvalidDataException or JsonException or IOException or SshException)
        {
        }
    }

    private sealed record TerminalControl(string? Type, int Cols, int Rows);

    private sealed record TerminalMessage(string Type, string? Message);
}