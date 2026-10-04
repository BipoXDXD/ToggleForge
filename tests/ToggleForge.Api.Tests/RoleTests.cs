using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE E1 (BFLA): papel insuficiente na própria organização recebe 403 e não muda nada.</summary>
public sealed class RoleTests(ApiFactory api)
{
    public static TheoryData<string, string> Forbidden => new()
    {
        { "viewer", "createProject" },
        { "viewer", "createEnvironment" },
        { "viewer", "createFlag" },
        { "viewer", "updateFlag" },
        { "viewer", "deleteFlag" },
        { "viewer", "publishDraft" },
        { "viewer", "rollBack" },
        { "viewer", "createCredential" },
        { "viewer", "listCredentials" },
        { "viewer", "revokeCredential" },
        { "editor", "createProject" },
        { "editor", "createEnvironment" },
        { "editor", "listCredentials" },
        { "editor", "createCredential" },
        { "editor", "revokeCredential" },
    };

    [Theory]
    [MemberData(nameof(Forbidden))]
    public async Task A_lower_role_gets_403(string role, string operation)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await owner.CreateFlagAsync("space-race", "new-shop"));
        var credentialId = (await owner.CreateCredentialAsync("space-race")).Json["id"]!.GetValue<string>();
        using var member = await MemberWithRoleAsync(owner, role);

        var response = operation switch
        {
            "createProject" => await member.CreateProjectAsync("another", owner.OrganizationId),
            "createEnvironment" => await member.CreateEnvironmentAsync("space-race", "staging", owner.OrganizationId),
            "createFlag" => await member.CreateFlagAsync("space-race", "battle-pass", organizationId: owner.OrganizationId),
            "updateFlag" => await member.UpdateFlagAsync("space-race", "new-shop", $"\"{revision}\"", """{"enabled":false}""", owner.OrganizationId),
            "deleteFlag" => await member.DeleteFlagAsync("space-race", "new-shop", $"\"{revision}\"", owner.OrganizationId),
            "publishDraft" => await member.PublishAsync("space-race", revision, ConsoleClient.NewIdempotencyKey(), organizationId: owner.OrganizationId),
            "rollBack" => await member.RollBackAsync("space-race", 1, 1, ConsoleClient.NewIdempotencyKey(), organizationId: owner.OrganizationId),
            "listCredentials" => await member.ListCredentialsAsync("space-race", organizationId: owner.OrganizationId),
            "createCredential" => await member.CreateCredentialAsync("space-race", organizationId: owner.OrganizationId),
            "revokeCredential" => await member.RevokeCredentialAsync("space-race", credentialId, organizationId: owner.OrganizationId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("/problems/forbidden", response.ProblemType);
        var draft = await owner.GetDraftAsync("space-race");
        Assert.Equal(revision, draft.Json["revision"]!.GetValue<long>());
        Assert.Null(draft.Json["basedOnVersion"]?.GetValue<long>());
        Assert.Equal(2, (await owner.GetProjectAsync("space-race")).Json["environments"]!.AsArray().Count);
        var credential = Assert.Single((await owner.ListCredentialsAsync("space-race")).Json["data"]!.AsArray())!;
        Assert.Null(credential["revokedAt"]?.GetValue<DateTimeOffset>());
    }

    /// <summary>O 400 de valor que não serve ao tipo só sai depois do papel conferido.</summary>
    [Fact]
    public async Task A_viewer_gets_403_for_a_json_value_that_does_not_fit_the_flag()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var created = await owner.CreateFlagAsync("space-race", "new-shop");
        using var viewer = await MemberWithRoleAsync(owner, "viewer");

        var response = await viewer.UpdateFlagAsync("space-race", "new-shop", created.Header("ETag"), """{"onValue":{}}""", owner.OrganizationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("/problems/forbidden", response.ProblemType);
        var flag = await owner.GetFlagAsync("space-race", "new-shop");
        Assert.Equal(created.Header("ETag"), flag.Header("ETag"));
        Assert.Equal(created.Body, flag.Body);
    }

    [Fact]
    public async Task A_viewer_gets_403_creating_a_json_flag()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        using var viewer = await MemberWithRoleAsync(owner, "viewer");

        var response = await viewer.CreateJsonFlagAsync("space-race", "shop-config", """{"discount":0.2}""", "null", organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("/problems/forbidden", response.ProblemType);
        Assert.Empty((await owner.GetDraftAsync("space-race")).Json["flags"]!.AsArray());
    }

    [Fact]
    public async Task An_editor_creates_changes_and_removes_flags_and_publishes()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        using var editor = await MemberWithRoleAsync(owner, "editor");

        var flag = await editor.CreateFlagAsync("space-race", "new-shop", organizationId: owner.OrganizationId);
        var other = await editor.CreateFlagAsync("space-race", "battle-pass", organizationId: owner.OrganizationId);
        var changed = await editor.UpdateFlagAsync("space-race", "new-shop", flag.Header("ETag"), """{"enabled":false}""", owner.OrganizationId);
        var removed = await editor.DeleteFlagAsync("space-race", "battle-pass", other.Header("ETag"), owner.OrganizationId);
        var publication = await editor.PublishAsync(
            "space-race", ConsoleClient.Revision(changed) + 1, ConsoleClient.NewIdempotencyKey(), organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.Created, flag.Status);
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        Assert.Equal(HttpStatusCode.Created, publication.Status);
    }

    [Fact]
    public async Task A_viewer_reads_projects_environments_and_drafts()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        using var viewer = await MemberWithRoleAsync(owner, "viewer");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetProjectAsync("space-race", owner.OrganizationId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetDraftAsync("space-race", organizationId: owner.OrganizationId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await viewer.ListProjectsAsync(owner.OrganizationId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetEnvironmentAsync("space-race", "dev", owner.OrganizationId)).Status);
    }

    private async Task<ConsoleClient> MemberWithRoleAsync(ConsoleClient owner, string role)
    {
        var member = await ConsoleClient.SignUpAsync(api);
        await api.AddMembershipAsync(owner.OrganizationId, member.MemberId, role);
        return member;
    }
}
