using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class RequestValidationTests(ApiFactory api)
{
    [Fact]
    public async Task A_mutation_without_the_antiforgery_token_is_rejected_and_changes_nothing()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var response = await owner.SendWithoutAntiforgeryAsync(
            "createProject", HttpMethod.Post, Paths.Projects(owner.OrganizationId), new { key = "space-race", name = "Space Race" });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("INVALID_TOKEN", response.Json["errors"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetProjectAsync("space-race")).Status);
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("revision")]
    [InlineData("organizationId")]
    public async Task Unknown_fields_are_rejected_and_nothing_is_saved(string field)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var response = await owner.SendRawAsync(
            "createFlag",
            HttpMethod.Post,
            $"{Paths.Environment(owner.OrganizationId, "space-race", "dev")}/draft/flags",
            $$"""{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":100,"{{field}}":"attacker"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("INVALID_BODY", response.Json["errors"]![0]!["type"]!.GetValue<string>());
        var draft = await owner.GetDraftAsync("space-race");
        Assert.Empty(draft.Json["flags"]!.AsArray());
        Assert.Equal(1, draft.Json["revision"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("""{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":"100"}""")]
    [InlineData("""{"key":"new-shop","key":"other","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":100}""")]
    [InlineData("""{"key":"new-shop",""")]
    [InlineData("")]
    public async Task Malformed_bodies_are_rejected(string json)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var response = await owner.SendRawAsync(
            "createFlag", HttpMethod.Post, $"{Paths.Environment(owner.OrganizationId, "space-race", "dev")}/draft/flags", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task Every_problem_of_the_request_is_listed_at_once()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var response = await owner.SendRawAsync(
            "createFlag",
            HttpMethod.Post,
            $"{Paths.Environment(owner.OrganizationId, "space-race", "dev")}/draft/flags",
            """{"key":"New Shop","rolloutBasisPoints":10001}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/enabled", "/key", "/offValue", "/onValue", "/rolloutBasisPoints"], response.ErrorSources());
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.Created)]
    [InlineData(10_000, HttpStatusCode.Created)]
    [InlineData(10_001, HttpStatusCode.BadRequest)]
    public async Task Rollout_accepts_0_to_10000_basis_points(int basisPoints, HttpStatusCode expected)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var response = await owner.CreateFlagAsync("space-race", "new-shop", basisPoints);

        Assert.Equal(expected, response.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Space\u0000Race")]
    [InlineData("Space\nRace")]
    [InlineData("'; drop table authoring.projects; --\u0007")]
    public async Task Invalid_names_are_rejected(string name)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var response = await owner.SendAsync(
            "createProject", HttpMethod.Post, Paths.Projects(owner.OrganizationId), new { key = "space-race", name });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/name"], response.ErrorSources());
    }

    [Theory]
    [InlineData(100, HttpStatusCode.Created)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    public async Task Names_have_at_most_100_characters(int length, HttpStatusCode expected)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var response = await owner.SendAsync(
            "createProject", HttpMethod.Post, Paths.Projects(owner.OrganizationId), new { key = "space-race", name = new string('n', length) });

        Assert.Equal(expected, response.Status);
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("' or 1=1 --")]
    public async Task An_invalid_organization_id_is_reported_in_the_path(string organizationId)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var response = await owner.SendAsync("getProject", HttpMethod.Get, $"/api/v1/organizations/{Uri.EscapeDataString(organizationId)}/projects/space-race");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["organizationId"], response.ErrorSources());
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_rejected()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var response = await owner.SendRawAsync(
            "createProject", HttpMethod.Post, Paths.Projects(owner.OrganizationId), "key=space-race", "application/x-www-form-urlencoded");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["Content-Type"], response.ErrorSources());
    }
}
