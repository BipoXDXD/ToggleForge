using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// STRIDE D1: limites por organização e por ambiente. As corridas provam o lock no banco: "conta e depois
/// insere" sem ele deixaria passar mais de um (write skew).
/// </summary>
public sealed class LimitTests(ApiFactory api)
{
    private const int MaxProjects = 20;
    private const int MaxActiveCredentials = 5;
    private const int Racers = 4;

    [Fact]
    public async Task The_21st_project_of_an_organization_conflicts()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        for (var project = 1; project <= MaxProjects; project++)
        {
            Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync($"project-{project}")).Status);
        }

        var response = await owner.CreateProjectAsync("one-too-many");

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/limit-reached", response.ProblemType);
    }

    [Fact]
    public async Task Racing_for_the_last_project_slot_creates_one_project()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        for (var project = 1; project < MaxProjects; project++)
        {
            await owner.CreateProjectAsync($"project-{project}");
        }

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(racer => owner.CreateProjectAsync($"racer-{racer}")));

        Assert.Single(responses, response => response.Status == HttpStatusCode.Created);
        Assert.All(responses.Where(response => response.Status != HttpStatusCode.Created), response => Assert.Equal("/problems/limit-reached", response.ProblemType));
    }

    [Fact]
    public async Task Racing_for_the_same_project_key_creates_one_project()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(_ => owner.CreateProjectAsync("space-race")));

        Assert.Single(responses, response => response.Status == HttpStatusCode.Created);
        Assert.All(responses.Where(response => response.Status != HttpStatusCode.Created), response => Assert.Equal("/problems/key-taken", response.ProblemType));
    }

    [Fact]
    public async Task Racing_for_the_last_credential_slot_issues_one_credential()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        for (var credential = 1; credential < MaxActiveCredentials; credential++)
        {
            await owner.CreateCredentialAsync("space-race");
        }

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(_ => owner.CreateCredentialAsync("space-race")));

        Assert.Single(responses, response => response.Status == HttpStatusCode.Created);
    }

    [Fact]
    public async Task Flags_created_at_the_same_time_all_land_in_the_draft()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(flag => editor.CreateFlagAsync("space-race", $"flag-{flag}")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.Status));
        var draft = await editor.GetDraftAsync("space-race");
        Assert.Equal(Racers, draft.Json["flags"]!.AsArray().Count);
        Assert.Equal(1 + Racers, draft.Json["revision"]!.GetValue<long>());
    }
}
