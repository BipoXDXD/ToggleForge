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
        return Describe(created.Project, created.Environments, current: new Dictionary<Guid, Publication>());
    }

    public async Task<Result<IReadOnlyList<ProjectView>>> ListAsync(
        Guid memberId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (await access.RequireMembershipAsync(memberId, organizationId, cancellationToken) is { } failure)
        {
            return failure;
        }

        return await DescribeAsync(await projects.ListAsync(organizationId, cancellationToken), cancellationToken);
    }

    public async Task<Result<ProjectView>> GetAsync(
        Guid memberId, Guid organizationId, ProjectKey key, CancellationToken cancellationToken)
    {
        if (await access.RequireMembershipAsync(memberId, organizationId, cancellationToken) is { } failure)
        {
            return failure;
        }

        if (await projects.FindAsync(organizationId, key, cancellationToken) is not { } project)
        {
            return AccessFailures.NotFound;
        }

        return (await DescribeAsync([project], cancellationToken))[0];
    }

    public async Task<Result<EnvironmentView>> CreateEnvironmentAsync(
        Guid memberId, Guid organizationId, ProjectKey projectKey, EnvironmentKey key, DisplayName name, CancellationToken cancellationToken)
    {
        if (!(await access.FindRoleAsync(memberId, organizationId, cancellationToken)).TryGetValue(out var role, out var failure))
        {
            return failure;
        }

        if (await projects.FindAsync(organizationId, projectKey, cancellationToken) is not { } project)
        {
            return AccessFailures.NotFound;
        }

        if (MemberAccess.Require(role, RolePermissions.CanAdminister) is { } forbidden)
        {
            return forbidden;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await projects.LockEnvironmentsAsync(project.Id, cancellationToken);
        var existingKeys = await environments.ListKeysAsync(project.Id, cancellationToken);
        if (!project.CreateEnvironment(key, name, existingKeys).TryGetValue(out var environment, out failure))
        {
            return failure;
        }

        environments.Add(environment);
        await unitOfWork.SaveOrFailAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return EnvironmentView.Of(environment, current: null);
    }

    public async Task<Result<EnvironmentView>> GetEnvironmentAsync(
        Guid memberId, EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (await access.RequireMembershipAsync(memberId, locator.OrganizationId, cancellationToken) is { } failure)
        {
            return failure;
        }

        if (await environments.FindAsync(locator, cancellationToken) is not { } environment)
        {
            return AccessFailures.NotFound;
        }

        return EnvironmentView.Of(environment, await publications.FindAsync(environment, cancellationToken));
    }

    /// <summary>Uma consulta para os ambientes e outra para as publicações em vigor, quantos forem os projetos.</summary>
    private async Task<List<ProjectView>> DescribeAsync(
        IReadOnlyList<Project> projectsToDescribe, CancellationToken cancellationToken)
    {
        var projectIds = projectsToDescribe.Select(project => project.Id).ToList();
        var environmentsByProject = (await environments.ListAsync(projectIds, cancellationToken)).ToLookup(environment => environment.ProjectId);
        var current = await publications.FindCurrentByEnvironmentAsync(projectIds, cancellationToken);
        return projectsToDescribe.Select(project => Describe(project, environmentsByProject[project.Id], current)).ToList();
    }

    private static ProjectView Describe(
        Project project, IEnumerable<ProjectEnvironment> projectEnvironments, IReadOnlyDictionary<Guid, Publication> current) =>
        new(
            project.Key,
            project.Name,
            projectEnvironments
                .OrderBy(environment => environment.Key.Value, StringComparer.Ordinal)
                .Select(environment => EnvironmentView.Of(environment, current.GetValueOrDefault(environment.Id)))
                .ToList());
}
