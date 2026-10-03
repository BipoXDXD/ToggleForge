using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE E1 (BFLA): papel insuficiente na própria organização recebe 403 e não muda nada.</summary>
public sealed class RoleTests(ApiFactory api)
{
    public static TheoryData<string, string> Forbidden => new()
    {
        { "viewer", "createProject" },
        { "viewer", "createFlag" },
        { "viewer", "updateFlag" },
        { "viewer", "deleteFlag" },
        { "viewer", "publishDraft" },
        { "viewer", "createCredential" },
        { "editor", "createProject" },
        { "editor", "createCredential" },
    };

    [Theory]
    [MemberData(nameof(Forbidden))]
    public async Task A_lower_role_gets_403(string role, string operation)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        var revision = ConsoleClient.Revision(await owner.CreateFlagAsync("space-race", "new-shop"));
        using var member = await MemberWithRoleAsync(owner, role);

        var response = operation switch
        {
            "createProject" => await member.CreateProjectAsync("another", owner.OrganizationId),
            "createFlag" => await member.CreateFlagAsync("space-race", "battle-pass", organizationId: owner.OrganizationId),
            "updateFlag" => await member.UpdateFlagAsync("space-race", "new-shop", $"\"{revision}\"", """{"enabled":false}""", owner.OrganizationId),
            "deleteFlag" => await member.DeleteFlagAsync("space-race", "new-shop", $"\"{revision}\"", owner.OrganizationId),
            "publishDraft" => await member.PublishAsync("space-race", revision, ConsoleClient.NewIdempotencyKey(), organizationId: owner.OrganizationId),
            "createCredential" => await member.CreateCredentialAsync("space-race", organizationId: owner.OrganizationId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("/problems/forbidden", response.ProblemType);
        var draft = await owner.GetDraftAsync("space-race");
        Assert.Equal(revision, draft.Json["revision"]!.GetValue<long>());
        Assert.Null(draft.Json["basedOnVersion"]?.GetValue<long>());
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
    public async Task A_viewer_reads_the_project_and_the_draft()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        using var viewer = await MemberWithRoleAsync(owner, "viewer");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetProjectAsync("space-race", owner.OrganizationId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetDraftAsync("space-race", organizationId: owner.OrganizationId)).Status);
    }

    private async Task<ConsoleClient> MemberWithRoleAsync(ConsoleClient owner, string role)
    {
        var member = await ConsoleClient.SignUpAsync(api);
        await api.AddMembershipAsync(owner.OrganizationId, member.MemberId, role);
        return member;
    }
}
