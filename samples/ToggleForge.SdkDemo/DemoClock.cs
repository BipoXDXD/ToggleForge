namespace ToggleForge.SdkDemo;

/// <summary>
/// Um relógio que o roteiro adianta: dias offline passam num instante. Os timers nunca disparam, porque quem
/// sincroniza é o roteiro, com <c>RefreshAsync</c>; o laço de fundo do SDK fica parado na primeira espera e não
/// corre contra ele num runner lento ou no debugger.
/// </summary>
internal sealed class DemoClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _ticks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new SilentTimer();

    public void Advance(TimeSpan elapsed)
    {
        _now += elapsed;
        _ticks += elapsed.Ticks;
    }

    private sealed class SilentTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
