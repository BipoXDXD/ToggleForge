using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE T1/I2 (BOLA): o membro B em cada rota da organização de A recebe 404 e A fica intacto.</summary>
public sealed class IsolationTests(ApiFactory api)
{
    public static TheoryData<string> Operations =>
        ["getProject", "createProject", "getDraft", "createFlag", "getFlag", "updateFlag", "deleteFlag", "listPublications", "getPublication", "publishDraft", "rollBack", "createCredential"];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_member_of_another_organization_gets_404_and_changes_nothing(string operation)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        using var intruder = await ConsoleClient.SignUpAsync(api);
        var revision = await owner.PublishedProjectAsync("space-race");
        var before = await StateAsync(owner);

        var response = await SendAsync(intruder, operation, owner.OrganizationId, revision);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("/problems/not-found", response.ProblemType);
        Assert.DoesNotContain("space-race", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("new-shop", response.Body, StringComparison.Ordinal);
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

    private static Task<ApiResponse> SendAsync(ConsoleClient client, string operation, Guid organizationId, long revision) =>
        operation switch
        {
            "getProject" => client.GetProjectAsync("space-race", organizationId),
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
            "createCredential" => client.CreateCredentialAsync("space-race", organizationId: organizationId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };

    private static async Task<string> StateAsync(ConsoleClient owner) =>
        (await owner.SendAsync("getSession", HttpMethod.Get, Paths.Session)).Body
        + (await owner.GetProjectAsync("space-race")).Body
        + (await owner.GetDraftAsync("space-race")).Body
        + (await owner.GetProjectAsync("hijack")).Status;
}
