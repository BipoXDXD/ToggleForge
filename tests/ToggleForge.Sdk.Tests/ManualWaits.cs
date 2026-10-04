using System.Threading.Channels;

namespace ToggleForge.Sdk.Tests;

/// <summary>
/// A espera do laço sob controle do teste: cada espera pedida fica registrada e só termina quando o teste manda,
/// sem relógio nem <c>sleep</c>.
/// </summary>
internal sealed class ManualWaits
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(5);

    private readonly Channel<(TimeSpan Delay, TaskCompletionSource Done)> _waits = Channel.CreateUnbounded<(TimeSpan, TaskCompletionSource)>();
    private TaskCompletionSource? _pending;
    private int _requested;

    /// <summary>Quantas esperas o laço pediu até agora, inclusive as que o teste ainda não leu.</summary>
    public int Requested => Volatile.Read(ref _requested);

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requested);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        _waits.Writer.TryWrite((delay, done));
        return done.Task;
    }

    /// <summary>A próxima espera que o laço pediu; falha o teste se o laço não pedir nenhuma.</summary>
    public async Task<TimeSpan> NextAsync()
    {
        using var patience = new CancellationTokenSource(_patience);
        var (delay, done) = await _waits.Reader.ReadAsync(patience.Token);
        _pending = done;
        return delay;
    }

    public void Elapse() => (_pending ?? throw new InvalidOperationException("No wait has been requested.")).SetResult();

    public bool IsWaiting => _pending is { Task.IsCompleted: false };
}
