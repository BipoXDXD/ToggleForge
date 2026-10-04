namespace ToggleForge.Sdk.Tests;

/// <summary>Respostas combinadas, uma por sincronização, e as ETags que o SDK mandou.</summary>
internal sealed class StubTransport : ISnapshotTransport
{
    private readonly Queue<SnapshotResponse> _responses = new();

    public List<string?> SentETags { get; } = [];

    public StubTransport Then(SnapshotResponse response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken)
    {
        SentETags.Add(eTag);
        return Task.FromResult(_responses.Dequeue());
    }
}
