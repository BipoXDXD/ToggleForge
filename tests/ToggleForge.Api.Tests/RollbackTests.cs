using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class RollbackTests(ApiFactory api)
{
    [Fact]
    public async Task Rolling_back_publishes_the_target_content_as_a_new_version()
    {
        using var editor = await TwoPublicationsAsync();

        var response = await editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.EndsWith("/environments/dev/publications/3", response.Header("Location"), StringComparison.Ordinal);
        Assert.Equal(3, response.Json["version"]!.GetValue<long>());
        Assert.Equal("rollback", response.Json["origin"]!.GetValue<string>());
        Assert.Equal(1, response.Json["restoredVersion"]!.GetValue<long>());
        Assert.Null(response.Json["summary"]);
        Assert.Equal(editor.Email, response.Json["publishedBy"]!["email"]!.GetValue<string>());
        var flag = Assert.Single(response.Json["flags"]!.AsArray())!;
        Assert.Equal("new-shop", flag["key"]!.GetValue<string>());
        Assert.Equal(1_000, flag["rolloutBasisPoints"]!.GetValue<int>());
    }

    [Fact]
    public async Task Rolling_back_replaces_the_draft_with_the_restored_content()
    {
        using var editor = await TwoPublicationsAsync();

        await editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey());

        var draft = await editor.GetDraftAsync("space-race");
        Assert.Equal(4, draft.Json["revision"]!.GetValue<long>());
        Assert.Equal(3, draft.Json["basedOnVersion"]!.GetValue<long>());
        Assert.Empty(draft.Json["changes"]!.AsArray());
        var flag = Assert.Single(draft.Json["flags"]!.AsArray())!;
        Assert.Equal(1_000, flag["rolloutBasisPoints"]!.GetValue<int>());
        Assert.Equal("\"4\"", (await editor.GetFlagAsync("space-race", "new-shop")).Header("ETag"));
    }

    [Fact]
    public async Task The_game_downloads_the_restored_content()
    {
        using var editor = await TwoPublicationsAsync();
        var secret = (await editor.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();

        await editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey());

        var snapshot = await GameClient.GetSnapshotAsync(api, secret);
        Assert.Equal(3, snapshot.Json["version"]!.GetValue<long>());
        Assert.Equal(1_000, snapshot.Json["flags"]![0]!["rolloutBasisPoints"]!.GetValue<int>());
    }

    [Fact]
    public async Task Rolling_back_after_another_publication_conflicts_and_keeps_the_current_version()
    {
        using var editor = await TwoPublicationsAsync();

        var response = await editor.RollBackAsync("space-race", 1, 1, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/current-version-changed", response.ProblemType);
        Assert.Equal(2, await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task Rolling_back_to_the_current_content_conflicts()
    {
        using var editor = await TwoPublicationsAsync();

        var response = await editor.RollBackAsync("space-race", 2, 2, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/already-current", response.ProblemType);
    }

    [Fact]
    public async Task Rolling_back_over_unpublished_changes_conflicts_and_keeps_them()
    {
        using var editor = await TwoPublicationsAsync();
        await editor.CreateFlagAsync("space-race", "season-event");

        var response = await editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/draft-has-pending-changes", response.ProblemType);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetFlagAsync("space-race", "season-event")).Status);
        Assert.Equal(2, await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task Rolling_back_with_discard_drops_the_unpublished_changes()
    {
        using var editor = await TwoPublicationsAsync();
        await editor.CreateFlagAsync("space-race", "season-event");

        var response = await editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey(), discardDraftChanges: true);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetFlagAsync("space-race", "season-event")).Status);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(long.MaxValue)]
    public async Task Rolling_back_to_a_version_that_does_not_exist_is_a_bad_request(long targetVersion)
    {
        using var editor = await TwoPublicationsAsync();

        var response = await editor.RollBackAsync("space-race", targetVersion, 2, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/targetVersion"], response.ErrorSources());
        Assert.Equal(2, await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task Rolling_back_an_environment_that_never_published_is_a_bad_request()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");

        var response = await editor.RollBackAsync("space-race", 1, 1, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/targetVersion"], response.ErrorSources());
    }

    [Theory]
    [InlineData("""{"expectedCurrentVersion":2}""", "/targetVersion")]
    [InlineData("""{"targetVersion":1}""", "/expectedCurrentVersion")]
    [InlineData("""{"targetVersion":0,"expectedCurrentVersion":2}""", "/targetVersion")]
    [InlineData("""{"targetVersion":1,"expectedCurrentVersion":0}""", "/expectedCurrentVersion")]
    [InlineData("""{"targetVersion":-1,"expectedCurrentVersion":-1}""", "/expectedCurrentVersion,/targetVersion")]
    public async Task Invalid_versions_are_rejected_together(string json, string pointers)
    {
        using var editor = await TwoPublicationsAsync();

        var response = await editor.SendAsync(
            "rollBack",
            HttpMethod.Post,
            $"{Paths.Environment(editor.OrganizationId, "space-race", "dev")}/rollbacks",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            request => request.Headers.Add("Idempotency-Key", ConsoleClient.NewIdempotencyKey()));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(pointers.Split(','), response.ErrorSources());
        Assert.Equal(2, await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task Repeating_a_rollback_with_the_same_key_and_body_returns_the_original()
    {
        using var editor = await TwoPublicationsAsync();
        var key = ConsoleClient.NewIdempotencyKey();
        var original = await editor.RollBackAsync("space-race", 1, 2, key);

        var repeated = await editor.RollBackAsync("space-race", 1, 2, key);

        Assert.Equal(HttpStatusCode.Created, repeated.Status);
        Assert.Equal(original.Body, repeated.Body);
        Assert.Equal(original.Header("Location"), repeated.Header("Location"));
        Assert.Equal(3, await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task Reusing_a_rollback_key_with_another_body_conflicts()
    {
        using var editor = await TwoPublicationsAsync();
        var key = ConsoleClient.NewIdempotencyKey();
        await editor.RollBackAsync("space-race", 1, 2, key);

        var reused = await editor.RollBackAsync("space-race", 2, 3, key);

        Assert.Equal(HttpStatusCode.Conflict, reused.Status);
        Assert.Equal("/problems/idempotency-key-reused", reused.ProblemType);
        Assert.Equal(3, await CurrentVersionAsync(editor));
    }

    [Fact]
    public async Task A_key_used_to_publish_cannot_roll_back()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await editor.CreateFlagAsync("space-race", "new-shop"));
        var key = ConsoleClient.NewIdempotencyKey();
        await editor.PublishAsync("space-race", revision, key);

        var response = await editor.RollBackAsync("space-race", 1, 1, key);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/idempotency-key-reused", response.ProblemType);
    }

    [Fact]
    public async Task Simultaneous_rollbacks_from_the_same_version_apply_once()
    {
        using var editor = await TwoPublicationsAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey())));

        Assert.Single(responses, response => response.Status == HttpStatusCode.Created);
        Assert.All(
            responses.Where(response => response.Status != HttpStatusCode.Created),
            response => Assert.Equal("/problems/current-version-changed", response.ProblemType));
        Assert.Equal(3, await CurrentVersionAsync(editor));
    }

    /// <summary><c>new-shop</c> a 10% na versão 1 e a 50% na versão 2; o rascunho fica na revisão 3.</summary>
    private async Task<ConsoleClient> TwoPublicationsAsync()
    {
        var editor = await ConsoleClient.SignUpAsync(api);
        await editor.PublishedProjectAsync("space-race");
        var flag = await editor.GetFlagAsync("space-race", "new-shop");
        var changed = await editor.UpdateFlagAsync("space-race", "new-shop", flag.Header("ETag"), """{"rolloutBasisPoints":5000}""");
        Assert.Equal(HttpStatusCode.Created, (await editor.PublishAsync("space-race", ConsoleClient.Revision(changed), ConsoleClient.NewIdempotencyKey())).Status);
        return editor;
    }

    private static async Task<long?> CurrentVersionAsync(ConsoleClient client)
    {
        var project = await client.GetProjectAsync("space-race");
        var dev = project.Json["environments"]!.AsArray().Single(environment => environment!["key"]!.GetValue<string>() == "dev")!;
        return dev["currentVersion"]?.GetValue<long>();
    }
}
