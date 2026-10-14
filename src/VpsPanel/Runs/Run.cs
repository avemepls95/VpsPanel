namespace VpsPanel.Runs;

internal enum RunStatus
{
    Running,
    Succeeded,
    Failed,
    Stopped
}

internal sealed record RunStart(string ServerId, string? ProjectKey, string Title, string? Subtitle, string? LockKey, string ConflictMessage);

internal sealed record RunLine(long Id, string Text);

/// <summary>
/// Changed completes on the next change of the run (a new line or the end).
/// </summary>
internal sealed record RunSlice(IReadOnlyList<RunLine> Lines, bool Finished, Task Changed);

internal sealed class Run(string id, RunStart start, DateTimeOffset startedAt)
{
    public const int MaxLines = 10_000;

    private readonly Lock _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _firstId = 1;
    private long _lastId;

    public string Id { get; } = id;
    public string ServerId { get; } = start.ServerId;
    public string? ProjectKey { get; } = start.ProjectKey;
    public string Title { get; } = start.Title;
    public string? Subtitle { get; } = start.Subtitle;
    public DateTimeOffset StartedAt { get; } = startedAt;
    public RunStatus Status { get; private set; } = RunStatus.Running;
    public int? ExitCode { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public CancellationToken Token => _stop.Token;
    public bool StopRequested => _stop.IsCancellationRequested;
    public Task Completed => _completed.Task;

    public long LastLineId
    {
        get
        {
            lock (_gate)
                return _lastId;
        }
    }

    public void Append(string text)
    {
        lock (_gate)
        {
            _lines.Enqueue(text.TrimEnd('\r'));
            _lastId++;
            if (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
                _firstId++;
            }
            Pulse();
        }
    }

    public RunSlice ReadAfter(long lastSeenId)
    {
        lock (_gate)
        {
            var lines = new List<RunLine>();
            if (_firstId > 1 && lastSeenId < _firstId - 1)
                lines.Add(new RunLine(_firstId - 1, $"Lines dropped from the start of the output: {_firstId - 1}"));

            var id = _firstId;
            foreach (var text in _lines)
            {
                if (id > lastSeenId)
                    lines.Add(new RunLine(id, text));
                id++;
            }
            return new RunSlice(lines, Status != RunStatus.Running, _changed.Task);
        }
    }

    public void Stop() => _stop.Cancel();

    public void Finish(RunStatus status, int? exitCode, DateTimeOffset finishedAt)
    {
        lock (_gate)
        {
            Status = status;
            ExitCode = exitCode;
            FinishedAt = finishedAt;
            Pulse();
        }
        _completed.TrySetResult();
    }

    private void Pulse()
    {
        var changed = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }
}