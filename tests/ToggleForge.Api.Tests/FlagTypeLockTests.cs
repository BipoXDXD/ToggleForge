using System.Net;
using System.Text.Json;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Uma chave publicada num ambiente mantém o tipo em toda publicação seguinte (ADR 0011, canvas T4r): um build no ar
/// lê a chave com a classe dele. Mudar o tipo exige uma chave nova.
/// </summary>
public sealed class FlagTypeLockTests(ApiFactory api)
{
    private const string Project = "space-race";
    private const string Key = "shop-config";
    private const string FlagTypeLocked = "/problems/flag-type-locked";
    private const int Racers = 8;

    [Theory]
    [InlineData("boolean", "json")]
    [InlineData("json", "boolean")]
    public async Task Recreating_a_published_key_with_another_type_gets_409_and_nothing_is_saved(string publishedType, string otherType)
    {
        using var owner = await OwnerWithProjectAsync();
        await PublishThenDeleteAsync(owner, publishedType);
        var draftBefore = (await owner.GetDraftAsync(Project)).Body;

        var response = await CreateAsync(owner, otherType);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal(FlagTypeLocked, response.ProblemType);
        Assert.Equal(DetailFor(publishedType), response.Json["detail"]!.GetValue<string>());
        Assert.Equal(draftBefore, (await owner.GetDraftAsync(Project)).Body);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetFlagAsync(Project, Key)).Status);
    }

    [Theory]
    [InlineData("boolean")]
    [InlineData("json")]
    public async Task A_deleted_key_comes_back_and_publishes_with_its_published_type(string publishedType)
    {
        using var owner = await OwnerWithProjectAsync();
        await PublishThenDeleteAsync(owner, publishedType);

        var recreated = await CreateAsync(owner, publishedType);
        var published = await owner.PublishAsync(Project, ConsoleClient.Revision(recreated), ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Created, recreated.Status);
        Assert.Equal(HttpStatusCode.Created, published.Status);
        Assert.Equal(publishedType, (await SnapshotFlagAsync(owner, Key)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Another_environment_publishes_the_key_with_another_type()
    {
        using var owner = await OwnerWithProjectAsync();
        await PublishThenDeleteAsync(owner, "boolean");
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateEnvironmentAsync(Project, "staging")).Status);

        var created = await owner.CreateJsonFlagAsync(Project, Key, """{"a":1}""", "null", environmentKey: "staging");
        var published = await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey(), environmentKey: "staging");

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(HttpStatusCode.Created, published.Status);
    }

    [Fact]
    public async Task A_new_key_takes_the_other_type()
    {
        using var owner = await OwnerWithProjectAsync();
        await PublishThenDeleteAsync(owner, "boolean");

        var created = await owner.CreateJsonFlagAsync(Project, $"{Key}-v2", """{"a":1}""", "null");
        var published = await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(HttpStatusCode.Created, published.Status);
    }

    /// <summary>
    /// Defesa em profundidade: um rascunho com a chave no outro tipo (gravado por fora da API, ou de antes da regra)
    /// não chega aos jogos. O check da criação não roda aqui, então quem recusa é a publicação.
    /// </summary>
    [Fact]
    public async Task Publishing_a_draft_that_holds_a_key_with_another_type_gets_409_and_publishes_nothing()
    {
        using var owner = await OwnerWithProjectAsync();
        var created = await CreateAsync(owner, "boolean");
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey())).Status);
        await api.ExecuteSqlAsync(
            $$"""
            update authoring.draft_flags flag set type = 'json', on_value = '{"a":1}', off_value = 'null'
            from authoring.environments environment join authoring.projects project on project.id = environment.project_id
            where flag.environment_id = environment.id and project.organization_id = '{{owner.OrganizationId}}' and flag.key = '{{Key}}'
            """);
        var draft = await owner.GetDraftAsync(Project);

        var response = await owner.PublishAsync(Project, draft.Json["revision"]!.GetValue<long>(), ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal(FlagTypeLocked, response.ProblemType);
        Assert.Equal(DetailFor("boolean"), response.Json["detail"]!.GetValue<string>());
        Assert.Equal(1, (await owner.GetEnvironmentAsync(Project, "dev")).Json["currentVersion"]!.GetValue<long>());
        Assert.Equal("boolean", (await SnapshotFlagAsync(owner, Key)).GetProperty("type").GetString());
    }

    /// <summary>
    /// O rollback também cria uma publicação. Uma versão antiga com a chave apagada depois volta com o tipo travado, sem
    /// gravar a trava de novo.
    /// </summary>
    [Fact]
    public async Task Rolling_back_to_a_version_with_a_deleted_key_publishes_it_with_its_type()
    {
        using var owner = await OwnerWithProjectAsync();
        await PublishThenDeleteAsync(owner, "json");
        var draft = await owner.GetDraftAsync(Project);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync(Project, "other-flag")).Status);
        var withoutKey = await owner.PublishAsync(Project, draft.Json["revision"]!.GetValue<long>() + 1, ConsoleClient.NewIdempotencyKey());
        Assert.Equal(HttpStatusCode.Created, withoutKey.Status);

        var rolledBack = await owner.RollBackAsync(Project, targetVersion: 1, expectedCurrentVersion: 2, ConsoleClient.NewIdempotencyKey());

        Assert.Equal(HttpStatusCode.Created, rolledBack.Status);
        Assert.Equal("json", (await SnapshotFlagAsync(owner, Key)).GetProperty("type").GetString());
    }

    /// <summary>
    /// STRIDE T4r sob disputa: um editor publica a chave como booleana enquanto outro a apaga e recria como JSON. O lock
    /// do ambiente põe os dois em fila: ou a publicação vem antes, e a recriação leva 409 <c>flag-type-locked</c>, ou a
    /// recriação vem antes, e a publicação leva 409 <c>draft-changed</c>. Nunca as duas versões do tipo publicadas.
    /// Quem segura o invariante é a FK do tipo travado; o lock é o que faz a resposta ser 409, e não 500, sob disputa.
    /// </summary>
    [Fact]
    public async Task Racing_a_publication_against_recreating_the_key_with_another_type_never_publishes_both_types()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        var projects = Enumerable.Range(1, Racers).Select(racer => $"race-{racer}").ToList();
        foreach (var project in projects)
        {
            Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync(project)).Status);
        }

        var races = await Task.WhenAll(projects.Select(project => RaceAsync(owner, project)));

        Assert.All(races, race => Assert.True(
            race is (HttpStatusCode.Created, null, HttpStatusCode.Conflict, FlagTypeLocked)
                or (HttpStatusCode.Conflict, "/problems/draft-changed", HttpStatusCode.Created, null),
            $"Unexpected outcome {race}."));
        foreach (var (project, race) in projects.Zip(races))
        {
            if (race.Recreated == HttpStatusCode.Created)
            {
                var draft = await owner.GetDraftAsync(project);
                var published = await owner.PublishAsync(project, draft.Json["revision"]!.GetValue<long>(), ConsoleClient.NewIdempotencyKey());
                Assert.Equal(HttpStatusCode.Created, published.Status);
            }
        }

        Assert.Equal(0L, await api.QueryScalarAsync(
            $"""
            select count(*) from (
                select flag.environment_id, flag.key
                from authoring.publication_flags flag
                join authoring.environments environment on environment.id = flag.environment_id
                join authoring.projects project on project.id = environment.project_id
                where project.organization_id = '{owner.OrganizationId}'
                group by flag.environment_id, flag.key
                having count(distinct flag.type) > 1) mixed
            """));
    }

    private static async Task<RaceOutcome> RaceAsync(ConsoleClient owner, string project)
    {
        var created = await owner.CreateFlagAsync(project, Key);
        Assert.Equal(HttpStatusCode.Created, created.Status);

        var publish = owner.PublishAsync(project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey());
        var recreate = RecreateAsJsonAsync(owner, project, created.Header("ETag"));
        var (published, recreated) = (await publish, await recreate);
        return new RaceOutcome(
            published.Status,
            published.Status == HttpStatusCode.Created ? null : published.ProblemType,
            recreated.Status,
            recreated.Status == HttpStatusCode.Created ? null : recreated.ProblemType);
    }

    private static async Task<ApiResponse> RecreateAsJsonAsync(ConsoleClient owner, string project, string? etag)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFlagAsync(project, Key, etag)).Status);
        return await owner.CreateJsonFlagAsync(project, Key, """{"a":1}""", "null");
    }

    private static string DetailFor(string publishedType) =>
        $"The key '{Key}' was already published in this environment as a {publishedType} flag, and a published key keeps its type. Use a new key for a flag of another type.";

    private async Task<ConsoleClient> OwnerWithProjectAsync()
    {
        var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync(Project)).Status);
        return owner;
    }

    private static Task<ApiResponse> CreateAsync(ConsoleClient owner, string type) =>
        type == "json" ? owner.CreateJsonFlagAsync(Project, Key, """{"a":1}""", "null") : owner.CreateFlagAsync(Project, Key);

    /// <summary>Publica <see cref="Key"/> como versão 1 com <paramref name="type"/> e a apaga do rascunho.</summary>
    private static async Task PublishThenDeleteAsync(ConsoleClient owner, string type)
    {
        var created = await CreateAsync(owner, type);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync(Project, ConsoleClient.Revision(created), ConsoleClient.NewIdempotencyKey())).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFlagAsync(Project, Key, created.Header("ETag"))).Status);
    }

    private async Task<JsonElement> SnapshotFlagAsync(ConsoleClient owner, string flagKey)
    {
        var secret = (await owner.CreateCredentialAsync(Project)).Json["secret"]!.GetValue<string>();
        var snapshot = await GameClient.GetSnapshotAsync(api, secret);
        Assert.Equal(HttpStatusCode.OK, snapshot.Status);
        using var body = JsonDocument.Parse(snapshot.Body);
        return body.RootElement.GetProperty("flags").EnumerateArray()
            .Single(flag => flag.GetProperty("key").GetString() == flagKey)
            .Clone();
    }

    private sealed record RaceOutcome(HttpStatusCode Published, string? PublishProblem, HttpStatusCode Recreated, string? RecreateProblem);
}
