using VpsPanel.Runs;

namespace VpsPanel.Api;

internal static class RunEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/runs", (RunManager runs) => runs.List().Select(RunView.From).ToList());
        app.MapGet("/api/runs/{id}/stream", Stream);
        app.MapPost("/api/runs/{id}/stop", (string id, RunManager runs) =>
        {
            runs.Get(id).Stop();
            return Results.NoContent();
        });
    }

    private static IResult Stream(string id, HttpRequest request, RunManager runs, CancellationToken ct)
    {
        var run = runs.Get(id);
        var lastSeen = long.TryParse(request.Headers["Last-Event-ID"], out var value) ? value : 0;

        // 204 stops EventSource from reconnecting to a finished run.
        if (run.Status != RunStatus.Running && lastSeen >= run.LastLineId)
            return Results.NoContent();
        return TypedResults.ServerSentEvents(RunEventStream.Read(run, lastSeen, ct));
    }
}