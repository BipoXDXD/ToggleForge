namespace ToggleForge.Sdk.Tests;

/// <summary>Um armazenamento em memória que registra cada gravação e pode simular um disco com defeito.</summary>
internal sealed class MemoryStore : ISnapshotStore
{
    public StoredSnapshot? Stored { get; set; }

    public List<StoredSnapshot> Saves { get; } = [];

    /// <summary>Quando presente, toda leitura e gravação lança esta exceção.</summary>
    public Exception? Failure { get; set; }

    public Task<StoredSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
        Failure is { } failure ? Task.FromException<StoredSnapshot?>(failure) : Task.FromResult(Stored);

    public Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return Task.FromException(failure);
        }

        Saves.Add(snapshot);
        Stored = snapshot;
        return Task.CompletedTask;
    }
}
