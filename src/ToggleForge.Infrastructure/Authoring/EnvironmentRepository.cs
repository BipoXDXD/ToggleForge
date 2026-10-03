using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class EnvironmentRepository(AuthoringDbContext context) : IEnvironmentRepository
{
    public async Task<IReadOnlyList<ProjectEnvironment>> ListAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await context.Environments
            .Where(e => projectIds.Contains(e.ProjectId))
            .OrderBy(e => e.Key)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EnvironmentKey>> ListKeysAsync(Guid projectId, CancellationToken cancellationToken) =>
        await context.Environments
            .Where(e => e.ProjectId == projectId)
            .Select(e => e.Key)
            .ToListAsync(cancellationToken);

    public Task<ProjectEnvironment?> FindAsync(EnvironmentLocator locator, CancellationToken cancellationToken) =>
        Located(locator).SingleOrDefaultAsync(cancellationToken);

    public async Task<ProjectEnvironment?> FindForUpdateAsync(EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Locking an environment requires an open transaction.");
        }

        var id = await Located(locator).Select(e => (Guid?)e.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is null)
        {
            return null;
        }

        // FOR NO KEY UPDATE é o lock que o próprio UPDATE do ambiente tomaria; a leitura seguinte já vê o
        // estado que o escritor anterior deixou (AoP cap. 36).
        await context.Database.ExecuteSqlAsync(
            $"select 1 from authoring.environments where id = {id} for no key update", cancellationToken);
        return await context.Environments.SingleAsync(e => e.Id == id, cancellationToken);
    }

    public void Add(ProjectEnvironment environment) => context.Environments.Add(environment);

    private IQueryable<ProjectEnvironment> Located(EnvironmentLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);

        return context.Environments
            .Join(
                context.Projects.Where(p => p.OrganizationId == locator.OrganizationId && p.Key == locator.ProjectKey),
                environment => environment.ProjectId,
                project => project.Id,
                (environment, _) => environment)
            .Where(e => e.Key == locator.EnvironmentKey);
    }
}
