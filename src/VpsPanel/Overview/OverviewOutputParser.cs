using System.Globalization;
using System.Text.Json;

namespace VpsPanel.Overview;

internal static class OverviewOutputParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RawOverview Parse(string output)
    {
        var sections = SplitSections(output);
        var docker = Section(sections, "docker");
        var dockerAvailable = docker.FirstOrDefault() == "available";

        return new RawOverview(
            Hostname: FirstLine(sections, "hostname"),
            Os: FirstLine(sections, "os"),
            UptimeSeconds: ParseDouble(FirstLine(sections, "uptime")),
            Cpus: int.TryParse(FirstLine(sections, "cpus"), out var cpus) ? cpus : null,
            Memory: ParsePair(FirstLine(sections, "mem")),
            Disk: ParsePair(FirstLine(sections, "disk")),
            DockerAvailable: dockerAvailable,
            Containers: dockerAvailable ? ParseDockerLines(string.Join('\n', docker.Skip(1))) : [],
            Timers: ParseTimers(string.Join('\n', Section(sections, "timers"))),
            TimerFiles: Section(sections, "timer-files").Where(l => l.Length > 0).ToHashSet(),
            ServiceResults: ParseServiceResults(Section(sections, "results")));
    }

    public static IReadOnlyList<DockerPsLine> ParseDockerLines(string output) =>
        output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonSerializer.Deserialize<DockerPsLine>(line, Json)!)
            .ToList();

    private static Dictionary<string, List<string>> SplitSections(string output)
    {
        var sections = new Dictionary<string, List<string>>();
        List<string>? current = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                current = [];
                sections[line[2..]] = current;
            }
            else
            {
                current?.Add(line);
            }
        }
        return sections;
    }

    private static List<string> Section(Dictionary<string, List<string>> sections, string name) =>
        sections.TryGetValue(name, out var lines) ? lines : [];

    private static string? FirstLine(Dictionary<string, List<string>> sections, string name)
    {
        var line = Section(sections, name).FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
        return string.IsNullOrEmpty(line) ? null : line;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static (long Total, long Used)? ParsePair(string? value)
    {
        var parts = value?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: 2 } && long.TryParse(parts[0], out var total) && long.TryParse(parts[1], out var used)
            ? (total, used)
            : null;
    }

    private static IReadOnlyList<TimerLine> ParseTimers(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return document.RootElement.EnumerateArray()
                .Select(e => new TimerLine(
                    e.GetProperty("unit").GetString() ?? "",
                    e.TryGetProperty("activates", out var activates) ? activates.GetString() ?? "" : "",
                    Number(e, "next"),
                    Number(e, "last")))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    private static Dictionary<string, string> ParseServiceResults(IEnumerable<string> lines)
    {
        var results = new Dictionary<string, string>();
        string? id = null;
        string? result = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("Id=", StringComparison.Ordinal))
                id = line[3..];
            else if (line.StartsWith("Result=", StringComparison.Ordinal))
                result = line[7..];

            if (id is not null && result is not null)
            {
                results[id] = result;
                id = null;
                result = null;
            }
        }
        return results;
    }
}