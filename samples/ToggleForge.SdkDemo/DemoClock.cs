namespace ToggleForge.SdkDemo;

/// <summary>Um relógio que o roteiro adianta: oito dias offline passam num instante.</summary>
internal sealed class DemoClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan elapsed)
    {
        _now += elapsed;
        _ticks += elapsed.Ticks;
    }
}
