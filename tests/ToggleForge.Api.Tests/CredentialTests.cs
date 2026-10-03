using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE S1: a credencial extraída de um build deixa de funcionar quando o Owner a revoga.</summary>
public sealed class CredentialTests(ApiFactory api)
{
    [Fact]
    public async Task Revoked_credential_is_rejected_from_the_next_request_on()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var credential = await owner.CreateCredentialAsync("space-race");
        var secret = credential.Json["secret"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await GameClient.GetSnapshotAsync(api, secret)).Status);

        var revoked = await owner.RevokeCredentialAsync("space-race", Id(credential));

        Assert.Equal(HttpStatusCode.OK, revoked.Status);
        Assert.NotNull(revoked.Json["revokedAt"]?.GetValue<DateTimeOffset>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await GameClient.GetSnapshotAsync(api, secret)).Status);
    }

    [Fact]
    public async Task Revoking_twice_returns_the_same_revocation()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var id = Id(await owner.CreateCredentialAsync("space-race"));
        var first = await owner.RevokeCredentialAsync("space-race", id);

        var second = await owner.RevokeCredentialAsync("space-race", id);

        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body, second.Body);
    }

    [Fact]
    public async Task Simultaneous_revocations_agree_on_the_instant()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var id = Id(await owner.CreateCredentialAsync("space-race"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.RevokeCredentialAsync("space-race", id)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        Assert.Single(responses.Select(response => response.Body).Distinct());
    }

    [Fact]
    public async Task Revoking_frees_a_slot_for_a_new_credential()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var ids = new List<string>();
        for (var nth = 1; nth <= 5; nth++)
        {
            ids.Add(Id(await owner.CreateCredentialAsync("space-race")));
        }

        Assert.Equal(HttpStatusCode.Conflict, (await owner.CreateCredentialAsync("space-race")).Status);
        await owner.RevokeCredentialAsync("space-race", ids[0]);

        Assert.Equal(HttpStatusCode.Created, (await owner.CreateCredentialAsync("space-race")).Status);
    }

    [Fact]
    public async Task Listing_shows_credentials_newest_first_without_secrets()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var older = await owner.CreateCredentialAsync("space-race");
        var newer = await owner.CreateCredentialAsync("space-race");
        await owner.RevokeCredentialAsync("space-race", Id(older));

        var listed = await owner.ListCredentialsAsync("space-race");

        Assert.Equal(HttpStatusCode.OK, listed.Status);
        Assert.Equal("private, no-cache", listed.Header("Cache-Control"));
        Assert.Equal([Id(newer), Id(older)], Ids(listed));
        Assert.Null(listed.Json["data"]![0]!["revokedAt"]?.GetValue<DateTimeOffset>());
        Assert.NotNull(listed.Json["data"]![1]!["revokedAt"]?.GetValue<DateTimeOffset>());
        Assert.DoesNotContain(older.Json["secret"]!.GetValue<string>(), listed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(newer.Json["secret"]!.GetValue<string>(), listed.Body, StringComparison.Ordinal);
    }

    /// <summary>Até 50 cabem todas; a 46ª revogada empurra a revogada mais antiga para fora da lista.</summary>
    [Theory]
    [InlineData(45, 0)]
    [InlineData(46, 1)]
    public async Task The_list_keeps_every_active_credential_and_the_most_recently_revoked(int revoked, int omitted)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var revokedIds = new List<string>();
        for (var nth = 0; nth < revoked; nth++)
        {
            var id = Id(await owner.CreateCredentialAsync("space-race"));
            await owner.RevokeCredentialAsync("space-race", id);
            revokedIds.Add(id);
        }

        var activeIds = new List<string>();
        for (var nth = 0; nth < 5; nth++)
        {
            activeIds.Add(Id(await owner.CreateCredentialAsync("space-race")));
        }

        var listed = Ids(await owner.ListCredentialsAsync("space-race"));

        Assert.Equal(50, listed.Count);
        Assert.Subset(listed.ToHashSet(), activeIds.ToHashSet());
        Assert.Equal(revokedIds[omitted..], listed.Where(id => !activeIds.Contains(id)).Reverse());
    }

    [Fact]
    public async Task A_credential_of_another_environment_is_not_found_and_stays_active()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var prodId = Id(await owner.CreateCredentialAsync("space-race", "prod"));

        var response = await owner.RevokeCredentialAsync("space-race", prodId, environmentKey: "dev");

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/not-found", response.ProblemType);
        var prod = Assert.Single((await owner.ListCredentialsAsync("space-race", "prod")).Json["data"]!.AsArray())!;
        Assert.Null(prod["revokedAt"]?.GetValue<DateTimeOffset>());
    }

    [Fact]
    public async Task An_unknown_credential_is_not_found()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var response = await owner.RevokeCredentialAsync("space-race", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("' or 1=1 --")]
    [InlineData("0f8fad5bd9cb469fa16570867728950e")]
    public async Task A_malformed_credential_id_is_rejected(string credentialId)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var response = await owner.RevokeCredentialAsync("space-race", Uri.EscapeDataString(credentialId));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["credentialId"], response.ErrorSources());
    }

    private static string Id(ApiResponse credential) => credential.Json["id"]!.GetValue<string>();

    private static List<string> Ids(ApiResponse list) =>
        list.Json["data"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
}
