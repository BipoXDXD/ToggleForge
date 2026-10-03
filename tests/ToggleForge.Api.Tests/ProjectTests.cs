using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class ProjectTests(ApiFactory api)
{
    [Fact]
    public async Task Listing_returns_the_projects_ordered_by_key_with_their_environments()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("zeta-run");
        await owner.CreateProjectAsync("alpha-quest");
        await owner.CreateFlagAsync("zeta-run", "new-shop");

        var listed = await owner.ListProjectsAsync();

        Assert.Equal(HttpStatusCode.OK, listed.Status);
        Assert.Equal("private, no-cache", listed.Header("Cache-Control"));
        var projects = listed.Json["data"]!.AsArray();
        Assert.Equal(["alpha-quest", "zeta-run"], projects.Select(project => project!["key"]!.GetValue<string>()));
        Assert.All(projects, project => Assert.Equal(
            ["dev", "prod"], project!["environments"]!.AsArray().Select(environment => environment!["key"]!.GetValue<string>())));
        Assert.False(projects[0]!["environments"]![0]!["hasUnpublishedChanges"]!.GetValue<bool>());
        Assert.True(projects[1]!["environments"]![0]!["hasUnpublishedChanges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_organization_without_projects_lists_none()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var listed = await owner.ListProjectsAsync();

        Assert.Equal(HttpStatusCode.OK, listed.Status);
        Assert.Empty(listed.Json["data"]!.AsArray());
    }

    [Fact]
    public async Task A_new_environment_starts_with_an_empty_draft_and_no_publication()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var created = await owner.CreateEnvironmentAsync("space-race", "staging");

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.EndsWith("/projects/space-race/environments/staging", created.Header("Location"), StringComparison.Ordinal);
        Assert.Equal("staging", created.Json["key"]!.GetValue<string>());
        Assert.Equal("Environment staging", created.Json["name"]!.GetValue<string>());
        Assert.Null(created.Json["currentVersion"]?.GetValue<long>());
        Assert.Equal(1, created.Json["draftRevision"]!.GetValue<long>());
        Assert.False(created.Json["hasUnpublishedChanges"]!.GetValue<bool>());
        var environments = (await owner.GetProjectAsync("space-race")).Json["environments"]!.AsArray();
        Assert.Equal(["dev", "prod", "staging"], environments.Select(environment => environment!["key"]!.GetValue<string>()));
        Assert.Empty((await owner.GetDraftAsync("space-race", "staging")).Json["flags"]!.AsArray());
    }

    [Fact]
    public async Task An_environment_key_already_in_the_project_conflicts()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var created = await owner.CreateEnvironmentAsync("space-race", "prod");

        Assert.Equal(HttpStatusCode.Conflict, created.Status);
        Assert.Equal("/problems/key-taken", created.ProblemType);
    }

    [Fact]
    public async Task The_same_environment_key_works_in_another_project()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        await owner.CreateProjectAsync("moon-race");
        await owner.CreateEnvironmentAsync("space-race", "staging");

        var created = await owner.CreateEnvironmentAsync("moon-race", "staging");

        Assert.Equal(HttpStatusCode.Created, created.Status);
    }

    [Fact]
    public async Task A_project_holds_10_environments_but_not_11()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        for (var nth = 3; nth <= 10; nth++)
        {
            Assert.Equal(HttpStatusCode.Created, (await owner.CreateEnvironmentAsync("space-race", $"env-{nth}")).Status);
        }

        var eleventh = await owner.CreateEnvironmentAsync("space-race", "env-11");

        Assert.Equal(HttpStatusCode.Conflict, eleventh.Status);
        Assert.Equal("/problems/limit-reached", eleventh.ProblemType);
    }

    [Fact]
    public async Task Creating_an_environment_in_a_missing_project_is_not_found()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var created = await owner.CreateEnvironmentAsync("space-race", "staging");

        Assert.Equal(HttpStatusCode.NotFound, created.Status);
    }

    [Theory]
    [InlineData("""{"key":"Staging","name":"Staging"}""", "/key")]
    [InlineData("""{"key":"staging","name":"   "}""", "/name")]
    [InlineData("""{"name":"Staging"}""", "/key")]
    [InlineData("""{"key":"","name":""}""", "/key,/name")]
    public async Task Invalid_environment_fields_are_rejected_together(string json, string pointers)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var created = await owner.SendRawAsync(
            "createEnvironment", HttpMethod.Post, $"{Paths.Project(owner.OrganizationId, "space-race")}/environments", json);

        Assert.Equal(HttpStatusCode.BadRequest, created.Status);
        Assert.Equal(pointers.Split(','), created.ErrorSources());
    }

    [Fact]
    public async Task Reading_an_environment_tells_its_version_and_pending_changes()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        var revision = await owner.PublishedProjectAsync("space-race");
        var published = await owner.GetEnvironmentAsync("space-race", "dev");
        await owner.CreateFlagAsync("space-race", "battle-pass");

        var changed = await owner.GetEnvironmentAsync("space-race", "dev");

        Assert.Equal(HttpStatusCode.OK, published.Status);
        Assert.Equal("private, no-cache", published.Header("Cache-Control"));
        Assert.Equal(1, published.Json["currentVersion"]!.GetValue<long>());
        Assert.Equal(revision, published.Json["draftRevision"]!.GetValue<long>());
        Assert.False(published.Json["hasUnpublishedChanges"]!.GetValue<bool>());
        Assert.Equal(revision + 1, changed.Json["draftRevision"]!.GetValue<long>());
        Assert.True(changed.Json["hasUnpublishedChanges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_missing_environment_is_not_found()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var environment = await owner.GetEnvironmentAsync("space-race", "staging");

        Assert.Equal(HttpStatusCode.NotFound, environment.Status);
    }
}
