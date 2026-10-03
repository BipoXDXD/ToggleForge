using System.Net;
using System.Text.Json.Nodes;
using ToggleForge.Api.Tests.Support;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Tests;

public sealed class SimulationTests(ApiFactory api)
{
    [Theory]
    [InlineData("player-42")]
    [InlineData("player-7")]
    [InlineData("jogadora-ção-🎮")]
    public async Task The_simulation_agrees_with_what_the_game_evaluates(string playerId)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var secret = (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();
        var game = GameEvaluation((await GameClient.GetSnapshotAsync(api, secret)).Json, "new-shop", playerId);

        var simulation = await owner.SimulatePublicationAsync("space-race", playerId, version: 1);

        Assert.Equal(HttpStatusCode.OK, simulation.Status);
        Assert.Equal("no-store", simulation.Header("Cache-Control"));
        Assert.Equal("publication", simulation.Json["source"]!.GetValue<string>());
        Assert.Equal(1, simulation.Json["version"]!.GetValue<long>());
        var result = Assert.Single(simulation.Json["results"]!.AsArray())!;
        Assert.Equal("new-shop", result["flagKey"]!.GetValue<string>());
        Assert.Equal(game.Value, result["value"]!.GetValue<bool>());
        Assert.Equal(game.Reason.ToString().ToLowerInvariant(), result["reason"]!.GetValue<string>());
        Assert.Equal(game.Bucket, result["bucket"]!.GetValue<int>());
    }

    [Fact]
    public async Task Simulating_the_draft_shows_the_unpublished_change()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        var revision = await owner.PublishedProjectAsync("space-race");
        await owner.UpdateFlagAsync("space-race", "new-shop", ETagOf(revision), """{"enabled":false}""");

        var draft = await owner.SimulateDraftAsync("space-race", "player-42");
        var published = await owner.SimulatePublicationAsync("space-race", "player-42", version: 1);

        Assert.Equal(HttpStatusCode.OK, draft.Status);
        Assert.Equal("draft", draft.Json["source"]!.GetValue<string>());
        Assert.Null(draft.Json["version"]);
        var disabled = Assert.Single(draft.Json["results"]!.AsArray())!;
        Assert.False(disabled["value"]!.GetValue<bool>());
        Assert.Equal("disabled", disabled["reason"]!.GetValue<string>());
        Assert.Null(disabled["bucket"]);
        Assert.NotNull(Assert.Single(published.Json["results"]!.AsArray())!["bucket"]);
    }

    [Fact]
    public async Task The_draft_and_its_publication_put_the_player_in_the_same_bucket()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");

        var draft = await owner.SimulateDraftAsync("space-race", "player-42");
        var published = await owner.SimulatePublicationAsync("space-race", "player-42", version: 1);

        Assert.Equal(published.Json["results"]!.ToJsonString(), draft.Json["results"]!.ToJsonString());
    }

    [Fact]
    public async Task Every_flag_is_simulated_in_key_order_and_extremes_have_no_bucket()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        await owner.CreateFlagAsync("space-race", "zeta-mode", rolloutBasisPoints: 5_000);
        await owner.CreateFlagAsync("space-race", "alpha-mode", rolloutBasisPoints: 0);
        await owner.CreateFlagAsync("space-race", "beta-mode", rolloutBasisPoints: 10_000);

        var simulation = await owner.SimulateDraftAsync("space-race", "player-42");

        var results = simulation.Json["results"]!.AsArray();
        Assert.Equal(["alpha-mode", "beta-mode", "zeta-mode"], results.Select(result => result!["flagKey"]!.GetValue<string>()));
        Assert.Equal(["excluded", "included"], results.Take(2).Select(result => result!["reason"]!.GetValue<string>()));
        Assert.Equal([false, true], results.Take(2).Select(result => result!["value"]!.GetValue<bool>()));
        Assert.All(results.Take(2), result => Assert.Null(result!["bucket"]));
        Assert.InRange(results[2]!["bucket"]!.GetValue<int>(), 0, 9_999);
    }

    [Fact]
    public async Task An_empty_draft_has_no_results()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var simulation = await owner.SimulateDraftAsync("space-race", "player-42");

        Assert.Equal(HttpStatusCode.OK, simulation.Status);
        Assert.Empty(simulation.Json["results"]!.AsArray());
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task A_version_the_environment_never_published_is_rejected_on_version(bool published, long version)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        if (published)
        {
            await owner.PublishedProjectAsync("space-race");
        }
        else
        {
            await owner.CreateProjectAsync("space-race");
        }

        var simulation = await owner.SimulatePublicationAsync("space-race", "player-42", version);

        Assert.Equal(HttpStatusCode.BadRequest, simulation.Status);
        Assert.Equal(["/version"], simulation.ErrorSources());
    }

    [Fact]
    public async Task A_missing_environment_is_not_found()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var simulation = await owner.SimulateDraftAsync("space-race", "player-42", environmentKey: "staging");

        Assert.Equal(HttpStatusCode.NotFound, simulation.Status);
        Assert.Equal("/problems/not-found", simulation.ProblemType);
    }

    [Theory]
    [InlineData("""{"source":"draft"}""", "/playerId")]
    [InlineData("""{"playerId":"","source":"draft"}""", "/playerId")]
    [InlineData("""{"playerId":"   ","source":"draft"}""", "/playerId")]
    [InlineData("""{"playerId":"player\u000042","source":"draft"}""", "/playerId")]
    [InlineData("""{"playerId":"player\u007f42","source":"draft"}""", "/playerId")]
    [InlineData("""{"playerId":"player\ud83c","source":"draft"}""", "/playerId")]
    [InlineData("""{"playerId":"player-42"}""", "/source")]
    [InlineData("""{"playerId":"player-42","source":"Draft"}""", "/source")]
    [InlineData("""{"playerId":"player-42","source":"draft","version":1}""", "/version")]
    [InlineData("""{"playerId":"player-42","source":"publication"}""", "/version")]
    [InlineData("""{"playerId":"player-42","source":"publication","version":0}""", "/version")]
    [InlineData("""{"playerId":"","source":"latest"}""", "/playerId,/source")]
    public async Task Invalid_simulation_fields_are_rejected_together(string json, string pointers)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");

        var simulation = await owner.SendRawAsync("simulatePlayer", HttpMethod.Post, SimulationsPath(owner), json);

        Assert.Equal(HttpStatusCode.BadRequest, simulation.Status);
        Assert.Equal(pointers.Split(','), simulation.ErrorSources());
    }

    [Theory]
    [InlineData(256, HttpStatusCode.OK)]
    [InlineData(257, HttpStatusCode.BadRequest)]
    public async Task A_player_id_has_up_to_256_characters(int length, HttpStatusCode expected)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");

        var simulation = await owner.SimulateDraftAsync("space-race", new string('p', length));

        Assert.Equal(expected, simulation.Status);
    }

    [Fact]
    public async Task A_viewer_simulates_the_draft_and_publications()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        using var viewer = await ConsoleClient.SignUpAsync(api);
        await api.AddMembershipAsync(owner.OrganizationId, viewer.MemberId, "viewer");

        var draft = await viewer.SimulateDraftAsync("space-race", "player-42", organizationId: owner.OrganizationId);
        var published = await viewer.SimulatePublicationAsync("space-race", "player-42", 1, organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.OK, draft.Status);
        Assert.Equal(HttpStatusCode.OK, published.Status);
    }

    /// <summary>O ID do jogador pode ser dado pessoal do estúdio: não volta na resposta nem vai para o log.</summary>
    [Fact]
    public async Task The_player_id_never_reaches_the_response_or_the_logs()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var canary = $"CANARY-{Guid.NewGuid()}";

        var simulation = await owner.SimulatePublicationAsync("space-race", canary, version: 1);

        Assert.Equal(HttpStatusCode.OK, simulation.Status);
        Assert.DoesNotContain(canary, simulation.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(canary, StringComparison.Ordinal));
    }

    /// <summary>O que o SDK calcularia com o snapshot que o jogo baixou.</summary>
    private static EvaluationResult<bool> GameEvaluation(JsonNode snapshot, string flagKey, string playerId)
    {
        var flags = snapshot["flags"]!.AsArray().Select(flag => new BooleanFlag(
            FlagKey.Parse(flag!["key"]!.GetValue<string>()),
            flag["enabled"]!.GetValue<bool>(),
            flag["onValue"]!.GetValue<bool>(),
            flag["offValue"]!.GetValue<bool>(),
            new PercentageRollout(
                RolloutPercentage.FromBasisPoints(flag["rolloutBasisPoints"]!.GetValue<int>()),
                RolloutSeed.Parse(flag["seed"]!.GetValue<string>()))));
        var configuration = new ConfigurationSnapshot(
            ProjectKey.Parse(snapshot["projectKey"]!.GetValue<string>()),
            EnvironmentKey.Parse(snapshot["environmentKey"]!.GetValue<string>()),
            snapshot["version"]!.GetValue<long>(),
            flags);
        return new FlagEvaluator().EvaluateBoolean(
            configuration, flagKey, EvaluationContext.ForPlayer(PlayerId.Parse(playerId)), defaultValue: false);
    }

    private static string ETagOf(long revision) => $"\"{revision}\"";

    private static string SimulationsPath(ConsoleClient owner) =>
        $"{Paths.Environment(owner.OrganizationId, "space-race", "dev")}/simulations";
}
