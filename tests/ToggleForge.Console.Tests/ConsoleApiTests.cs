using System.Net;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Tests;

public sealed class ConsoleApiTests
{
    private const string Projects = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects";
    private static readonly Guid _organizationId = Guid.Parse("3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f");

    [Fact]
    public async Task Every_mutation_sends_the_antiforgery_token_fetched_once_per_session()
    {
        var api = new StubApi().Answer(HttpMethod.Post, Projects, HttpStatusCode.Created, """{"key":"space-race","name":"Space Race","environments":[]}""");
        var console = api.CreateClient();

        await console.CreateProjectAsync(_organizationId, new ProjectCreation("space-race", "Space Race"), TestContext.Current.CancellationToken);
        await console.CreateProjectAsync(_organizationId, new ProjectCreation("space-race", "Space Race"), TestContext.Current.CancellationToken);

        Assert.Single(api.RequestsTo(HttpMethod.Get, "/api/v1/antiforgery-token"));
        Assert.All(api.RequestsTo(HttpMethod.Post, Projects), request =>
            Assert.Equal([StubApi.AntiforgeryToken], request.Headers.GetValues("X-XSRF-TOKEN")));
    }

    [Fact]
    public async Task Reads_go_without_the_antiforgery_token()
    {
        var api = new StubApi().Answer(HttpMethod.Get, Projects, HttpStatusCode.OK, """{"data":[]}""");

        await api.CreateClient().ListProjectsAsync(_organizationId, TestContext.Current.CancellationToken);

        Assert.Empty(api.RequestsTo(HttpMethod.Get, "/api/v1/antiforgery-token"));
        Assert.False(Assert.Single(api.RequestsTo(HttpMethod.Get, Projects)).Headers.Contains("X-XSRF-TOKEN"));
    }

    [Fact]
    public async Task Signing_in_makes_the_next_mutation_fetch_a_token_for_the_new_session()
    {
        var api = new StubApi()
            .Answer(HttpMethod.Post, "/api/v1/session", HttpStatusCode.OK, Session)
            .Answer(HttpMethod.Post, Projects, HttpStatusCode.Created, """{"key":"space-race","name":"Space Race","environments":[]}""");
        var console = api.CreateClient();
        var signedIn = await console.SignInAsync(new Credentials("owner@toggleforge.test", "a password"), TestContext.Current.CancellationToken);

        Assert.True(signedIn.Succeeded);
        api.Session.Start(signedIn.Value);
        await console.CreateProjectAsync(_organizationId, new ProjectCreation("space-race", "Space Race"), TestContext.Current.CancellationToken);

        Assert.Equal(2, api.RequestsTo(HttpMethod.Get, "/api/v1/antiforgery-token").Count());
    }

    [Fact]
    public async Task A_401_ends_the_session()
    {
        var api = new StubApi().Answer(HttpMethod.Get, Projects, HttpStatusCode.Unauthorized, """{"type":"about:blank","title":"Unauthorized","status":401}""");
        api.Session.Start(new Api.Session(new Member(Guid.NewGuid(), "owner@toggleforge.test"), []));
        var ended = 0;
        api.Session.Ended += () => ended++;

        var projects = await api.CreateClient().ListProjectsAsync(_organizationId, TestContext.Current.CancellationToken);

        Assert.False(projects.Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, projects.Problem.StatusCode);
        Assert.Null(api.Session.Current);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task The_problem_of_the_api_reaches_the_page_with_every_error()
    {
        var api = new StubApi().Answer(HttpMethod.Post, Projects, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-request","title":"Invalid request","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"Keys have 1 to 64 characters.","source":{"location":"body","pointer":"/key"}},
                       {"type":"INVALID_VALUE","detail":"Names have 1 to 100 characters.","source":{"location":"body","pointer":"/name"}}]}
            """, "application/problem+json");

        var project = await api.CreateClient().CreateProjectAsync(_organizationId, new ProjectCreation("", ""), TestContext.Current.CancellationToken);

        Assert.False(project.Succeeded);
        Assert.Equal(HttpStatusCode.BadRequest, project.Problem.StatusCode);
        Assert.Equal(["Keys have 1 to 64 characters.", "Names have 1 to 100 characters."], project.Problem.Messages);
    }

    [Fact]
    public async Task A_body_that_is_not_the_contract_becomes_an_unexpected_response()
    {
        var api = new StubApi().Answer(HttpMethod.Get, Projects, HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html");

        var projects = await api.CreateClient().ListProjectsAsync(_organizationId, TestContext.Current.CancellationToken);

        Assert.False(projects.Succeeded);
        Assert.Equal("/problems/unexpected-response", projects.Problem.Type);
        Assert.Equal(HttpStatusCode.BadGateway, projects.Problem.StatusCode);
    }

    [Fact]
    public async Task A_network_failure_becomes_an_unreachable_problem()
    {
        var api = new StubApi().Fail(HttpMethod.Get, Projects);

        var projects = await api.CreateClient().ListProjectsAsync(_organizationId, TestContext.Current.CancellationToken);

        Assert.Same(Problem.Unreachable, projects.Problem);
    }

    [Fact]
    public async Task A_mutation_stops_when_the_antiforgery_token_cannot_be_fetched()
    {
        var api = new StubApi().Fail(HttpMethod.Get, "/api/v1/antiforgery-token");

        var project = await api.CreateClient().CreateProjectAsync(_organizationId, new ProjectCreation("space-race", "Space Race"), TestContext.Current.CancellationToken);

        Assert.Same(Problem.Unreachable, project.Problem);
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Projects));
    }

    [Fact]
    public async Task Keys_from_the_address_bar_are_escaped_in_the_path()
    {
        var api = new StubApi();

        await api.CreateClient().GetDraftAsync(_organizationId, "../../accounts", "dev", TestContext.Current.CancellationToken);

        var request = Assert.Single(api.Requests);
        Assert.Equal($"{Projects}/..%2F..%2Faccounts/environments/dev/draft", request.RequestUri?.AbsolutePath);
    }

    private const string Session = """
        {"member":{"id":"9b2f0c1e-5d6a-4b7c-8e9f-1a2b3c4d5e6f","email":"owner@toggleforge.test"},
         "organizations":[{"organizationId":"3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f","name":"Studio","role":"owner"}]}
        """;
}
