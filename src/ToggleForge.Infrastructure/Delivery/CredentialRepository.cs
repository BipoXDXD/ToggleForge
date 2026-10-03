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

    public void Add(Credential credential) => context.Credentials.Add(credential);
}
