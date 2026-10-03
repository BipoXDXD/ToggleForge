using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class FlagEditingTests(ApiFactory api)
{
    [Fact]
    public async Task Reading_a_flag_returns_the_etag_of_its_last_change()
    {
        using var editor = await DraftWithNewShopAsync();

        var flag = await editor.Client.GetFlagAsync("space-race", "new-shop");

        Assert.Equal(HttpStatusCode.OK, flag.Status);
        Assert.Equal(editor.ETag, flag.Header("ETag"));
        Assert.Equal(1_000, flag.Json["rolloutBasisPoints"]!.GetValue<int>());
    }

    [Fact]
    public async Task Updating_changes_only_the_fields_sent()
    {
        using var editor = await DraftWithNewShopAsync();

        var updated = await editor.Client.UpdateFlagAsync("space-race", "new-shop", editor.ETag, """{"rolloutBasisPoints":5000}""");

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal(5_000, updated.Json["rolloutBasisPoints"]!.GetValue<int>());
        Assert.True(updated.Json["enabled"]!.GetValue<bool>());
        Assert.True(updated.Json["onValue"]!.GetValue<bool>());
        Assert.False(updated.Json["offValue"]!.GetValue<bool>());
        Assert.Equal("\"3\"", updated.Header("ETag"));
        Assert.Equal(3, (await editor.Client.GetDraftAsync("space-race")).Json["revision"]!.GetValue<long>());
    }

    [Fact]
    public async Task Second_update_with_stale_revision_conflicts_and_keeps_the_first()
    {
        using var editor = await DraftWithNewShopAsync();
        await editor.Client.UpdateFlagAsync("space-race", "new-shop", editor.ETag, """{"rolloutBasisPoints":5000}""");

        var stale = await editor.Client.UpdateFlagAsync("space-race", "new-shop", editor.ETag, """{"rolloutBasisPoints":100}""");

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal("/problems/flag-changed", stale.ProblemType);
        Assert.Equal(5_000, (await editor.Client.GetFlagAsync("space-race", "new-shop")).Json["rolloutBasisPoints"]!.GetValue<int>());
    }

    [Fact]
    public async Task Simultaneous_updates_with_the_same_etag_apply_once()
    {
        using var editor = await DraftWithNewShopAsync();

        var responses = await Task.WhenAll(Enumerable.Range(1, 4).Select(racer =>
            editor.Client.UpdateFlagAsync("space-race", "new-shop", editor.ETag, $$"""{"rolloutBasisPoints":{{racer * 100}}}""")));

        Assert.Single(responses, response => response.Status == HttpStatusCode.OK);
        Assert.All(responses.Where(response => response.Status != HttpStatusCode.OK), response => Assert.Equal(HttpStatusCode.PreconditionFailed, response.Status));
        Assert.Equal(3, (await editor.Client.GetDraftAsync("space-race")).Json["revision"]!.GetValue<long>());
    }

    [Fact]
    public async Task Editing_one_flag_does_not_make_another_stale()
    {
        using var editor = await DraftWithNewShopAsync();
        var battlePass = await editor.Client.CreateFlagAsync("space-race", "battle-pass");
        await editor.Client.UpdateFlagAsync("space-race", "new-shop", editor.ETag, """{"enabled":false}""");

        var updated = await editor.Client.UpdateFlagAsync("space-race", "battle-pass", battlePass.Header("ETag"), """{"enabled":false}""");

        Assert.Equal(HttpStatusCode.OK, updated.Status);
    }

    [Theory]
    [InlineData("updateFlag")]
    [InlineData("deleteFlag")]
    public async Task A_change_without_if_match_gets_428_and_changes_nothing(string operation)
    {
        using var editor = await DraftWithNewShopAsync();

        var response = operation == "updateFlag"
            ? await editor.Client.UpdateFlagAsync("space-race", "new-shop", ifMatch: null, """{"enabled":false}""")
            : await editor.Client.DeleteFlagAsync("space-race", "new-shop", ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.Status);
        Assert.Equal("/problems/precondition-required", response.ProblemType);
        await AssertUnchangedAsync(editor);
    }

    [Theory]
    [InlineData("W/\"2\"")]
    [InlineData("*")]
    [InlineData("\"two\"")]
    [InlineData("\"0\"")]
    [InlineData("\"2\", \"3\"")]
    [InlineData("2")]
    public async Task A_malformed_if_match_is_rejected(string ifMatch)
    {
        using var editor = await DraftWithNewShopAsync();

        var response = await editor.Client.UpdateFlagAsync("space-race", "new-shop", ifMatch, """{"enabled":false}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["If-Match"], response.ErrorSources());
        await AssertUnchangedAsync(editor);
    }

    [Theory]
    [InlineData("""{"enabled":null}""")]
    [InlineData("""{}""")]
    [InlineData("""{"seed":"attacker"}""")]
    [InlineData("""{"key":"other-key"}""")]
    [InlineData("""{"rolloutBasisPoints":10001}""")]
    [InlineData("""{"rolloutBasisPoints":"5000"}""")]
    public async Task An_invalid_patch_is_rejected_and_changes_nothing(string json)
    {
        using var editor = await DraftWithNewShopAsync();

        var response = await editor.Client.UpdateFlagAsync("space-race", "new-shop", editor.ETag, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        await AssertUnchangedAsync(editor);
    }

    [Fact]
    public async Task A_patch_sent_as_plain_json_is_rejected()
    {
        using var editor = await DraftWithNewShopAsync();

        var response = await editor.Client.UpdateFlagAsync(
            "space-race", "new-shop", editor.ETag, """{"enabled":false}""", mediaType: "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["Content-Type"], response.ErrorSources());
    }

    [Fact]
    public async Task Deleting_takes_the_flag_out_and_a_second_delete_gets_404()
    {
        using var editor = await DraftWithNewShopAsync();

        var deleted = await editor.Client.DeleteFlagAsync("space-race", "new-shop", editor.ETag);
        var again = await editor.Client.DeleteFlagAsync("space-race", "new-shop", editor.ETag);

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
        var draft = await editor.Client.GetDraftAsync("space-race");
        Assert.Empty(draft.Json["flags"]!.AsArray());
        Assert.Equal(3, draft.Json["revision"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_deleted_flag_stays_with_games_until_the_next_publication()
    {
        using var editor = await DraftWithNewShopAsync();
        await editor.Client.PublishAsync("space-race", 2, ConsoleClient.NewIdempotencyKey());

        await editor.Client.DeleteFlagAsync("space-race", "new-shop", editor.ETag);

        var draft = await editor.Client.GetDraftAsync("space-race");
        var change = Assert.Single(draft.Json["changes"]!.AsArray())!;
        Assert.Equal("new-shop", change["flagKey"]!.GetValue<string>());
        Assert.Equal("removed", change["change"]!.GetValue<string>());
        Assert.Equal(1, draft.Json["basedOnVersion"]!.GetValue<long>());
    }

    [Fact]
    public async Task Recreating_a_deleted_key_gives_it_a_new_seed()
    {
        using var editor = await DraftWithNewShopAsync();
        await editor.Client.PublishAsync("space-race", 2, ConsoleClient.NewIdempotencyKey());
        var secret = (await editor.Client.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();
        var firstSeed = await SeedInSnapshotAsync(secret);
        await editor.Client.DeleteFlagAsync("space-race", "new-shop", editor.ETag);
        var recreated = await editor.Client.CreateFlagAsync("space-race", "new-shop");

        await editor.Client.PublishAsync("space-race", ConsoleClient.Revision(recreated), ConsoleClient.NewIdempotencyKey());

        Assert.NotEqual(firstSeed, await SeedInSnapshotAsync(secret));
    }

    private async Task<EditorDraft> DraftWithNewShopAsync()
    {
        var client = await ConsoleClient.SignUpAsync(api);
        await client.CreateProjectAsync("space-race");
        var flag = await client.CreateFlagAsync("space-race", "new-shop");
        return new EditorDraft(client, flag.Header("ETag")!);
    }

    private static async Task AssertUnchangedAsync(EditorDraft editor)
    {
        var flag = await editor.Client.GetFlagAsync("space-race", "new-shop");
        Assert.Equal(editor.ETag, flag.Header("ETag"));
        Assert.True(flag.Json["enabled"]!.GetValue<bool>());
        Assert.Equal(2, (await editor.Client.GetDraftAsync("space-race")).Json["revision"]!.GetValue<long>());
    }

    private async Task<string> SeedInSnapshotAsync(string secret) =>
        (await GameClient.GetSnapshotAsync(api, secret)).Json["flags"]![0]!["seed"]!.GetValue<string>();

    private sealed record EditorDraft(ConsoleClient Client, string ETag) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
