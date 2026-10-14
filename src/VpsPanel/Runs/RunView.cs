namespace VpsPanel.Runs;

internal sealed record RunView(
    string Id,
    string ServerId,
    string? ProjectKey,
    string Title,
    string? Subtitle,
    RunStatus Status,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long LastLineId)
{
    public static RunView From(Run run) =>
        new(run.Id, run.ServerId, run.ProjectKey, run.Title, run.Subtitle, run.Status, run.ExitCode, run.StartedAt,
            run.FinishedAt, run.LastLineId);
}

internal sealed record RunStarted(string RunId);