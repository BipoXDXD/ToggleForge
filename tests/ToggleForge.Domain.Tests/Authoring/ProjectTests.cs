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

    [Fact]
    public void New_environment_belongs_to_the_project_with_an_empty_draft()
    {
        var (project, _) = Create(Guid.NewGuid(), existingProjects: 0);

        var created = project.CreateEnvironment(EnvironmentKey.Parse("staging"), Names.Of("Staging"), Keys("dev", "prod"));

        Assert.True(created.TryGetValue(out var environment, out var failure), failure?.Detail);
        Assert.Equal(project.Id, environment.ProjectId);
        Assert.Equal("staging", environment.Key.Value);
        Assert.Equal("Staging", environment.Name.Value);
        Assert.Empty(environment.Flags);
        Assert.Equal(1, environment.DraftRevision);
        Assert.Null(environment.CurrentVersion);
    }

    [Fact]
    public void Environment_key_is_unique_in_the_project()
    {
        var (project, _) = Create(Guid.NewGuid(), existingProjects: 0);

        var created = project.CreateEnvironment(EnvironmentKey.Parse("prod"), Names.Of("Production 2"), Keys("dev", "prod"));

        Assert.False(created.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.EnvironmentKeyTaken, failure);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void Project_accepts_its_10th_environment_but_not_an_11th(int existing, bool accepted)
    {
        var (project, _) = Create(Guid.NewGuid(), existingProjects: 0);
        var keys = Enumerable.Range(1, existing).Select(nth => EnvironmentKey.Parse($"env-{nth}")).ToList();

        var created = project.CreateEnvironment(EnvironmentKey.Parse("staging"), Names.Of("Staging"), keys);

        Assert.Equal(accepted, created.TryGetValue(out _, out var failure));
        Assert.Equal(accepted ? null : AuthoringFailures.EnvironmentLimitReached, failure);
    }

    [Fact]
    public void A_taken_key_is_reported_before_the_limit()
    {
        var (project, _) = Create(Guid.NewGuid(), existingProjects: 0);
        var keys = Enumerable.Range(1, 10).Select(nth => EnvironmentKey.Parse($"env-{nth}")).ToList();

        var created = project.CreateEnvironment(EnvironmentKey.Parse("env-1"), Names.Of("Again"), keys);

        Assert.False(created.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.EnvironmentKeyTaken, failure);
    }

    private static List<EnvironmentKey> Keys(params string[] keys) => keys.Select(EnvironmentKey.Parse).ToList();

    private static NewProject Create(Guid organizationId, int existingProjects)
    {
        var created = Project.Create(organizationId, ProjectKey.Parse("studio-game"), Names.Of("Studio Game"), existingProjects);
        Assert.True(created.TryGetValue(out var newProject, out var failure), failure?.Detail);
        return newProject;
    }
}
