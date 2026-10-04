namespace ToggleForge.Sdk.Tests;

/// <summary>Um armazenamento cuja primeira gravação fica presa até o teste liberar, para sobrepor duas gravações.</summary>
internal sealed class GatedStore : ISnapshotStore
{
    private readonly TaskCompletionSource _firstSaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseFirstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lock = new();
    private int _saves;

    public List<StoredSnapshot> Saves { get; } = [];

    public Task FirstSaveStarted => _firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    public void ReleaseFirstSave() => _releaseFirstSave.SetResult();

    public Task<StoredSnapshot?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<StoredSnapshot?>(null);

    public async Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _saves) == 1)
        {
            _firstSaveStarted.SetResult();
            await _releaseFirstSave.Task;
        }

        lock (_lock)
        {
            Saves.Add(snapshot);
        }
    }
}
