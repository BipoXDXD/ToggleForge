using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class ProjectTests
{
    [Fact]
    public void New_project_comes_with_dev_and_prod_environments_and_empty_drafts()
    {
        var organizationId = Guid.NewGuid();

        var (project, environments) = Create(organizationId, existingProjects: 0);

        Assert.Equal(organizationId, project.OrganizationId);
        Assert.Equal(["dev", "prod"], environments.Select(environment => environment.Key.Value));
        Assert.All(environments, environment =>
        {
            Assert.Equal(project.Id, environment.ProjectId);
            Assert.Empty(environment.Flags);
            Assert.Equal(1, environment.DraftRevision);
            Assert.Null(environment.CurrentVersion);
        });
    }

    [Fact]
    public void Organization_accepts_its_20th_project_but_not_a_21st()
    {
        Create(Guid.NewGuid(), existingProjects: 19);

        var created = Project.Create(Guid.NewGuid(), ProjectKey.Parse("studio-game"), Names.Of("Studio Game"), existingProjects: 20);

        Assert.False(created.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.ProjectLimitReached, failure);
    }

    private static NewProject Create(Guid organizationId, int existingProjects)
    {
        var created = Project.Create(organizationId, ProjectKey.Parse("studio-game"), Names.Of("Studio Game"), existingProjects);
        Assert.True(created.TryGetValue(out var newProject, out var failure), failure?.Detail);
        return newProject;
    }
}
