using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Delivery;
using ToggleForge.Domain.Delivery;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Delivery;

internal sealed class CredentialRepository(DeliveryDbContext context) : ICredentialRepository
{
    public Task LockEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken) =>
        AdvisoryLocks.AcquireAsync(context, AdvisoryLocks.CredentialsOfEnvironment, environmentId, cancellationToken);

    public Task<int> CountActiveAsync(Guid environmentId, CancellationToken cancellationToken) =>
        context.Credentials.CountAsync(c => c.EnvironmentId == environmentId && c.RevokedAt == null, cancellationToken);

    public Task<Credential?> FindActiveByHashAsync(string secretHash, CancellationToken cancellationToken) =>
        context.Credentials
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.SecretHash == secretHash && c.RevokedAt == null, cancellationToken);

    public async Task<IReadOnlyList<Credential>> ListActiveAsync(Guid environmentId, CancellationToken cancellationToken) =>
        await context.Credentials
            .AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId && c.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Credential>> ListRecentlyRevokedAsync(
        Guid environmentId, int limit, CancellationToken cancellationToken) =>
        await context.Credentials
            .AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId && c.RevokedAt != null)
            .OrderByDescending(c => c.RevokedAt)
            .ThenByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<Credential?> FindAsync(Guid environmentId, Guid credentialId, CancellationToken cancellationToken) =>
        context.Credentials.SingleOrDefaultAsync(c => c.Id == credentialId && c.EnvironmentId == environmentId, cancellationToken);

    public void Add(Credential credential) => context.Credentials.Add(credential);

    public Task RecordUseAsync(Guid credentialId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var windowStart = now - Credential.UsePrecision;
        return context.Credentials
            .Where(c => c.Id == credentialId && (c.LastUsedAt == null || c.LastUsedAt < windowStart))
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.LastUsedAt, now), cancellationToken);
    }
}
