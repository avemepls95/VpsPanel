namespace VpsPanel.Reboot;

internal sealed record RebootTiming(TimeSpan PollInterval, TimeSpan ConnectTimeout, int MaxAttempts)
{
    public static RebootTiming Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), 60);
}