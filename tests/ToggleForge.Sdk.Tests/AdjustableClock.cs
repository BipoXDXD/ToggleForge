namespace ToggleForge.Sdk.Tests;

/// <summary>
/// Um relógio de parede que o teste pode atrasar, como um jogador mexendo no relógio do celular, enquanto o
/// monotônico só anda para frente. O <c>FakeTimeProvider</c> não volta no tempo.
/// </summary>
internal sealed class AdjustableClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _ticks;

    /// <summary>O tempo passa de verdade: os dois relógios andam.</summary>
    public void Advance(TimeSpan elapsed)
    {
        _now += elapsed;
        _ticks += elapsed.Ticks;
    }

    /// <summary>Só o relógio de parede muda, para frente ou para trás.</summary>
    public void SetWallClock(DateTimeOffset now) => _now = now;
}
