using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VpsPanel.Settings;

namespace VpsPanel.Ssh;

/// <summary>
/// The "aws ssm start-session" process that forwards a random local port to a port of the instance.
/// </summary>
internal sealed partial class SsmTunnel : IAsyncDisposable
{
    private const string PluginMissingMarker = "SessionManagerPlugin is not found";

    private readonly Process _process;
    private readonly Task _output;
    private readonly Task _errors;
    private readonly SsmSettings _ssm;
    private readonly SsmOptions _options;
    private readonly string _sessionId;

    private SsmTunnel(Process process, PortOpened opened, Task output, Task errors, SsmSettings ssm, SsmOptions options)
    {
        _process = process;
        LocalPort = opened.Port;
        _sessionId = opened.SessionId;
        _output = output;
        _errors = errors;
        _ssm = ssm;
        _options = options;
    }

    public int LocalPort { get; }

    public static IReadOnlyList<string> Arguments(SsmSettings ssm, int remotePort) =>
    [
        ..ProfileAndRegion(ssm),
        "ssm", "start-session", "--target", ssm.InstanceId,
        "--document-name", "AWS-StartPortForwardingSession",
        "--parameters", $"portNumber={remotePort},localPortNumber=0"
    ];

    public static IReadOnlyList<string> TerminateArguments(SsmSettings ssm, string sessionId) =>
        [..ProfileAndRegion(ssm), "ssm", "terminate-session", "--session-id", sessionId];

    public static async Task<SsmTunnel> Start(SsmSettings ssm, int remotePort, SsmOptions options, CancellationToken ct)
    {
        var process = StartProcess(options.AwsCliPath, Arguments(ssm, remotePort));
        var lastError = new LastLine();
        var lastOutput = new LastLine();
        var errors = Drain(process.StandardError, lastError);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(options.StartTimeout);

            // Reading a process pipe does not react to a token, so the wait is limited from outside.
            var opened = await ReadPortOpened(process.StandardOutput, lastOutput).WaitAsync(limit.Token);
            if (opened is null)
            {
                await process.WaitForExitAsync(limit.Token);
                await errors.WaitAsync(limit.Token);
                throw new SsmUnavailableException(DescribeExit(process.ExitCode, lastError.Value, lastOutput.Value));
            }
            return new SsmTunnel(process, opened, Drain(process.StandardOutput, null), errors, ssm, options);
        }
        catch (Exception e)
        {
            await Stop(process);
            await errors;
            process.Dispose();
            if (e is OperationCanceledException && !ct.IsCancellationRequested)
                throw new SsmUnavailableException($"the session did not start within {options.StartTimeout.TotalSeconds:0} s.");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Stop(_process);
        await Task.WhenAll(_output, _errors);
        _process.Dispose();
        Terminate();
    }

    // The killed plugin cannot end the session itself, so it is ended through the API without waiting for the result.
    private void Terminate()
    {
        var process = StartProcess(_options.AwsCliPath, TerminateArguments(_ssm, _sessionId));
        _ = Task.WhenAll(Drain(process.StandardOutput, null), Drain(process.StandardError, null), process.WaitForExitAsync())
            .ContinueWith(_ => process.Dispose(), TaskScheduler.Default);
    }

    private static List<string> ProfileAndRegion(SsmSettings ssm)
    {
        var arguments = new List<string>();
        if (ssm.Profile is not null)
            arguments.AddRange(["--profile", ssm.Profile]);
        if (ssm.Region is not null)
            arguments.AddRange(["--region", ssm.Region]);
        return arguments;
    }

    private static Process StartProcess(string awsCliPath, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(awsCliPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        try
        {
            return Process.Start(info)!;
        }
        catch (Win32Exception)
        {
            throw new SsmUnavailableException("AWS CLI was not found. Install AWS CLI v2 and check that \"aws --version\" works in a terminal.");
        }
    }

    private static async Task<PortOpened?> ReadPortOpened(StreamReader output, LastLine last)
    {
        while (await output.ReadLineAsync() is { } line)
        {
            last.Set(line);
            if (PortOpenedLine().Match(line) is { Success: true } match)
                return new PortOpened(int.Parse(match.Groups[1].Value), match.Groups[2].Value);
        }
        return null;
    }

    // The plugin stops writing when nobody reads a full pipe, so its output is read to the end.
    private static async Task Drain(StreamReader reader, LastLine? last)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
                last?.Set(line);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    // aws starts session-manager-plugin as a child process.
    private static async Task Stop(Process process)
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static string DescribeExit(int exitCode, string? error, string? output)
    {
        if (error?.Contains(PluginMissingMarker) == true || output?.Contains(PluginMissingMarker) == true)
            return "the Session Manager plugin for AWS CLI is not installed. See https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html";
        return error ?? output ?? $"aws exited with code {exitCode}.";
    }

    [GeneratedRegex(@"^Port (\d+) opened for sessionId (\S+)\.$")]
    private static partial Regex PortOpenedLine();

    private sealed record PortOpened(int Port, string SessionId);

    private sealed class LastLine
    {
        private volatile string? _value;

        public string? Value => _value;

        public void Set(string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                _value = line.Trim();
        }
    }
}