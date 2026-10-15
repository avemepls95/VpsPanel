using System.Diagnostics;
using VpsPanel.Ssh;

namespace VpsPanel.Tests.Infrastructure;

/// <summary>
/// The fake aws from VpsPanel.FakeAws. Its behavior is set by environment variables, which all child processes share,
/// so the tests that start it live in the "ssm" collection and run one at a time.
/// </summary>
internal static class FakeAws
{
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "aws.exe");

    public static string PidFile { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vpspanel-fake-aws-{Guid.NewGuid():N}.pid");

    public static string TerminatedFile { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vpspanel-fake-aws-{Guid.NewGuid():N}.terminated");

    public static SsmOptions Options(TimeSpan? startTimeout = null, TimeSpan? keepAliveInterval = null) =>
        new(Path, startTimeout ?? TimeSpan.FromSeconds(30), keepAliveInterval ?? TimeSpan.FromMinutes(1));

    public static void UseMode(string mode, TimeSpan? idleTimeout = null)
    {
        Environment.SetEnvironmentVariable("FAKE_AWS_MODE", mode);
        Environment.SetEnvironmentVariable("FAKE_AWS_IDLE_TIMEOUT_MS", idleTimeout?.TotalMilliseconds.ToString("0"));
    }

    public static int LastPid() => int.Parse(File.ReadAllText(PidFile));

    /// <summary>
    /// Waits until some session is terminated through "ssm terminate-session" and returns the terminated session ids.
    /// </summary>
    public static async Task<string?> WaitForTerminatedSessions(TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(TerminatedFile))
                return await File.ReadAllTextAsync(TerminatedFile);
            await Task.Delay(100);
        }
        return null;
    }

    public static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}