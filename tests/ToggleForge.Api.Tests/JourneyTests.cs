using System.Net;
using System.Net.Http.Headers;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>O caminho feliz mais longo do Marco 2, ponta a ponta (Khorikov cap. 8).</summary>
public sealed class JourneyTests(ApiFactory api)
{
    [Fact]
    public async Task A_published_flag_reaches_the_game_through_the_environment_credential()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        var flag = await owner.CreateFlagAsync("space-race", "new-shop", rolloutBasisPoints: 2_500);
        var published = await owner.PublishAsync(
            "space-race", ConsoleClient.Revision(flag), ConsoleClient.NewIdempotencyKey(), summary: "Open the new shop to 25%");
        var credential = await owner.CreateCredentialAsync("space-race");
        var secret = credential.Json["secret"]!.GetValue<string>();

        var snapshot = await GetSnapshotAsync(secret);

        Assert.Equal(HttpStatusCode.Created, published.Status);
        Assert.Equal(1, published.Json["version"]!.GetValue<long>());
        Assert.Equal(owner.Email, published.Json["publishedBy"]!["email"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, snapshot.Status);
        Assert.Equal("toggleforge.rollout.v1", snapshot.Json["algorithm"]!.GetValue<string>());
        Assert.Equal("space-race", snapshot.Json["projectKey"]!.GetValue<string>());
        Assert.Equal("dev", snapshot.Json["environmentKey"]!.GetValue<string>());
        Assert.Equal(1, snapshot.Json["version"]!.GetValue<long>());
        var shipped = Assert.Single(snapshot.Json["flags"]!.AsArray())!;
        Assert.Equal("new-shop", shipped["key"]!.GetValue<string>());
        Assert.Equal(2_500, shipped["rolloutBasisPoints"]!.GetValue<int>());
        Assert.Matches("^[0-9a-f]{32}$", shipped["seed"]!.GetValue<string>());
        Assert.Equal("\"1\"", snapshot.Header("ETag"));
    }

    [Fact]
    public async Task The_game_gets_304_while_the_snapshot_is_unchanged()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var secret = (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();
        var etag = (await GetSnapshotAsync(secret)).Header("ETag");

        var unchanged = await GetSnapshotAsync(secret, etag);

        Assert.Equal(HttpStatusCode.NotModified, unchanged.Status);
        Assert.Equal(etag, unchanged.Header("ETag"));
    }

    [Fact]
    public async Task The_credential_secret_never_reaches_the_logs()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var secret = (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();

        await GetSnapshotAsync(secret);

        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(owner.SessionCookieValue()!, StringComparison.Ordinal));
    }

    private Task<ApiResponse> GetSnapshotAsync(string secret, string? etag = null) =>
        GameClient.GetSnapshotAsync(api, secret, request =>
        {
            if (etag is not null)
            {
                request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
            }
        });
}
