using VpsPanel.Overview;
using VpsPanel.Servers;
using VpsPanel.Settings;

namespace VpsPanel.Api;

internal static class ServerEndpoints
{
    public static void MapServerEndpoints(this IEndpointRouteBuilder app)
    {
        var servers = app.MapGroup("/api/servers");
        servers.MapGet("", (ISettingsStore settings) => settings.Read().Servers.Select(ServerView.From).ToList());
        servers.MapPost("", Create);
        servers.MapPut("/{id}", Update);
        servers.MapDelete("/{id}", Delete);
        servers.MapGet("/{id}/overview", (string id, OverviewService overview, CancellationToken ct) => overview.Get(id, ct));
        servers.MapPost("/{id}/provider/refresh", RefreshProvider);
        servers.MapPost("/{id}/host-key/reset", ResetHostKey);
    }

    private static async Task<ServerView> Create(ServerRequest request, ISettingsStore settings, IProviderLookup providers, CancellationToken ct)
    {
        var valid = request.Validate();
        ProviderInfo? provider;
        if (valid.ProviderName is not null)
            provider = new ProviderInfo(valid.ProviderName, true);
        else
            provider = await providers.Find(valid.Host, ct) is { } found ? new ProviderInfo(found, false) : null;

        var server = new ServerSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = valid.Name,
            Host = valid.Host,
            Port = valid.Port,
            User = valid.User,
            KeyPath = valid.KeyPath,
            Provider = provider
        };
        settings.Update(s => s.Servers.Add(server));
        return ServerView.From(server);
    }

    private static async Task<ServerView> Update(string id, ServerRequest request, ISettingsStore settings, IProviderLookup providers, CancellationToken ct)
    {
        var valid = request.Validate();
        var current = settings.Read().GetServer(id);
        var addressChanged = current.Host != valid.Host || current.Port != valid.Port;
        var provider = await ResolveProvider(current, valid, addressChanged, providers, ct);

        return settings.Update(s =>
        {
            var server = s.GetServer(id);
            server.Name = valid.Name;
            server.Host = valid.Host;
            server.Port = valid.Port;
            server.User = valid.User;
            server.KeyPath = valid.KeyPath;
            server.Provider = provider;
            if (addressChanged)
                server.HostKeyFingerprint = null;
            return ServerView.From(server);
        });
    }

    private static async Task<ProviderInfo?> ResolveProvider(
        ServerSettings current, ValidServerRequest valid, bool addressChanged, IProviderLookup providers, CancellationToken ct)
    {
        if (valid.ProviderName != current.Provider?.Name)
            return valid.ProviderName is null ? null : new ProviderInfo(valid.ProviderName, true);
        if (addressChanged && current.Provider?.IsManual != true)
            return await providers.Find(valid.Host, ct) is { } found ? new ProviderInfo(found, false) : null;
        return current.Provider;
    }

    private static IResult Delete(string id, ISettingsStore settings)
    {
        settings.Update(s => s.Servers.Remove(s.GetServer(id)));
        return Results.NoContent();
    }

    private static async Task<ServerView> RefreshProvider(string id, ISettingsStore settings, IProviderLookup providers, CancellationToken ct)
    {
        var found = await providers.Find(settings.Read().GetServer(id).Host, ct);
        return settings.Update(s =>
        {
            var server = s.GetServer(id);
            server.Provider = found is null ? null : new ProviderInfo(found, false);
            return ServerView.From(server);
        });
    }

    private static ServerView ResetHostKey(string id, ISettingsStore settings) =>
        settings.Update(s =>
        {
            var server = s.GetServer(id);
            server.HostKeyFingerprint = null;
            return ServerView.From(server);
        });
}