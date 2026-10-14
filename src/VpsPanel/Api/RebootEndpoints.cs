using VpsPanel.Reboot;
using VpsPanel.Runs;

namespace VpsPanel.Api;

internal static class RebootEndpoints
{
    public static void MapRebootEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/servers/{id}/reboot", (string id, RebootService reboot) => new RunStarted(reboot.Start(id).Id));
}