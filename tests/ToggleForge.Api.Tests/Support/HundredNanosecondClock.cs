namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Relógio com a resolução do Linux (100 ns), mais fina que a do Postgres (1 µs). No macOS o relógio do
/// sistema já vem em microssegundos e esconderia qualquer diferença entre o instante na memória e o gravado.
/// </summary>
public sealed class HundredNanosecondClock : TimeProvider
{
    private const long OddTicks = 7;

    public override DateTimeOffset GetUtcNow()
    {
        var now = System.GetUtcNow();
        return now.AddTicks(OddTicks - (now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
