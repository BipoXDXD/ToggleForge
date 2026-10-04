using System.Net;
using Npgsql;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>As constraints do tipo e dos valores de uma flag (ADR 0011), contra dado gravado por fora da aplicação.</summary>
public sealed class FlagStorageTests(ApiFactory api)
{
    private const string PublishedTypeForeignKey = "fk_publication_flags_published_flag_types";

    [Theory]
    [InlineData("on_value")]
    [InlineData("off_value")]
    public async Task The_database_refuses_a_boolean_flag_that_stores_a_json_value(string column)
    {
        using var owner = await OwnerWithNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            api.ExecuteSqlAsync(UpdateNewShop(owner, $"{column} = '3'")));

        Assert.Equal("ck_draft_flags_boolean_values", error.ConstraintName);
    }

    [Fact]
    public async Task The_database_refuses_an_unknown_flag_type()
    {
        using var owner = await OwnerWithNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            api.ExecuteSqlAsync(UpdateNewShop(owner, "type = 'color'")));

        Assert.Equal("ck_draft_flags_type", error.ConstraintName);
    }

    [Fact]
    public async Task The_database_takes_any_json_value_in_a_json_flag()
    {
        using var owner = await OwnerWithProjectAsync();

        await api.ExecuteSqlAsync(InsertDraftFlag(owner, "config", "json", """{"a":1}""", "null"));

        Assert.Equal("""{"a":1}""", await api.QueryScalarAsync(SelectDraftValue(owner, "config", "on_value")));
        Assert.Equal("null", await api.QueryScalarAsync(SelectDraftValue(owner, "config", "off_value")));
    }

    [Fact]
    public async Task The_database_refuses_a_published_boolean_flag_that_stores_a_json_value()
    {
        using var owner = await OwnerWithPublishedNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            api.ExecuteSqlAsync(UpdatePublishedNewShop(owner, "off_value = '\"no\"'")));

        Assert.Equal("ck_publication_flags_boolean_values", error.ConstraintName);
    }

    [Fact]
    public async Task The_database_refuses_an_unknown_published_flag_type()
    {
        using var owner = await OwnerWithPublishedNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            api.ExecuteSqlAsync(UpdatePublishedNewShop(owner, "type = 'color'")));

        Assert.Equal("ck_publication_flags_type", error.ConstraintName);
    }

    /// <summary>ADR 0011: a chave publicada mantém o tipo, mesmo para quem grava sem passar pela aplicação.</summary>
    [Fact]
    public async Task The_database_refuses_a_published_flag_with_another_type_than_its_key_was_published_with()
    {
        using var owner = await OwnerWithPublishedNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            api.ExecuteSqlAsync(UpdatePublishedNewShop(owner, "type = 'json'")));

        Assert.Equal(PublishedTypeForeignKey, error.ConstraintName);
    }

    [Fact]
    public async Task The_database_refuses_changing_the_type_a_key_was_published_with()
    {
        using var owner = await OwnerWithPublishedNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => api.ExecuteSqlAsync(
            $"""
            update authoring.published_flag_types published set type = 'json'
            from authoring.environments environment join authoring.projects project on project.id = environment.project_id
            where published.environment_id = environment.id and project.organization_id = '{owner.OrganizationId}' and published.key = 'new-shop'
            """));

        Assert.Equal(PublishedTypeForeignKey, error.ConstraintName);
    }

    [Fact]
    public async Task The_database_keeps_one_published_type_per_key_and_environment()
    {
        using var owner = await OwnerWithPublishedNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => api.ExecuteSqlAsync(
            $"""
            insert into authoring.published_flag_types (environment_id, key, type)
            select environment.id, 'new-shop', 'json'
            {DevEnvironmentOf(owner)}
            """));

        Assert.Equal("pk_published_flag_types", error.ConstraintName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_database_refuses_a_json_value_over_4_KiB(bool onValueIsLarge)
    {
        using var owner = await OwnerWithProjectAsync();
        var large = ConsoleClient.JsonTextOf(4_097);

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => api.ExecuteSqlAsync(
            InsertDraftFlag(owner, "config", "json", onValueIsLarge ? large : "null", onValueIsLarge ? "null" : large)));

        Assert.Equal("ck_draft_flags_value_bytes", error.ConstraintName);
    }

    [Fact]
    public async Task The_database_takes_a_json_value_of_exactly_4_KiB()
    {
        using var owner = await OwnerWithProjectAsync();

        var value = ConsoleClient.JsonTextOf(4_096);

        await api.ExecuteSqlAsync(InsertDraftFlag(owner, "config", "json", "null", value));

        Assert.Equal(value, await api.QueryScalarAsync(SelectDraftValue(owner, "config", "off_value")));
    }

    [Fact]
    public async Task A_corrupted_json_value_never_reaches_the_logs_or_the_response()
    {
        using var owner = await OwnerWithProjectAsync();
        var canary = $"CANARY-{Guid.NewGuid()}";
        // Passa nos CHECKs do banco, mas a chave repetida é recusada pelo domínio ao ler.
        await api.ExecuteSqlAsync(InsertDraftFlag(owner, "config", "json", $$"""{"k":"{{canary}}","k":1}""", "null"));

        var draft = await owner.GetDraftAsync("space-race");

        Assert.Equal(HttpStatusCode.InternalServerError, draft.Status);
        Assert.Equal("/problems/unexpected", draft.Json["type"]!.GetValue<string>());
        Assert.DoesNotContain(canary, draft.Body, StringComparison.Ordinal);
        var reference = draft.Json["instance"]!.GetValue<string>()["/unexpected-errors/".Length..];
        Assert.Contains(api.Logs.Entries, entry => entry.Contains(reference, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(canary, StringComparison.Ordinal));
    }

    /// <summary>STRIDE I1 no caminho da publicação: o snapshot do jogo e o histórico também leem o valor gravado.</summary>
    [Fact]
    public async Task A_corrupted_published_json_value_never_reaches_the_logs_or_the_response()
    {
        using var owner = await OwnerWithProjectAsync();
        var secret = (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();
        var canary = $"CANARY-{Guid.NewGuid()}";
        await api.ExecuteSqlAsync(
            $"""
            insert into authoring.publications (environment_id, version, published_at, published_by, origin)
            select environment.id, 1, now(), '{owner.MemberId}', 'publication'
            {DevEnvironmentOf(owner)};
            insert into authoring.published_flag_types (environment_id, key, type)
            select environment.id, 'config', 'json'
            {DevEnvironmentOf(owner)};
            insert into authoring.publication_flags (environment_id, version, key, is_enabled, type, on_value, off_value, rollout_basis_points, seed)
            select environment.id, 1, 'config', true, 'json', {Literal($$"""{"k":"{{canary}}","k":1}""")}, 'null', 1000, 'seed-1'
            {DevEnvironmentOf(owner)};
            update authoring.environments set current_version = 1 where id = (select environment.id {DevEnvironmentOf(owner)});
            """);

        var snapshot = await GameClient.GetSnapshotAsync(api, secret);
        var publication = await owner.GetPublicationAsync("space-race", "1");

        Assert.All([snapshot, publication], response =>
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
            Assert.Equal("/problems/unexpected", response.ProblemType);
            Assert.DoesNotContain(canary, response.Body, StringComparison.Ordinal);
            // Sem o registro da referência, o DoesNotContain abaixo passaria mesmo sem log nenhum.
            var reference = response.Json["instance"]!.GetValue<string>()["/unexpected-errors/".Length..];
            Assert.Contains(api.Logs.Entries, entry => entry.Contains(reference, StringComparison.Ordinal));
        });
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(canary, StringComparison.Ordinal));
    }

    private async Task<ConsoleClient> OwnerWithProjectAsync()
    {
        var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        return owner;
    }

    private async Task<ConsoleClient> OwnerWithNewShopAsync()
    {
        var owner = await OwnerWithProjectAsync();
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "new-shop")).Status);
        return owner;
    }

    private async Task<ConsoleClient> OwnerWithPublishedNewShopAsync()
    {
        var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        return owner;
    }

    private static string InsertDraftFlag(ConsoleClient owner, string key, string type, string onValue, string offValue) =>
        $"""
        insert into authoring.draft_flags (environment_id, key, is_enabled, type, on_value, off_value, rollout, seed, changed_at_revision)
        select environment.id, {Literal(key)}, true, {Literal(type)}, {Literal(onValue)}, {Literal(offValue)}, 1000, 'seed-1', 2
        {DevEnvironmentOf(owner)}
        """;

    private static string SelectDraftValue(ConsoleClient owner, string key, string column) =>
        $"""
        select flag.{column}::text
        from authoring.draft_flags flag join authoring.environments environment on environment.id = flag.environment_id
        join authoring.projects project on project.id = environment.project_id
        where project.organization_id = '{owner.OrganizationId}' and environment.key = 'dev' and flag.key = {Literal(key)}
        """;

    private static string DevEnvironmentOf(ConsoleClient owner) =>
        $"""
        from authoring.environments environment join authoring.projects project on project.id = environment.project_id
        where project.organization_id = '{owner.OrganizationId}' and environment.key = 'dev'
        """;

    // Outras organizações também têm new-shop, e os testes rodam em paralelo: só a flag deste teste muda.
    private static string UpdateNewShop(ConsoleClient owner, string assignment) =>
        $"""
        update authoring.draft_flags flag set {assignment}
        from authoring.environments environment join authoring.projects project on project.id = environment.project_id
        where flag.environment_id = environment.id and project.organization_id = '{owner.OrganizationId}' and flag.key = 'new-shop'
        """;

    private static string UpdatePublishedNewShop(ConsoleClient owner, string assignment) =>
        $"""
        update authoring.publication_flags flag set {assignment}
        from authoring.environments environment join authoring.projects project on project.id = environment.project_id
        where flag.environment_id = environment.id and project.organization_id = '{owner.OrganizationId}' and flag.key = 'new-shop'
        """;

    private static string Literal(string text) => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";
}
