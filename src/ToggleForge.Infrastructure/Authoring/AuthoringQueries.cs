using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Authoring;

namespace ToggleForge.Infrastructure.Authoring;

/// <summary>Consultas que o módulo Authoring oferece aos outros módulos; só leitura.</summary>
internal sealed class AuthoringQueries(AuthoringDbContext context, IPublicationRepository publications)
    : IEnvironmentDirectory, IPublishedSnapshots
{
    public async Task<Guid?> FindIdAsync(EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);

        return await context.Environments
            .AsNoTracking()
            .Where(e => e.Key == locator.EnvironmentKey)
            .Join(
                context.Projects.Where(p => p.OrganizationId == locator.OrganizationId && p.Key == locator.ProjectKey),
                environment => environment.ProjectId,
                project => project.Id,
                (environment, _) => (Guid?)environment.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SnapshotView?> FindCurrentAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var environment = await context.Environments
            .AsNoTracking()
            .Where(e => e.Id == environmentId && e.CurrentVersion != null)
            .Join(context.Projects, e => e.ProjectId, p => p.Id, (e, p) => new { ProjectKey = p.Key, e.Key, e.CurrentVersion })
            .SingleOrDefaultAsync(cancellationToken);
        if (environment?.CurrentVersion is not { } version)
        {
            return null;
        }

        var publication = await publications.FindAsync(environmentId, version, cancellationToken)
            ?? throw new InvalidOperationException($"Environment {environmentId} points to missing version {version}.");
        return new SnapshotView(environment.ProjectKey, environment.Key, publication.Version, publication.PublishedAt, publication.Flags);
    }
}
