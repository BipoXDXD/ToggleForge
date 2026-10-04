using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE T1/I2 (BOLA): o membro B em cada rota da organização de A recebe 404 e A fica intacto.</summary>
public sealed class IsolationTests(ApiFactory api)
{
    public static TheoryData<string> Operations =>
        ["listProjects", "getProject", "createProject", "createEnvironment", "getEnvironment", "getDraft", "createFlag", "getFlag", "updateFlag", "deleteFlag", "listPublications", "getPublication", "publishDraft", "rollBack", "simulatePlayer", "listCredentials", "createCredential", "revokeCredential"];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_member_of_another_organization_gets_404_and_changes_nothing(string operation)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        using var intruder = await ConsoleClient.SignUpAsync(api);
        var canary = $"CANARY-{Guid.NewGuid()}";
        var revision = await PublishedProjectWithSecretAsync(owner, canary);
        var credentialId = (await owner.CreateCredentialAsync("space-race")).Json["id"]!.GetValue<string>();
        var before = await StateAsync(owner);

        var response = await SendAsync(intruder, operation, owner.OrganizationId, revision, credentialId);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/not-found", response.ProblemType);
        Assert.DoesNotContain("space-race", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("new-shop", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, response.Body, StringComparison.Ordinal);
        Assert.Equal(before, await StateAsync(owner));
    }

    /// <summary>O 400 de valor que não serve ao tipo revelaria que a flag existe e é booleana.</summary>
    [Fact]
    public async Task A_member_of_another_organization_gets_404_for_a_json_value_that_does_not_fit_the_flag()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        using var intruder = await ConsoleClient.SignUpAsync(api);
        var revision = await owner.PublishedProjectAsync("space-race");
        var before = await StateAsync(owner);

        var response = await intruder.UpdateFlagAsync("space-race", "new-shop", $"\"{revision}\"", """{"onValue":{}}""", owner.OrganizationId);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/not-found", response.ProblemType);
        Assert.Equal(before, await StateAsync(owner));
    }

    /// <summary>O 409 da trava de tipo revelaria que a chave já foi publicada, e com qual tipo.</summary>
    [Fact]
    public async Task A_member_of_another_organization_gets_404_not_the_type_a_key_was_published_with()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        using var intruder = await ConsoleClient.SignUpAsync(api);
        var revision = await owner.PublishedProjectAsync("space-race");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFlagAsync("space-race", "new-shop", $"\"{revision}\"")).Status);
        var before = await StateAsync(owner);

        var response = await intruder.CreateJsonFlagAsync("space-race", "new-shop", """{"a":1}""", "null", organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/not-found", response.ProblemType);
        Assert.DoesNotContain("new-shop", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("boolean", response.Body, StringComparison.Ordinal);
        Assert.Equal(before, await StateAsync(owner));
    }

    /// <summary>O 409 do teto revelaria quanto o rascunho de outra organização ocupa.</summary>
    [Fact]
    public async Task A_member_of_another_organization_gets_404_even_when_the_owner_draft_is_full()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        using var intruder = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        await owner.FillJsonBudgetAsync("space-race", 128 * 1024);
        var before = await StateAsync(owner);
        var fullValue = ConsoleClient.JsonTextOf(4_096);

        var response = await intruder.CreateJsonFlagAsync("space-race", "hijack", fullValue, fullValue, organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/not-found", response.ProblemType);
        Assert.Equal(before, await StateAsync(owner));
    }

    [Fact]
    public async Task A_missing_organization_answers_like_a_foreign_one()
    {
        using var intruder = await ConsoleClient.SignUpAsync(api);
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var foreign = await intruder.GetProjectAsync("space-race", owner.OrganizationId);
        var missing = await intruder.GetProjectAsync("space-race", Guid.NewGuid());

        Assert.Equal(foreign.Status, missing.Status);
        Assert.Equal(foreign.Body, missing.Body);
    }

    private static Task<ApiResponse> SendAsync(
        ConsoleClient client, string operation, Guid organizationId, long revision, string credentialId) =>
        operation switch
        {
            "listProjects" => client.ListProjectsAsync(organizationId),
            "getProject" => client.GetProjectAsync("space-race", organizationId),
            "createEnvironment" => client.CreateEnvironmentAsync("space-race", "hijack", organizationId),
            "getEnvironment" => client.GetEnvironmentAsync("space-race", "dev", organizationId),
            "createProject" => client.CreateProjectAsync("hijack", organizationId),
            "getDraft" => client.GetDraftAsync("space-race", organizationId: organizationId),
            "createFlag" => client.CreateFlagAsync("space-race", "hijack", organizationId: organizationId),
            "getFlag" => client.GetFlagAsync("space-race", "new-shop", organizationId),
            "updateFlag" => client.UpdateFlagAsync("space-race", "new-shop", $"\"{revision}\"", """{"enabled":false}""", organizationId),
            "deleteFlag" => client.DeleteFlagAsync("space-race", "new-shop", $"\"{revision}\"", organizationId),
            "listPublications" => client.ListPublicationsAsync("space-race", organizationId: organizationId),
            "getPublication" => client.GetPublicationAsync("space-race", "1", organizationId),
            "publishDraft" => client.PublishAsync("space-race", revision, ConsoleClient.NewIdempotencyKey(), organizationId: organizationId),
            "rollBack" => client.RollBackAsync("space-race", 1, 1, ConsoleClient.NewIdempotencyKey(), organizationId: organizationId),
            "simulatePlayer" => client.SimulatePublicationAsync("space-race", "player-42", 1, organizationId: organizationId),
            "listCredentials" => client.ListCredentialsAsync("space-race", organizationId: organizationId),
            "createCredential" => client.CreateCredentialAsync("space-race", organizationId: organizationId),
            "revokeCredential" => client.RevokeCredentialAsync("space-race", credentialId, organizationId: organizationId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };

    /// <summary>Como <see cref="ConsoleClient.PublishedProjectAsync"/>, mais uma flag json publicada com um canário.</summary>
    private static async Task<long> PublishedProjectWithSecretAsync(ConsoleClient owner, string canary)
    {
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "new-shop")).Status);
        var secret = await owner.CreateJsonFlagAsync("space-race", "shop-config", $$"""{"secret":"{{canary}}"}""", "null");
        Assert.Equal(HttpStatusCode.Created, secret.Status);
        var revision = ConsoleClient.Revision(secret);
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync("space-race", revision, ConsoleClient.NewIdempotencyKey())).Status);
        return revision;
    }

    private static async Task<string> StateAsync(ConsoleClient owner) =>
        (await owner.SendAsync("getSession", HttpMethod.Get, Paths.Session)).Body
        + (await owner.GetProjectAsync("space-race")).Body
        + (await owner.GetDraftAsync("space-race")).Body
        + (await owner.ListCredentialsAsync("space-race")).Body
        + (await owner.GetProjectAsync("hijack")).Status;
}
