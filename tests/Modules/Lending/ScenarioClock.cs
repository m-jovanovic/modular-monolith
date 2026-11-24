namespace EquipmentLibrary.Lending.Tests;

internal sealed class ScenarioClock : TimeProvider
{
    private long _utcTicks;

    public override DateTimeOffset GetUtcNow()
    {
        long ticks = Interlocked.Read(ref _utcTicks);
        return ticks == 0 ? System.GetUtcNow() : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public void Set(DateTimeOffset instant) => Interlocked.Exchange(ref _utcTicks, instant.UtcTicks);
    public void Reset() => Interlocked.Exchange(ref _utcTicks, 0);
}
