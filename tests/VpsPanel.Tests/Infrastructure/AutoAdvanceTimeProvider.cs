namespace VpsPanel.Tests.Infrastructure;

/// <summary>
/// A clock with manual time control. <see cref="Advance"/> moves time forward and fires the timers that are due.
/// Timers no longer than <see cref="InstantTimersUpTo"/> fire immediately and do not move the time.
/// </summary>
internal sealed class AutoAdvanceTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Lock _gate = new();
    private readonly List<AutoTimer> _pending = [];
    private long _ticks;

    public TimeSpan InstantTimersUpTo { get; set; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by)
    {
        List<AutoTimer> due;
        lock (_gate)
        {
            _ticks += by.Ticks;
            due = _pending.Where(t => t.DueTicks <= _ticks).ToList();
            _pending.RemoveAll(due.Contains);
        }

        foreach (var timer in due)
            timer.Fire();
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _start.AddTicks(_ticks);
    }

    public override long GetTimestamp()
    {
        lock (_gate)
            return _ticks;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new AutoTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class AutoTimer(AutoAdvanceTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public long DueTicks { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                owner._pending.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                    return true;

                if (dueTime <= owner.InstantTimersUpTo)
                {
                    ThreadPool.QueueUserWorkItem(_ => Fire());
                    return true;
                }

                DueTicks = owner._ticks + dueTime.Ticks;
                owner._pending.Add(this);
                return true;
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
                owner._pending.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}