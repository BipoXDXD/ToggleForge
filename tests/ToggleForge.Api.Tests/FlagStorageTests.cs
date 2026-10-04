using System.Net;
using Npgsql;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>As constraints do tipo e dos valores de uma flag (ADR 0011), contra dado gravado por fora da aplicação.</summary>
public sealed class FlagStorageTests(ApiFactory api)
{
    [Fact]
    public async Task The_database_refuses_a_boolean_flag_that_stores_a_json_value()
    {
        using var owner = await OwnerWithNewShopAsync();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            api.ExecuteSqlAsync(UpdateNewShop(owner, "on_value = '3'")));

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

    private async Task<ConsoleClient> OwnerWithProjectAsync()
    {
        var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        return owner;
    }

    // A flag entra por SQL porque a resposta de createFlag só segue o contrato novo na Tarefa 9; o insert também
    // prova que as constraints aceitam uma flag booleana válida.
    private async Task<ConsoleClient> OwnerWithNewShopAsync()
    {
        var owner = await OwnerWithProjectAsync();
        await api.ExecuteSqlAsync(InsertDraftFlag(owner, "new-shop", "boolean", "true", "false"));
        return owner;
    }

    private static string InsertDraftFlag(ConsoleClient owner, string key, string type, string onValue, string offValue) =>
        $"""
        insert into authoring.draft_flags (environment_id, key, is_enabled, type, on_value, off_value, rollout, seed, changed_at_revision)
        select environment.id, {Literal(key)}, true, {Literal(type)}, {Literal(onValue)}, {Literal(offValue)}, 1000, 'seed-1', 2
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

    private static string Literal(string text) => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";
}
