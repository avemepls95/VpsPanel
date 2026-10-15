namespace VpsPanel.Ssh;

internal sealed record SsmOptions(string AwsCliPath, TimeSpan StartTimeout, TimeSpan KeepAliveInterval)
{
    public static SsmOptions Default { get; } = new("aws", TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
}