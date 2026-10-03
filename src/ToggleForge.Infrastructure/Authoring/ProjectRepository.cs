using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class ProjectRepository(AuthoringDbContext context) : IProjectRepository
{
    public Task LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        AdvisoryLocks.AcquireAsync(context, AdvisoryLocks.ProjectsOfOrganization, organizationId, cancellationToken);

    public Task<int> CountAsync(Guid organizationId, CancellationToken cancellationToken) =>
        context.Projects.CountAsync(p => p.OrganizationId == organizationId, cancellationToken);

    public Task<Project?> FindAsync(Guid organizationId, ProjectKey key, CancellationToken cancellationToken) =>
        context.Projects.SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Key == key, cancellationToken);

    public void Add(Project project) => context.Projects.Add(project);
}
