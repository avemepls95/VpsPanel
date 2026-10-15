using System.Net;
using System.Net.Sockets;

// Imitates "aws ssm start-session --document-name AWS-StartPortForwardingSession" for the tests.
// FAKE_AWS_MODE: ok (default), not-connected, no-plugin, hang.
// FAKE_AWS_TARGET: host:port where accepted connections are forwarded in the ok mode.
// FAKE_AWS_PID_FILE: if set, the process id is written there.
// FAKE_AWS_IDLE_TIMEOUT_MS: if set, a connection without traffic for that time is closed, as SSM does.
// FAKE_AWS_TERMINATED_FILE: "ssm terminate-session" appends the session id there.
const string SessionId = "fake-0123456789abcdef0";

if (Array.IndexOf(args, "terminate-session") >= 0)
{
    var terminated = Environment.GetEnvironmentVariable("FAKE_AWS_TERMINATED_FILE");
    if (!string.IsNullOrEmpty(terminated))
        File.AppendAllText(terminated, args[Array.IndexOf(args, "--session-id") + 1] + "\n");
    return 0;
}

if (Environment.GetEnvironmentVariable("FAKE_AWS_PID_FILE") is { Length: > 0 } pidFile)
    File.WriteAllText(pidFile, Environment.ProcessId.ToString());

switch (Environment.GetEnvironmentVariable("FAKE_AWS_MODE") ?? "ok")
{
    case "not-connected":
        Console.Error.WriteLine();
        Console.Error.WriteLine("An error occurred (TargetNotConnected) when calling the StartSession operation: i-0123456789abcdef0 is not connected.");
        return 254;
    case "no-plugin":
        Console.Error.WriteLine();
        Console.Error.WriteLine("SessionManagerPlugin is not found. Please refer to SessionManager Documentation here: http://docs.aws.amazon.com/console/systems-manager/session-manager-plugin-not-found");
        return 255;
    case "hang":
        await Task.Delay(Timeout.Infinite);
        return 0;
}

var target = Environment.GetEnvironmentVariable("FAKE_AWS_TARGET")
             ?? throw new InvalidOperationException("FAKE_AWS_TARGET is not set.");
var separator = target.LastIndexOf(':');
var targetHost = target[..separator];
var targetPort = int.Parse(target[(separator + 1)..]);
var idleTimeout = Environment.GetEnvironmentVariable("FAKE_AWS_IDLE_TIMEOUT_MS") is { Length: > 0 } idle
    ? TimeSpan.FromMilliseconds(int.Parse(idle))
    : Timeout.InfiniteTimeSpan;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

Console.WriteLine();
Console.WriteLine($"Starting session with SessionId: {SessionId}");
Console.WriteLine($"Port {localPort} opened for sessionId {SessionId}.");
Console.WriteLine("Waiting for connections...");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Forward(client, targetHost, targetPort, idleTimeout);
}

static async Task Forward(TcpClient client, string host, int port, TimeSpan idleTimeout)
{
    using (client)
    using (var server = new TcpClient())
    {
        try
        {
            await server.ConnectAsync(host, port);
            var activity = new Activity();
            var local = client.GetStream();
            var remote = server.GetStream();
            await Task.WhenAny(Pump(local, remote, activity), Pump(remote, local, activity), WaitForIdle(activity, idleTimeout));
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
        }
    }
}

static async Task Pump(Stream from, Stream to, Activity activity)
{
    var buffer = new byte[16384];
    int read;
    while ((read = await from.ReadAsync(buffer)) > 0)
    {
        activity.Touch();
        await to.WriteAsync(buffer.AsMemory(0, read));
    }
}

static async Task WaitForIdle(Activity activity, TimeSpan idleTimeout)
{
    if (idleTimeout == Timeout.InfiniteTimeSpan)
        await Task.Delay(Timeout.Infinite);
    while (activity.Since < idleTimeout)
        await Task.Delay(100);
}

sealed class Activity
{
    private long _last = Environment.TickCount64;

    public TimeSpan Since => TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _last));

    public void Touch() => Volatile.Write(ref _last, Environment.TickCount64);
}