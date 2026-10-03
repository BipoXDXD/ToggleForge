using ToggleForge.Application.Access;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

public sealed class ProjectService(
    MemberAccess access,
    IProjectRepository projects,
    IEnvironmentRepository environments,
    IPublicationRepository publications,
    IAuthoringUnitOfWork unitOfWork)
{
    public async Task<Result<ProjectView>> CreateAsync(
        Guid memberId, Guid organizationId, ProjectKey key, DisplayName name, CancellationToken cancellationToken)
    {
        if (!(await access.FindRoleAsync(memberId, organizationId, cancellationToken)).TryGetValue(out var role, out var failure))
        {
            return failure;
        }

        if (MemberAccess.Require(role, RolePermissions.CanAdminister) is { } forbidden)
        {
            return forbidden;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await projects.LockOrganizationAsync(organizationId, cancellationToken);
        if (await projects.FindAsync(organizationId, key, cancellationToken) is not null)
        {
            return AuthoringFailures.ProjectKeyTaken;
        }

        var existingProjects = await projects.CountAsync(organizationId, cancellationToken);
        if (!Project.Create(organizationId, key, name, existingProjects).TryGetValue(out var created, out failure))
        {
            return failure;
        }

        projects.Add(created.Project);
        foreach (var environment in created.Environments)
        {
            environments.Add(environment);
        }

        await unitOfWork.SaveOrFailAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(created.Project, created.Environments, cancellationToken);
    }

    public async Task<Result<ProjectView>> GetAsync(
        Guid memberId, Guid organizationId, ProjectKey key, CancellationToken cancellationToken)
    {
        if (!(await access.FindRoleAsync(memberId, organizationId, cancellationToken)).TryGetValue(out _, out var failure))
        {
            return failure;
        }

        if (await projects.FindAsync(organizationId, key, cancellationToken) is not { } project)
        {
            return AccessFailures.NotFound;
        }

        return await DescribeAsync(project, await environments.ListAsync(project.Id, cancellationToken), cancellationToken);
    }

    private async Task<ProjectView> DescribeAsync(
        Project project, IReadOnlyList<ProjectEnvironment> projectEnvironments, CancellationToken cancellationToken)
    {
        var current = await publications.FindCurrentByEnvironmentAsync(project.Id, cancellationToken);
        var views = projectEnvironments
            .OrderBy(environment => environment.Key.Value, StringComparer.Ordinal)
            .Select(environment => new EnvironmentView(
                environment.Key,
                environment.Name,
                environment.CurrentVersion,
                environment.DraftRevision,
                HasUnpublishedChanges: environment.PendingChanges(current.GetValueOrDefault(environment.Id)).Count > 0))
            .ToList();
        return new ProjectView(project.Key, project.Name, views);
    }
}
