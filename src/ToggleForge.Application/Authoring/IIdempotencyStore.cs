namespace ToggleForge.Application.Authoring;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid memberId, IdempotencyKey key, CancellationToken cancellationToken);

    void Add(IdempotencyRecord record);

    /// <summary>Apaga, num só comando, os registros criados antes de <paramref name="cutoff"/>; devolve quantos.</summary>
    Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
