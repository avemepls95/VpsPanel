using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpsPanel.Runs;

internal static class RunEventStream
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async IAsyncEnumerable<SseItem<string>> Read(Run run, long lastSeen, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            var slice = run.ReadAfter(lastSeen);
            foreach (var line in slice.Lines)
            {
                yield return new SseItem<string>(line.Text, "line") { EventId = line.Id.ToString(CultureInfo.InvariantCulture) };
                lastSeen = line.Id;
            }

            if (slice.Finished)
            {
                yield return new SseItem<string>(JsonSerializer.Serialize(RunEnd.From(run), Json), "end");
                yield break;
            }

            await slice.Changed.WaitAsync(ct);
        }
    }

    private sealed record RunEnd(RunStatus Status, int? ExitCode, long DurationMs)
    {
        public static RunEnd From(Run run) =>
            new(run.Status, run.ExitCode, (long)((run.FinishedAt ?? run.StartedAt) - run.StartedAt).TotalMilliseconds);
    }
}