using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Authoring;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class IdempotencyStore(AuthoringDbContext context) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(Guid memberId, IdempotencyKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        return context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.MemberId == memberId && r.Key == key.Value, cancellationToken);
    }

    public void Add(IdempotencyRecord record) => context.IdempotencyRecords.Add(record);

    public Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        context.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}
