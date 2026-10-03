namespace ToggleForge.Application;

internal static class ClockExtensions
{
    /// <summary>
    /// O agora na resolução do Postgres (1 µs). O relógio do Linux tem 100 ns: sem truncar, a resposta original
    /// mostraria um instante que nenhuma releitura (replay, histórico) devolve.
    /// </summary>
    public static DateTimeOffset GetUtcNowInMicroseconds(this TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
