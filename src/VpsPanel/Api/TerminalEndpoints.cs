using VpsPanel.Security;
using VpsPanel.ShellColors;
using VpsPanel.Terminal;

namespace VpsPanel.Api;

internal static class TerminalEndpoints
{
    public static void MapTerminalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/servers/{id}/terminal", Open);

        var colors = app.MapGroup("/api/servers/{id}/terminal/colors");
        colors.MapGet("", (string id, ShellColorsService service, CancellationToken ct) => service.Get(id, ct));
        colors.MapPost("/enable", (string id, ShellColorsService service, CancellationToken ct) => service.Enable(id, ct));
        colors.MapPost("/disable", (string id, ShellColorsService service, CancellationToken ct) => service.Disable(id, ct));
    }

    private static async Task<IResult> Open(string id, int? cols, int? rows, HttpContext context, TerminalBridge bridge)
    {
        if (!context.WebSockets.IsWebSocketRequest)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A WebSocket connection is expected.");
        if (!SameOrigin.Check(context.Request))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "The request came from another site.");

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await bridge.Run(socket, id, cols ?? 80, rows ?? 24, context.RequestAborted);
        return Results.Empty;
    }
}