namespace ToggleForge.Sdk.Tests;

/// <summary>Uma sincronização que só termina quando o teste responde ou quando é cancelada.</summary>
internal sealed class HangingTransport : ISnapshotTransport
{
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SnapshotResponse> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Arrived => _arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    public bool WasCancelled { get; private set; }

    public void Answer(SnapshotResponse response) => _answer.SetResult(response);

    public async Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            WasCancelled = true;
            _answer.TrySetCanceled(cancellationToken);
        });
        _arrived.TrySetResult();
        return await _answer.Task;
    }
}
