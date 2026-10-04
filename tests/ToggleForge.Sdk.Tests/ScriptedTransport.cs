namespace ToggleForge.Sdk.Tests;

/// <summary>
/// Respostas por ordem de chegada, que o teste pode segurar para criar sincronizações sobrepostas: a que chega
/// primeiro pode responder por último.
/// </summary>
internal sealed class ScriptedTransport : ISnapshotTransport
{
    private readonly Queue<TaskCompletionSource<SnapshotResponse>> _answers = new();

    public void Next(SnapshotResponse response)
    {
        var answer = new TaskCompletionSource<SnapshotResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        answer.SetResult(response);
        _answers.Enqueue(answer);
    }

    /// <summary>A próxima sincronização fica esperando até o teste completar a resposta.</summary>
    public TaskCompletionSource<SnapshotResponse> Hold()
    {
        var answer = new TaskCompletionSource<SnapshotResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answers.Enqueue(answer);
        return answer;
    }

    public Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken) => _answers.Dequeue().Task;
}
