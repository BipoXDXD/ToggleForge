using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

public sealed class Project
{
    public const int MaxPerOrganization = 20;

    private Project(Guid id, Guid organizationId, ProjectKey key, DisplayName name)
    {
        Id = id;
        OrganizationId = organizationId;
        Key = key;
        Name = name;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public ProjectKey Key { get; }

    public DisplayName Name { get; }

    /// <summary>
    /// Todo projeto nasce com os ambientes dev e prod, cada um com o rascunho vazio.
    /// <paramref name="existingProjects"/> é a contagem atual da organização, lida sob lock.
    /// </summary>
    public static Result<NewProject> Create(Guid organizationId, ProjectKey key, DisplayName name, int existingProjects)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(name);

        if (existingProjects >= MaxPerOrganization)
        {
            return AuthoringFailures.ProjectLimitReached;
        }

        var project = new Project(Guid.NewGuid(), organizationId, key, name);
        ProjectEnvironment[] environments =
        [
            ProjectEnvironment.Create(project.Id, EnvironmentKey.Parse("dev"), DefaultName("Development")),
            ProjectEnvironment.Create(project.Id, EnvironmentKey.Parse("prod"), DefaultName("Production")),
        ];
        return new NewProject(project, environments);
    }

    private static DisplayName DefaultName(string value) =>
        DisplayName.TryParse(value, out var name) ? name : throw new InvalidOperationException($"'{value}' is not a valid name.");
}
