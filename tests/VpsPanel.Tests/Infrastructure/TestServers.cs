using VpsPanel.Settings;

namespace VpsPanel.Tests.Infrastructure;

internal static class TestServers
{
    public static ServerSettings Create(string name, string host = "192.0.2.10") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Host = host,
        Port = 22,
        User = "root",
        KeyPath = @"C:\keys\test_ed25519"
    };
}