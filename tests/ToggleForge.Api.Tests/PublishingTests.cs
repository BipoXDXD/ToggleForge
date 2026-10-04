using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class PublishingTests(ApiFactory api)
{
    [Fact]
    public async Task Repeating_a_publication_with_the_same_key_and_body_returns_the_original()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop"));
        var key = ConsoleClient.NewIdempotencyKey();
        var original = await editor.PublishAsync("space-race", revision, key, summary: "First release");

        var repeated = await editor.PublishAsync("space-race", revision, key, summary: "First release");

        Assert.Equal(HttpStatusCode.Created, repeated.Status);
        Assert.Equal(original.Body, repeated.Body);
        Assert.Equal(original.Header("Location"), repeated.Header("Location"));
        Assert.Equal(1, (await CurrentVersionAsync(editor)).GetValueOrDefault());
    }

    [Fact]
    public async Task Reusing_a_key_with_another_body_conflicts()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop"));
        var key = ConsoleClient.NewIdempotencyKey();
        await editor.PublishAsync("space-race", revision, key, summary: "First release");

        var reused = await editor.PublishAsync("space-race", revision, key, summary: "Another release");

        Assert.Equal(HttpStatusCode.Conflict, reused.Status);
        Assert.Equal("/problems/idempotency-key-reused", reused.ProblemType);
    }

    [Fact]
    public async Task Another_member_using_the_same_key_gets_their_own_publication()
    {
        using var first = await ConsoleClient.SignUpAsync(api);
        using var second = await ConsoleClient.SignUpAsync(api);
        var key = ConsoleClient.NewIdempotencyKey();
        await first.CreateProjectAsync("space-race");
        await second.CreateProjectAsync("space-race");
        await first.PublishAsync("space-race", ConsoleClient.Revision(await first.CreateFlagAsync("space-race", "new-shop")), key);

        var response = await second.PublishAsync(
            "space-race", ConsoleClient.Revision(await second.CreateFlagAsync("space-race", "battle-pass")), key);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal(second.Email, response.Json["publishedBy"]!["email"]!.GetValue<string>());
        Assert.Equal("battle-pass", response.Json["flags"]![0]!["key"]!.GetValue<string>());
    }

    [Fact]
    public async Task Simultaneous_retries_with_the_same_key_publish_once()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop"));
        var key = ConsoleClient.NewIdempotencyKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => editor.PublishAsync("space-race", revision, key)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.Status));
        Assert.Single(responses.Select(response => response.Body).Distinct());
        Assert.Equal(1, (await CurrentVersionAsync(editor)).GetValueOrDefault());
    }

    [Fact]
    public async Task The_same_key_in_another_environment_conflicts()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var devRevision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop", environmentKey: "dev"));
        var prodRevision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop", environmentKey: "prod"));
        var key = ConsoleClient.NewIdempotencyKey();
        var original = await editor.PublishAsync("space-race", devRevision, key, environmentKey: "dev");
        Assert.Equal(HttpStatusCode.Created, original.Status);

        var reused = await editor.PublishAsync("space-race", prodRevision, key, environmentKey: "prod");

        Assert.Equal(HttpStatusCode.Conflict, reused.Status);
        Assert.Equal("/problems/idempotency-key-reused", reused.ProblemType);
        Assert.Null(await CurrentVersionAsync(editor, "prod"));
        Assert.Equal(1, await CurrentVersionAsync(editor, "dev"));
    }

    /// <summary>
    /// O lock é por ambiente, então as requisições não se esperam: em geral todas passam pelo replay sem achar a
    /// chave e disputam a primary key do registro de idempotência, e as perdedoras abortam e respondem pelo registro
    /// da vencedora. Uma requisição que chegue depois do commit cai no replay comum; nos dois caminhos a resposta é
    /// 409, porque o fingerprint da vencedora tem outro ambiente.
    /// </summary>
    [Fact]
    public async Task The_same_key_racing_in_several_environments_publishes_once_and_conflicts_for_the_rest()
    {
        const int Racers = 4;
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        // Todo projeto nasce com dev e prod; os outros corredores são ambientes criados aqui.
        string[] created = [.. Enumerable.Range(1, Racers - 2).Select(racer => $"racer-{racer}")];
        foreach (var environment in created)
        {
            await editor.CreateEnvironmentAsync("space-race", environment);
        }

        string[] environments = ["dev", "prod", .. created];
        var revisions = new Dictionary<string, long>();
        foreach (var environment in environments)
        {
            revisions[environment] = ConsoleClient.Revision(
                await editor.CreateFlagAsync("space-race", "new-shop", environmentKey: environment));
        }

        var key = ConsoleClient.NewIdempotencyKey();

        var responses = await Task.WhenAll(environments.Select(environment =>
            editor.PublishAsync("space-race", revisions[environment], key, environmentKey: environment)));

        var outcomes = environments.Zip(responses, (environment, response) => (Environment: environment, Response: response)).ToList();
        var winner = Assert.Single(outcomes, outcome => outcome.Response.Status == HttpStatusCode.Created).Environment;
        Assert.All(
            outcomes.Where(outcome => outcome.Environment != winner),
            outcome =>
            {
                Assert.Equal(HttpStatusCode.Conflict, outcome.Response.Status);
                Assert.Equal("/problems/idempotency-key-reused", outcome.Response.ProblemType);
            });
        foreach (var environment in environments)
        {
            Assert.Equal(environment == winner ? 1 : null, await CurrentVersionAsync(editor, environment));
        }
    }

    /// <summary>
    /// Mesma organização, ambiente, chave e corpo: só o membro diferencia os pedidos. O colega não recebe a
    /// publicação do dono como replay; o pedido dele é avaliado do zero e esbarra no rascunho já publicado.
    /// </summary>
    [Fact]
    public async Task A_colleague_reusing_a_members_key_and_body_is_not_served_their_publication()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        using var colleague = await ConsoleClient.SignUpAsync(api);
        await api.AddMembershipAsync(owner.OrganizationId, colleague.MemberId, "editor");
        await owner.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await owner.CreateFlagAsync("space-race", "new-shop"));
        var key = ConsoleClient.NewIdempotencyKey();
        var original = await owner.PublishAsync("space-race", revision, key, summary: "First release");
        Assert.Equal(HttpStatusCode.Created, original.Status);

        var response = await colleague.PublishAsync(
            "space-race", revision, key, summary: "First release", organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/nothing-to-publish", response.ProblemType);
        Assert.Equal(1, await CurrentVersionAsync(owner));
    }

    [Fact]
    public async Task Publishing_a_revision_older_than_the_draft_conflicts()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var reviewed = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop"));
        await editor.CreateFlagAsync("space-race", "battle-pass");

        var response = await editor.PublishAsync("space-race", reviewed, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/draft-changed", response.ProblemType);
        Assert.Null(await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task Publishing_a_draft_equal_to_the_current_publication_conflicts()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        var revision = await editor.PublishedProjectAsync("space-race");

        var response = await editor.PublishAsync("space-race", revision, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/nothing-to-publish", response.ProblemType);
    }

    [Theory]
    [InlineData(null, "MISSING_VALUE")]
    [InlineData("too-short-key", "INVALID_VALUE")]
    [InlineData("has spaces in the key 123", "INVALID_VALUE")]
    public async Task The_idempotency_key_is_required_and_validated(string? key, string errorType)
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop"));

        var response = await editor.SendAsync(
            "publishDraft",
            HttpMethod.Post,
            $"{Paths.Environment(editor.OrganizationId, "space-race", "dev")}/publications",
            new { draftRevision = revision },
            request =>
            {
                if (key is not null)
                {
                    request.Headers.Add("Idempotency-Key", key);
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["Idempotency-Key"], response.ErrorSources());
        Assert.Equal(errorType, response.Json["errors"]![0]!["type"]!.GetValue<string>());
    }

    private static async Task<long?> CurrentVersionAsync(ConsoleClient client, string environmentKey = "dev")
    {
        var project = await client.GetProjectAsync("space-race");
        var environment = project.Json["environments"]!.AsArray()
            .Single(environment => environment!["key"]!.GetValue<string>() == environmentKey)!;
        return environment["currentVersion"]?.GetValue<long>();
    }
}
