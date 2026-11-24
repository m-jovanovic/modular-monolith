namespace EquipmentLibrary.Lending.Tests;

internal sealed class ScenarioClock : TimeProvider
{
    private readonly TaskCompletionSource<ITimer> _expiryTimer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _utcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != TimeSpan.FromMinutes(1))
        {
            return base.CreateTimer(callback, state, dueTime, period);
        }

        ITimer timer = base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _expiryTimer.SetResult(timer);
        return timer;
    }

    public async Task TickWorkerAsync()
    {
        ITimer timer = await _expiryTimer.Task.WaitAsync(TimeSpan.FromSeconds(10));
        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    public override DateTimeOffset GetUtcNow()
    {
        long ticks = Interlocked.Read(ref _utcTicks);
        return ticks == 0 ? System.GetUtcNow() : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public void Set(DateTimeOffset instant) => Interlocked.Exchange(ref _utcTicks, instant.UtcTicks);
    public void Reset() => Interlocked.Exchange(ref _utcTicks, 0);
}
