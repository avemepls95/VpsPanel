using System.Collections.Concurrent;

namespace VpsPanel.Runs;

internal sealed class RunManager(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, Run> _runs = new();
    private readonly ConcurrentDictionary<string, string> _locks = new();

    public Run Start(RunStart start, Func<Run, Task<int?>> work)
    {
        var run = new Run(Guid.NewGuid().ToString("N"), start, time.GetUtcNow());
        if (start.LockKey is { } key && !_locks.TryAdd(key, run.Id))
            throw new PanelException(409, start.ConflictMessage);

        _runs[run.Id] = run;
        _ = Execute(run, start.LockKey, work);
        return run;
    }

    public Run Get(string id) =>
        _runs.TryGetValue(id, out var run) ? run : throw new PanelException(404, "Run not found.");

    public IReadOnlyList<Run> List() => _runs.Values.OrderByDescending(r => r.StartedAt).ToList();

    private async Task Execute(Run run, string? lockKey, Func<Run, Task<int?>> work)
    {
        RunStatus status;
        int? exitCode = null;
        try
        {
            exitCode = await Task.Run(() => work(run));
            status = run.StopRequested ? RunStatus.Stopped
                : exitCode == 0 ? RunStatus.Succeeded
                : RunStatus.Failed;
        }
        catch (Exception) when (run.StopRequested)
        {
            status = RunStatus.Stopped;
        }
        catch (Exception e)
        {
            run.Append($"Error: {e.Message}");
            status = RunStatus.Failed;
        }

        // The lock is released before Finish so that the next run is accepted right after Completed.
        if (lockKey is not null)
            _locks.TryRemove(lockKey, out _);
        run.Finish(status, exitCode, time.GetUtcNow());
    }
}