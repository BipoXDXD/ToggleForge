using System.Net;
using System.Text;
using System.Text.Json;
using ToggleForge.Api.Tests.Support;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Tests;

/// <summary>
/// As flags JSON pela API (ADR 0011): criação, alteração, publicação, simulação e snapshot, com os limites do canvas
/// <c>docs/api/configuracoes-json.md</c> (T4, T5, D3, D4).
/// </summary>
public sealed class JsonFlagTests(ApiFactory api)
{
    private const string Project = "space-race";
    private const int JsonBudgetBytes = 128 * 1024;
    private const string JsonBudgetReached = "/problems/json-budget-reached";

    [Fact]
    public async Task A_json_flag_is_created_with_its_values_compacted()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", """{ "discount" : 0.2, "items" : ["sword"] }""", """{"discount":0}""");

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal("json", response.Json["type"]!.GetValue<string>());
        Assert.Equal("""{"discount":0.2,"items":["sword"]}""", RawValue(response, "onValue"));
        Assert.Equal("""{"discount":0}""", RawValue(response, "offValue"));
    }

    [Fact]
    public async Task A_flag_without_a_type_is_boolean()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateFlagAsync(Project, "new-shop");

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal("boolean", response.Json["type"]!.GetValue<string>());
        Assert.Equal("true", RawValue(response, "onValue"));
        Assert.Equal("false", RawValue(response, "offValue"));
    }

    [Fact]
    public async Task A_json_flag_takes_true_null_and_scalars_as_values()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", "true", "null");
        var scalars = await owner.CreateJsonFlagAsync(Project, "shop-title", "\"Summer sale\"", "-1.5");

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal("true", RawValue(response, "onValue"));
        Assert.Equal("null", RawValue(response, "offValue"));
        Assert.Equal(HttpStatusCode.Created, scalars.Status);
        Assert.Equal("\"Summer sale\"", RawValue(scalars, "onValue"));
        Assert.Equal("-1.5", RawValue(scalars, "offValue"));
    }

    [Theory]
    [InlineData("onValue", "1")]
    [InlineData("onValue", "\"true\"")]
    [InlineData("onValue", "null")]
    [InlineData("onValue", "{}")]
    [InlineData("offValue", "0")]
    [InlineData("offValue", "\"false\"")]
    [InlineData("offValue", "null")]
    [InlineData("offValue", "[]")]
    public async Task A_boolean_flag_refuses_a_value_that_is_not_true_or_false(string field, string value)
    {
        using var owner = await OwnerWithProjectAsync();
        var onValue = field == "onValue" ? value : "true";
        var offValue = field == "offValue" ? value : "false";

        var response = await CreateRawAsync(
            owner, $$"""{"key":"new-shop","type":"boolean","enabled":true,"onValue":{{onValue}},"offValue":{{offValue}},"rolloutBasisPoints":100}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("/problems/invalid-request", response.ProblemType);
        Assert.Equal([$"/{field}"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    public static TheoryData<string> InvalidJsonValues =>
    [
        "[[[[[[[[[1]]]]]]]]]",
        "1e400",
        ConsoleClient.JsonTextOf(4_097),
    ];

    [Theory]
    [MemberData(nameof(InvalidJsonValues))]
    public async Task An_invalid_json_value_is_rejected_on_its_field(string value)
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", value, "null");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/onValue"], response.ErrorSources());
        Assert.Equal("INVALID_VALUE", response.Json["errors"]![0]!["type"]!.GetValue<string>());
        await AssertDraftIsEmptyAsync(owner);
    }

    /// <summary>
    /// Quem recusa é o binding (<c>AllowDuplicateProperties = false</c> nas opções da API), antes do domínio; o
    /// <c>DuplicateKey</c> do <c>JsonFlagValue</c> fica como defesa em profundidade.
    /// </summary>
    [Fact]
    public async Task A_duplicated_key_is_rejected_at_any_depth()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":{"b":1,"b":2}}""", "null");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("INVALID_BODY", response.Json["errors"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(["/onValue"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    [Theory]
    [InlineData("\"color\"")]
    [InlineData("\"Boolean\"")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("\"\\ud800\"")]
    public async Task An_unknown_type_is_rejected(string type)
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await CreateRawAsync(
            owner, $$"""{"key":"shop-config","type":{{type}},"enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":100}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/type"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    [Fact]
    public async Task A_null_type_is_rejected()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await CreateRawAsync(
            owner, """{"key":"shop-config","type":null,"enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":100}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/type"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    [Fact]
    public async Task A_flag_with_an_unknown_type_reports_only_the_type()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await CreateRawAsync(
            owner, """{"key":"shop-config","type":"color","enabled":true,"onValue":{},"offValue":false,"rolloutBasisPoints":100}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/type"], response.ErrorSources());
    }

    [Fact]
    public async Task Every_invalid_field_of_a_creation_is_reported_together()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await CreateRawAsync(
            owner, """{"key":"shop-config","type":"json","enabled":true,"onValue":[[[[[[[[[1]]]]]]]]],"rolloutBasisPoints":100}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/offValue", "/onValue"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    [Fact]
    public async Task Changing_the_type_of_a_flag_is_rejected_and_nothing_is_saved()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateFlagAsync(Project, "new-shop");
        var before = await owner.GetFlagAsync(Project, "new-shop");

        var response = await owner.UpdateFlagAsync(Project, "new-shop", created.Header("ETag"), """{"type":"json"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("INVALID_BODY", response.Json["errors"]![0]!["type"]!.GetValue<string>());
        await AssertFlagIsUnchangedAsync(owner, "new-shop", before);
    }

    [Fact]
    public async Task A_json_flag_takes_a_new_value_through_a_patch()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":1}""", "null");

        var response = await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), """{"onValue":[1, 2]}""");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("[1,2]", RawValue(response, "onValue"));
        Assert.Equal("null", RawValue(response, "offValue"));
        Assert.NotEqual(created.Header("ETag"), response.Header("ETag"));
        Assert.Equal("[1,2]", RawValue(await owner.GetFlagAsync(Project, "shop-config"), "onValue"));
    }

    /// <summary>
    /// A chave repetida e o aninhamento além do teto do leitor (64 níveis) são recusados pelo binding, como na criação,
    /// e o erro aponta o campo do patch, nunca o corpo inteiro.
    /// </summary>
    [Theory]
    [InlineData("onValue", """{"a":{"b":1,"b":2}}""")]
    [InlineData("offValue", """{"a":{"b":1,"b":2}}""")]
    [InlineData("onValue", "[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]")]
    [InlineData("offValue", "[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]")]
    public async Task A_value_the_binding_refuses_in_a_patch_points_at_its_field(string field, string value)
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":1}""", """{"a":0}""");
        var before = await owner.GetFlagAsync(Project, "shop-config");

        var response = await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), $$"""{"{{field}}":{{value}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("INVALID_BODY", response.Json["errors"]![0]!["type"]!.GetValue<string>());
        Assert.Equal([$"/{field}"], response.ErrorSources());
        await AssertFlagIsUnchangedAsync(owner, "shop-config", before);
    }

    [Theory]
    [InlineData("onValue")]
    [InlineData("offValue")]
    public async Task A_patch_cannot_set_the_json_null_and_changes_nothing(string field)
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":1}""", """{"a":0}""");
        var before = await owner.GetFlagAsync(Project, "shop-config");

        var response = await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), $$"""{"{{field}}":null}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal([$"/{field}"], response.ErrorSources());
        await AssertFlagIsUnchangedAsync(owner, "shop-config", before);
    }

    [Theory]
    [InlineData("onValue", "1")]
    [InlineData("onValue", "\"true\"")]
    [InlineData("onValue", "{}")]
    [InlineData("offValue", "1")]
    [InlineData("offValue", "\"true\"")]
    [InlineData("offValue", "{}")]
    public async Task A_boolean_flag_refuses_a_patch_with_a_value_that_is_not_true_or_false(string field, string value)
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateFlagAsync(Project, "new-shop");
        var before = await owner.GetFlagAsync(Project, "new-shop");

        var response = await owner.UpdateFlagAsync(Project, "new-shop", created.Header("ETag"), $$"""{"{{field}}":{{value}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("/problems/invalid-request", response.ProblemType);
        Assert.Equal([$"/{field}"], response.ErrorSources());
        await AssertFlagIsUnchangedAsync(owner, "new-shop", before);
    }

    /// <summary>Espaço em branco não é mudança (canvas T3): nem revisão nova nem ETag nova para outro editor.</summary>
    [Fact]
    public async Task A_patch_that_only_reformats_a_json_value_keeps_the_etag()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":1}""", "null");
        var environmentBefore = await owner.GetEnvironmentAsync(Project, "dev");

        var response = await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), """{"onValue":{ "a" : 1 }}""");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(created.Header("ETag"), response.Header("ETag"));
        var environmentAfter = await owner.GetEnvironmentAsync(Project, "dev");
        Assert.Equal(environmentBefore.Json["draftRevision"]!.GetValue<long>(), environmentAfter.Json["draftRevision"]!.GetValue<long>());
        Assert.Equal(
            environmentBefore.Json["hasUnpublishedChanges"]!.GetValue<bool>(), environmentAfter.Json["hasUnpublishedChanges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_published_json_flag_reaches_the_snapshot_as_json()
    {
        using var owner = await OwnerWithProjectAsync();
        await owner.CreateFlagAsync(Project, "new-shop");
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"discount":0.2}""", "[]");
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey())).Status);
        var secret = (await owner.CreateCredentialAsync(Project)).Json["secret"]!.GetValue<string>();

        var snapshot = await GameClient.GetSnapshotAsync(api, secret);

        Assert.Equal(HttpStatusCode.OK, snapshot.Status);
        var json = SnapshotFlag(snapshot, "shop-config");
        Assert.Equal("json", json.GetProperty("type").GetString());
        Assert.Equal("""{"discount":0.2}""", json.GetProperty("onValue").GetRawText());
        Assert.Equal("[]", json.GetProperty("offValue").GetRawText());
        var boolean = SnapshotFlag(snapshot, "new-shop");
        Assert.Equal("boolean", boolean.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.True, boolean.GetProperty("onValue").ValueKind);
        Assert.Equal(JsonValueKind.False, boolean.GetProperty("offValue").ValueKind);
    }

    [Fact]
    public async Task The_simulation_returns_the_json_value_the_player_gets()
    {
        using var owner = await OwnerWithProjectAsync();
        await owner.CreateJsonFlagAsync(Project, "shop-config", """{"discount":0.2}""", "null", rolloutBasisPoints: 10_000);

        var response = await owner.SimulateDraftAsync(Project, "player-42");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        using var body = JsonDocument.Parse(response.Body);
        var result = Assert.Single(body.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal("json", result.GetProperty("type").GetString());
        Assert.Equal("""{"discount":0.2}""", result.GetProperty("value").GetRawText());
        Assert.Equal("included", result.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Each_simulated_flag_reports_its_own_type()
    {
        using var owner = await OwnerWithProjectAsync();
        await owner.CreateFlagAsync(Project, "new-shop");
        await owner.CreateJsonFlagAsync(Project, "shop-config", """{"discount":0.2}""", "null");

        var response = await owner.SimulateDraftAsync(Project, "player-42");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("boolean", SimulationResult(response, "new-shop").GetProperty("type").GetString());
        Assert.Equal("json", SimulationResult(response, "shop-config").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_disabled_json_flag_simulates_to_its_off_value_without_a_bucket()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"discount":0.2}""", """{"discount":0}""");
        Assert.Equal(HttpStatusCode.OK, (await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), """{"enabled":false}""")).Status);

        var response = await owner.SimulateDraftAsync(Project, "player-42");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = SimulationResult(response, "shop-config");
        Assert.Equal("""{"discount":0}""", result.GetProperty("value").GetRawText());
        Assert.Equal("disabled", result.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("bucket").ValueKind);
    }

    /// <summary>
    /// O jogo calcula o bucket só com projeto, ambiente, chave, seed e jogador (ADR 0002): o tipo da flag não entra no
    /// hash, então a simulação de uma flag JSON num rollout parcial tem de cair no mesmo bucket e no mesmo valor. O seed
    /// é sorteado na criação, então os jogadores são escolhidos pelo resultado do jogo, um de cada lado do rollout.
    /// </summary>
    [Fact]
    public async Task A_json_flag_in_a_partial_rollout_simulates_what_the_game_evaluates()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"discount":0.2}""", """{"discount":0}""", rolloutBasisPoints: 5_000);
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey())).Status);
        var secret = (await owner.CreateCredentialAsync(Project)).Json["secret"]!.GetValue<string>();
        var snapshot = await GameClient.GetSnapshotAsync(api, secret);
        var candidates = Enumerable.Range(1, 200)
            .Select(number => $"player-{number}")
            .Select(playerId => (PlayerId: playerId, Game: GameEvaluation(snapshot, "shop-config", playerId)))
            .ToList();

        // Com 50% de rollout, 200 candidatos todos do mesmo lado têm chance de 2 × 2^-200.
        var included = candidates.Find(candidate => candidate.Game.Reason == EvaluationReason.Included);
        var excluded = candidates.Find(candidate => candidate.Game.Reason == EvaluationReason.Excluded);

        Assert.NotNull(included.PlayerId);
        Assert.NotNull(excluded.PlayerId);
        await AssertSimulationMatchesGameAsync(owner, included.PlayerId, included.Game, "included");
        await AssertSimulationMatchesGameAsync(owner, excluded.PlayerId, excluded.Game, "excluded");
    }

    [Fact]
    public async Task Rolling_back_restores_the_json_value()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":1}""", "null");
        await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey());
        var changed = await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), """{"onValue":{"a":2}}""");
        await owner.PublishAsync(Project, ConsoleClient.Revision(changed), ConsoleClient.NewIdempotencyKey());
        var secret = (await owner.CreateCredentialAsync(Project)).Json["secret"]!.GetValue<string>();

        var rollback = await owner.RollBackAsync(Project, 1, 2, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Created, rollback.Status);
        Assert.Equal("""{"a":1}""", SnapshotFlag(await GameClient.GetSnapshotAsync(api, secret), "shop-config").GetProperty("onValue").GetRawText());
        Assert.Equal("""{"a":1}""", RawValue(await owner.GetFlagAsync(Project, "shop-config"), "onValue"));
    }

    [Fact]
    public async Task A_draft_holds_at_most_128_KiB_of_json_values()
    {
        using var owner = await OwnerWithProjectAsync();
        await owner.FillJsonBudgetAsync(Project, JsonBudgetBytes);

        var response = await owner.CreateJsonFlagAsync(Project, "one-too-many", "0", "0");

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal(JsonBudgetReached, response.ProblemType);
        Assert.Equal(16, (await owner.GetDraftAsync(Project)).Json["flags"]!.AsArray().Count);
    }

    [Fact]
    public async Task A_change_past_the_json_budget_gets_409_and_changes_nothing()
    {
        using var owner = await OwnerWithProjectAsync();
        var growing = await owner.CreateJsonFlagAsync(Project, "growing", "0", "0");
        await owner.FillJsonBudgetAsync(Project, JsonBudgetBytes - 2);
        var before = await owner.GetFlagAsync(Project, "growing");

        var response = await owner.UpdateFlagAsync(Project, "growing", growing.Header("ETag"), """{"onValue":10}""");

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal(JsonBudgetReached, response.ProblemType);
        await AssertFlagIsUnchangedAsync(owner, "growing", before);
    }

    public static TheoryData<string> UnusualValues =>
    [
        "\"\\u0000\"",
        "\"a\\nb\"",
        "\"😀\"",
        "\" \"",
        "-0",
        "1.7976931348623157e308",
        "1e-320",
        "[[[[[[[[1]]]]]]]]",
        """{"":1}""",
        """{"a/b~c":1}""",
        "\"maçã\"",
    ];

    [Theory]
    [MemberData(nameof(UnusualValues))]
    public async Task Unusual_but_valid_json_values_round_trip_unchanged(string value)
    {
        using var owner = await OwnerWithProjectAsync();
        using var sent = JsonDocument.Parse(value);

        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", value, "null");
        var read = await owner.GetFlagAsync(Project, "shop-config");
        var published = await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey());
        var secret = (await owner.CreateCredentialAsync(Project)).Json["secret"]!.GetValue<string>();
        var snapshot = await GameClient.GetSnapshotAsync(api, secret);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(HttpStatusCode.Created, published.Status);
        Assert.Equal(HttpStatusCode.OK, snapshot.Status);
        AssertSameJson(sent.RootElement, Value(created, "onValue"));
        AssertSameJson(sent.RootElement, Value(read, "onValue"));
        AssertSameJson(sent.RootElement, SnapshotFlag(snapshot, "shop-config").GetProperty("onValue"));
    }

    [Fact]
    public async Task A_value_nested_past_the_reader_depth_gets_400()
    {
        using var owner = await OwnerWithProjectAsync();
        const int ReaderMaxDepth = 64;
        var nested = new string('[', ReaderMaxDepth + 1) + new string(']', ReaderMaxDepth + 1);

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", nested, "null");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/onValue"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    [Fact]
    public async Task A_body_that_is_not_utf8_gets_400()
    {
        using var owner = await OwnerWithProjectAsync();
        var prefix = Encoding.UTF8.GetBytes("""{"key":"shop-config","type":"json","enabled":true,"onValue":"a""");
        var suffix = Encoding.UTF8.GetBytes("""b","offValue":null,"rolloutBasisPoints":100}""");
        using var content = new ByteArrayContent([.. prefix, 0xC3, 0x28, .. suffix]);
        content.Headers.ContentType = new("application/json");

        var response = await owner.SendAsync("createFlag", HttpMethod.Post, FlagsPath(owner), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/onValue"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    /// <summary>A ADR 0010 do SDK conta com esta recusa: a string não existe em UTF-16 válido.</summary>
    [Theory]
    [InlineData("\"\\ud800\"")]
    [InlineData("""{"\ud800":1}""")]
    public async Task A_lone_surrogate_in_a_json_value_gets_400(string value)
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", value, "null");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/onValue"], response.ErrorSources());
        await AssertDraftIsEmptyAsync(owner);
    }

    /// <summary>Canvas M6: o ponteiro não reflete chaves escolhidas pelo cliente.</summary>
    [Fact]
    public async Task An_invalid_body_inside_a_value_points_at_the_field()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"<b>x</b>":1,"<b>x</b>":2}""", "null");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/onValue"], response.ErrorSources());
        Assert.DoesNotContain("<b>", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_field_with_markup_is_not_echoed_in_the_pointer()
    {
        using var owner = await OwnerWithProjectAsync();

        var response = await CreateRawAsync(
            owner, """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":100,"<b>x</b>":1}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal([string.Empty], response.ErrorSources());
        Assert.DoesNotContain("<b>", response.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// STRIDE I1: a <c>JsonException</c> da chave repetida cita a chave. O System.Text.Json corta o nome em 15
    /// caracteres na mensagem, então o canário cabe nesse corte para que um log da exceção o mostre.
    /// </summary>
    [Theory]
    [InlineData("createFlag")]
    [InlineData("updateFlag")]
    public async Task A_canary_in_a_repeated_key_never_reaches_the_logs_or_the_response(string operation)
    {
        using var owner = await OwnerWithProjectAsync();
        var canary = $"CANARY{Guid.NewGuid():N}"[..14];
        var repeated = $$"""{"{{canary}}":1,"{{canary}}":2}""";

        var response = operation == "createFlag"
            ? await owner.CreateJsonFlagAsync(Project, "shop-config", repeated, "null")
            : await PatchOnValueAsync(owner, repeated);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(["/onValue"], response.ErrorSources());
        Assert.DoesNotContain(canary, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(canary, StringComparison.Ordinal));
    }

    private async Task<ConsoleClient> OwnerWithProjectAsync()
    {
        var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync(Project)).Status);
        return owner;
    }

    private static async Task<ApiResponse> PatchOnValueAsync(ConsoleClient owner, string onValue)
    {
        var created = await owner.CreateJsonFlagAsync(Project, "shop-config", """{"a":1}""", "null");
        Assert.Equal(HttpStatusCode.Created, created.Status);
        return await owner.UpdateFlagAsync(Project, "shop-config", created.Header("ETag"), $$"""{"onValue":{{onValue}}}""");
    }

    private static Task<ApiResponse> CreateRawAsync(ConsoleClient owner, string json) =>
        owner.SendRawAsync("createFlag", HttpMethod.Post, FlagsPath(owner), json);

    private static string FlagsPath(ConsoleClient owner) => $"{Paths.Environment(owner.OrganizationId, Project, "dev")}/draft/flags";

    private static async Task AssertDraftIsEmptyAsync(ConsoleClient owner) =>
        Assert.Empty((await owner.GetDraftAsync(Project)).Json["flags"]!.AsArray());

    private static async Task AssertFlagIsUnchangedAsync(ConsoleClient owner, string flagKey, ApiResponse before)
    {
        var after = await owner.GetFlagAsync(Project, flagKey);
        Assert.Equal(before.Header("ETag"), after.Header("ETag"));
        Assert.Equal(before.Body, after.Body);
    }

    private static JsonElement Value(ApiResponse response, string property)
    {
        using var body = JsonDocument.Parse(response.Body);
        return body.RootElement.GetProperty(property).Clone();
    }

    /// <summary>O valor como o servidor o escreveu, que é o texto compacto.</summary>
    private static string RawValue(ApiResponse response, string property) => Value(response, property).GetRawText();

    private static JsonElement SnapshotFlag(ApiResponse snapshot, string flagKey)
    {
        using var body = JsonDocument.Parse(snapshot.Body);
        return body.RootElement.GetProperty("flags").EnumerateArray()
            .Single(flag => flag.GetProperty("key").GetString() == flagKey)
            .Clone();
    }

    private static void AssertSameJson(JsonElement expected, JsonElement actual) =>
        Assert.True(JsonElement.DeepEquals(expected, actual), $"Expected {expected.GetRawText()}, got {actual.GetRawText()}.");

    /// <summary>A simulação da publicação 1 para o jogador dá o valor, o motivo e o bucket do jogo, e o motivo esperado.</summary>
    private static async Task AssertSimulationMatchesGameAsync(ConsoleClient owner, string playerId, EvaluationResult<string> game, string expectedReason)
    {
        var response = await owner.SimulatePublicationAsync(Project, playerId, version: 1);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = SimulationResult(response, "shop-config");
        Assert.Equal(game.Value, result.GetProperty("value").GetRawText());
        Assert.Equal(game.Reason.ToString().ToLowerInvariant(), result.GetProperty("reason").GetString());
        Assert.Equal(game.Bucket, result.GetProperty("bucket").GetInt32());
        Assert.Equal(expectedReason, result.GetProperty("reason").GetString());
    }

    private static JsonElement SimulationResult(ApiResponse simulation, string flagKey)
    {
        using var body = JsonDocument.Parse(simulation.Body);
        return body.RootElement.GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("flagKey").GetString() == flagKey)
            .Clone();
    }

    /// <summary>O que o SDK calcularia para a flag JSON com o snapshot que o jogo baixou; o valor sai como texto JSON.</summary>
    private static EvaluationResult<string> GameEvaluation(ApiResponse snapshot, string flagKey, string playerId)
    {
        using var body = JsonDocument.Parse(snapshot.Body);
        var root = body.RootElement;
        var flags = root.GetProperty("flags").EnumerateArray()
            .Where(flag => flag.GetProperty("type").GetString() == "json")
            .Select(flag => new JsonFlag(
                FlagKey.Parse(flag.GetProperty("key").GetString()!),
                flag.GetProperty("enabled").GetBoolean(),
                flag.GetProperty("onValue").GetRawText(),
                flag.GetProperty("offValue").GetRawText(),
                new PercentageRollout(
                    RolloutPercentage.FromBasisPoints(flag.GetProperty("rolloutBasisPoints").GetInt32()),
                    RolloutSeed.Parse(flag.GetProperty("seed").GetString()!))))
            .ToList();
        var configuration = new ConfigurationSnapshot(
            ProjectKey.Parse(root.GetProperty("projectKey").GetString()!),
            EnvironmentKey.Parse(root.GetProperty("environmentKey").GetString()!),
            root.GetProperty("version").GetInt64(),
            flags);
        return new FlagEvaluator().EvaluateJson(
            configuration, flagKey, EvaluationContext.ForPlayer(PlayerId.Parse(playerId)), defaultValue: "null", text => text);
    }
}
