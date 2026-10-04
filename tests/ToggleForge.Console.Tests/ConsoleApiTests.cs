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

    [Fact]
    public async Task A_flag_change_is_a_merge_patch_with_only_the_changed_fields_and_the_etag_read()
    {
        var api = new StubApi().Answer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, NewShopJson, eTag: "\"8\"");

        var saved = await api.CreateClient().UpdateFlagAsync(
            _newShop, "\"7\"", new FlagModification(RolloutBasisPoints: 5_000), TestContext.Current.CancellationToken);

        Assert.True(saved.Succeeded);
        Assert.Equal("\"8\"", saved.Value.ETag);
        var request = Assert.Single(api.RequestsTo(HttpMethod.Patch, NewShop));
        Assert.Equal(["\"7\""], request.Headers.GetValues("If-Match"));
        Assert.Equal("application/merge-patch+json", request.Content?.Headers.ContentType?.MediaType);
        Assert.Equal("""{"rolloutBasisPoints":5000}""", api.BodyOf(request));
    }

    [Fact]
    public async Task A_json_flag_is_read_with_its_type_and_values()
    {
        var api = new StubApi().Answer(HttpMethod.Get, ShopConfig, HttpStatusCode.OK,
            """{"key":"shop-config","type":"json","enabled":true,"onValue":{ "discount" : 0.2 },"offValue":null,"rolloutBasisPoints":10000}""",
            eTag: "\"4\"");

        var flag = await api.CreateClient().GetFlagAsync(_shopConfig, TestContext.Current.CancellationToken);

        Assert.True(flag.Succeeded);
        Assert.Equal("json", flag.Value.Value.Type);
        Assert.True(flag.Value.Value.IsJson);
        Assert.Equal("""{"discount":0.2}""", flag.Value.Value.OnValue.Text);
        Assert.Equal(FlagValue.Null, flag.Value.Value.OffValue);
    }

    [Fact]
    public async Task A_json_flag_is_created_with_its_type_and_its_values_as_json()
    {
        var api = new StubApi().Answer(HttpMethod.Post, Draft + "/flags", HttpStatusCode.Created,
            """{"key":"shop-config","type":"json","enabled":true,"onValue":{"discount":0.2},"offValue":null,"rolloutBasisPoints":10000}""");

        await api.CreateClient().CreateFlagAsync(
            _organizationId, "space-race", "dev",
            new FlagCreation("shop-config", FlagTypes.Json, Enabled: true, Values.Json("""{ "discount": 0.2 }"""), FlagValue.Null, 10_000),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"key":"shop-config","type":"json","enabled":true,"onValue":{"discount":0.2},"offValue":null,"rolloutBasisPoints":10000}""",
            api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Post, Draft + "/flags"))));
    }

    [Fact]
    public async Task A_json_value_change_goes_as_json_in_the_merge_patch()
    {
        var api = new StubApi().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK,
            """{"key":"shop-config","type":"json","enabled":true,"onValue":[1,2],"offValue":null,"rolloutBasisPoints":10000}""",
            eTag: "\"5\"");

        await api.CreateClient().UpdateFlagAsync(
            _shopConfig, "\"4\"", new FlagModification(OnValue: Values.Json("[1, 2]")), TestContext.Current.CancellationToken);

        Assert.Equal("""{"onValue":[1,2]}""", api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig))));
    }

    [Fact]
    public async Task A_refused_value_keeps_the_field_the_api_pointed_to()
    {
        var api = new StubApi().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"The value nests deeper than 8 levels.","source":{"location":"body","pointer":"/offValue"}}]}
            """, "application/problem+json");

        var saved = await api.CreateClient().UpdateFlagAsync(
            _shopConfig, "\"4\"", new FlagModification(OffValue: Values.Json("[[[[[[[[[1]]]]]]]]]")), TestContext.Current.CancellationToken);

        Assert.False(saved.Succeeded);
        Assert.Equal("/offValue", Assert.Single(saved.Problem.Errors!).Source?.Pointer);
    }

    [Fact]
    public async Task Removing_a_flag_sends_the_etag_read()
    {
        var api = new StubApi().Answer(HttpMethod.Delete, NewShop, HttpStatusCode.NoContent, string.Empty);

        var problem = await api.CreateClient().DeleteFlagAsync(_newShop, "\"7\"", TestContext.Current.CancellationToken);

        Assert.Null(problem);
        Assert.Equal(["\"7\""], Assert.Single(api.RequestsTo(HttpMethod.Delete, NewShop)).Headers.GetValues("If-Match"));
    }

    [Fact]
    public async Task A_flag_read_without_etag_cannot_be_changed_later_and_is_unexpected()
    {
        var api = new StubApi().Answer(HttpMethod.Get, NewShop, HttpStatusCode.OK, NewShopJson);

        var flag = await api.CreateClient().GetFlagAsync(_newShop, TestContext.Current.CancellationToken);

        Assert.False(flag.Succeeded);
        Assert.Equal("/problems/unexpected-response", flag.Problem.Type);
    }

    [Fact]
    public async Task A_publication_without_summary_leaves_the_field_out()
    {
        const string Publications = Projects + "/space-race/environments/dev/publications";
        var api = new StubApi().Answer(HttpMethod.Post, Publications, HttpStatusCode.Created, """{"version":1}""");

        await api.CreateClient().PublishAsync(
            new EnvironmentAddress(_organizationId, "space-race", "dev"), new PublicationRequest(9, Summary: null), "key-of-sixteen-chars",
            TestContext.Current.CancellationToken);

        var request = Assert.Single(api.RequestsTo(HttpMethod.Post, Publications));
        Assert.Equal("""{"draftRevision":9}""", api.BodyOf(request));
        Assert.Equal(["key-of-sixteen-chars"], request.Headers.GetValues("Idempotency-Key"));
    }

    [Fact]
    public async Task The_next_page_of_the_history_goes_with_its_token_escaped()
    {
        const string Publications = Projects + "/space-race/environments/dev/publications";
        var api = new StubApi().Answer(HttpMethod.Get, Publications, HttpStatusCode.OK, """{"data":[],"metadata":{}}""");

        await api.CreateClient().ListPublicationsAsync(
            new EnvironmentAddress(_organizationId, "space-race", "dev"), "abc&maxPageSize=100", TestContext.Current.CancellationToken);

        Assert.Equal("?pageToken=abc%26maxPageSize%3D100", Assert.Single(api.Requests).RequestUri?.Query);
    }

    [Fact]
    public async Task A_rollback_sends_what_the_member_saw_with_its_key()
    {
        const string Rollbacks = Projects + "/space-race/environments/dev/rollbacks";
        var api = new StubApi().Answer(HttpMethod.Post, Rollbacks, HttpStatusCode.Created, """{"version":6}""");

        await api.CreateClient().RollBackAsync(
            new EnvironmentAddress(_organizationId, "space-race", "dev"), new RollbackRequest(2, 5, DiscardDraftChanges: false),
            "key-of-sixteen-chars", TestContext.Current.CancellationToken);

        var request = Assert.Single(api.RequestsTo(HttpMethod.Post, Rollbacks));
        Assert.Equal("""{"targetVersion":2,"expectedCurrentVersion":5,"discardDraftChanges":false}""", api.BodyOf(request));
        Assert.Equal(["key-of-sixteen-chars"], request.Headers.GetValues("Idempotency-Key"));
    }

    private const string Draft = Projects + "/space-race/environments/dev/draft";
    private const string ShopConfig = Draft + "/flags/shop-config";
    private static readonly FlagAddress _shopConfig = new(_organizationId, "space-race", "dev", "shop-config");

    private const string NewShop = Projects + "/space-race/environments/dev/draft/flags/new-shop";
    private const string NewShopJson = """{"key":"new-shop","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":5000}""";
    private static readonly FlagAddress _newShop = new(_organizationId, "space-race", "dev", "new-shop");

    private const string Session = """
        {"member":{"id":"9b2f0c1e-5d6a-4b7c-8e9f-1a2b3c4d5e6f","email":"owner@toggleforge.test"},
         "organizations":[{"organizationId":"3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f","name":"Studio","role":"owner"}]}
        """;
}
