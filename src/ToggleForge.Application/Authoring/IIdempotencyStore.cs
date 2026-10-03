namespace ToggleForge.Application.Authoring;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid memberId, IdempotencyKey key, CancellationToken cancellationToken);

    void Add(IdempotencyRecord record);
}
