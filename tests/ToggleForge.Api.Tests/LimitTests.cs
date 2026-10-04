using System.Net;
using System.Text;
using System.Text.Json;
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
    private const int MaxEnvironments = 10;
    private const int Racers = 4;
    private const int JsonBudgetBytes = 128 * 1024;
    private const int FullValueBytes = 4 * 1024;
    private const string JsonBudgetReached = "/problems/json-budget-reached";

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
    public async Task Racing_for_the_last_environment_slot_creates_one_environment()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        for (var environment = 3; environment < MaxEnvironments; environment++)
        {
            Assert.Equal(HttpStatusCode.Created, (await owner.CreateEnvironmentAsync("space-race", $"env-{environment}")).Status);
        }

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(racer => owner.CreateEnvironmentAsync("space-race", $"racer-{racer}")));

        Assert.Single(responses, response => response.Status == HttpStatusCode.Created);
        Assert.All(responses.Where(response => response.Status != HttpStatusCode.Created), response => Assert.Equal("/problems/limit-reached", response.ProblemType));
        Assert.Equal(MaxEnvironments, (await owner.GetProjectAsync("space-race")).Json["environments"]!.AsArray().Count);
    }

    [Fact]
    public async Task Racing_for_the_same_environment_key_creates_one_environment()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(_ => owner.CreateEnvironmentAsync("space-race", "staging")));

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

    /// <summary>Canvas D4: o teto do rascunho é invariante do ambiente, travado na edição.</summary>
    [Fact]
    public async Task Concurrent_creations_cannot_exceed_the_environment_json_budget()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        await editor.FillJsonBudgetAsync("space-race", JsonBudgetBytes - (2 * FullValueBytes));
        var fullValue = ConsoleClient.JsonTextOf(FullValueBytes);

        var responses = await Task.WhenAll(Enumerable.Range(1, Racers).Select(racer =>
            editor.CreateJsonFlagAsync("space-race", $"racer-{racer}", fullValue, fullValue)));

        Assert.Single(responses, response => response.Status == HttpStatusCode.Created);
        Assert.All(responses.Where(response => response.Status != HttpStatusCode.Created), response => Assert.Equal(JsonBudgetReached, response.ProblemType));
        Assert.Equal(JsonBudgetBytes, JsonBytesOf(await editor.GetDraftAsync("space-race")));
    }

    /// <summary>A ETag é por flag: só o lock do ambiente impede que edições de flags diferentes passem juntas do teto.</summary>
    [Fact]
    public async Task Concurrent_changes_cannot_exceed_the_environment_json_budget()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var racers = new List<(string Key, string? ETag)>();
        for (var racer = 1; racer <= Racers; racer++)
        {
            var created = await editor.CreateJsonFlagAsync("space-race", $"racer-{racer}", "0", "0");
            racers.Add(($"racer-{racer}", created.Header("ETag")));
        }

        const int Growth = FullValueBytes - 1;
        await editor.FillJsonBudgetAsync("space-race", JsonBudgetBytes - (2 * Racers) - Growth);
        var patch = $$"""{"onValue":{{ConsoleClient.JsonTextOf(FullValueBytes)}}}""";

        var responses = await Task.WhenAll(racers.Select(racer => editor.UpdateFlagAsync("space-race", racer.Key, racer.ETag, patch)));

        Assert.Single(responses, response => response.Status == HttpStatusCode.OK);
        Assert.All(responses.Where(response => response.Status != HttpStatusCode.OK), response => Assert.Equal(JsonBudgetReached, response.ProblemType));
        Assert.Equal(JsonBudgetBytes, JsonBytesOf(await editor.GetDraftAsync("space-race")));
    }

    /// <summary>A soma dos valores das flags json do rascunho, como o servidor os escreve (compactos).</summary>
    private static int JsonBytesOf(ApiResponse draft)
    {
        using var body = JsonDocument.Parse(draft.Body);
        return body.RootElement.GetProperty("flags").EnumerateArray()
            .Where(flag => flag.GetProperty("type").GetString() == "json")
            .Sum(flag => Encoding.UTF8.GetByteCount(flag.GetProperty("onValue").GetRawText())
                + Encoding.UTF8.GetByteCount(flag.GetProperty("offValue").GetRawText()));
    }
}
