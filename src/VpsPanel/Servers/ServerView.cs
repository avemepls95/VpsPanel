using VpsPanel.Settings;

namespace VpsPanel.Servers;

internal sealed record ServerView(
    string Id,
    string Name,
    string Host,
    int Port,
    string User,
    string KeyPath,
    ProviderInfo? Provider,
    string? HostKeyFingerprint)
{
    public static ServerView From(ServerSettings server) =>
        new(server.Id, server.Name, server.Host, server.Port, server.User, server.KeyPath, server.Provider, server.HostKeyFingerprint);
}