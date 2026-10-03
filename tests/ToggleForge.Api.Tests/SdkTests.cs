using System.Net;
using System.Net.Http.Headers;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class SdkTests(ApiFactory api)
{
    [Theory]
    [InlineData(null)]
    [InlineData("Bearer tfsdk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("Bearer tfsdk_too-short")]
    [InlineData("Bearer ' or 1=1 --")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task A_missing_or_unknown_credential_gets_401(string? authorization)
    {
        var response = await GetSnapshotAsync(authorization);

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("Bearer", response.Header("WWW-Authenticate"));
    }

    [Fact]
    public async Task A_credential_reads_only_its_own_environment()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var prodRevision = ConsoleClient.Revision(await owner.CreateFlagAsync("space-race", "new-shop", environmentKey: "prod"));
        await owner.PublishAsync("space-race", prodRevision, ConsoleClient.NewIdempotencyKey(), environmentKey: "prod");
        var devSecret = (await owner.CreateCredentialAsync("space-race", "dev")).Json["secret"]!.GetValue<string>();

        var response = await GetSnapshotAsync("Bearer " + devSecret);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/no-publication", response.ProblemType);
        Assert.DoesNotContain("new-shop", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_console_session_cannot_download_snapshots()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var response = await owner.SendAsync("getSnapshot", HttpMethod.Get, Paths.Snapshot);

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }

    [Fact]
    public async Task A_malformed_if_none_match_is_rejected()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var secret = (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();

        var response = await GetSnapshotAsync("Bearer " + secret, request => request.Headers.TryAddWithoutValidation("If-None-Match", "not-an-etag"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["If-None-Match"], response.ErrorSources());
    }

    [Theory]
    [InlineData(5, HttpStatusCode.Created)]
    [InlineData(6, HttpStatusCode.Conflict)]
    public async Task An_environment_has_at_most_5_active_credentials(int nth, HttpStatusCode expected)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        for (var created = 1; created < nth; created++)
        {
            await owner.CreateCredentialAsync("space-race");
        }

        var response = await owner.CreateCredentialAsync("space-race");

        Assert.Equal(expected, response.Status);
    }

    private Task<ApiResponse> GetSnapshotAsync(string? authorization, Action<HttpRequestMessage>? configure = null) =>
        GameClient.SendAsync(api, request =>
        {
            if (authorization is not null)
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
            }

            configure?.Invoke(request);
        });
}
