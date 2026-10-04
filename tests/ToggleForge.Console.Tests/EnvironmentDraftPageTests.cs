using System.Net;
using Bunit;
using ToggleForge.Console.Pages;

namespace ToggleForge.Console.Tests;

public sealed class EnvironmentDraftPageTests
{
    private const string Project = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race";
    private const string Draft = Project + "/environments/dev/draft";

    [Fact]
    public void A_rollout_with_three_decimals_is_refused_before_reaching_the_api()
    {
        var api = DraftWithOneFlag();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderDraft(console);
        page.WaitForElement("form");

        page.Find("input[maxlength='64']").Change("new-shop");
        page.Find("input[type=number]").Change("12.345");
        page.Find("form").Submit();

        Assert.Equal("Enter a rollout between 0 and 100, with at most two decimals.", page.Find("[role=alert]").TextContent.Trim());
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Draft + "/flags"));
    }

    [Fact]
    public void A_created_flag_joins_the_draft_table_with_its_rollout()
    {
        var api = DraftWithOneFlag()
            .Answer(HttpMethod.Post, Draft + "/flags", HttpStatusCode.Created,
                """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":1250}""");
        ConsoleRendering.SignInAs(api, "owner");
        using var console = ConsoleRendering.Create(api);
        var page = RenderDraft(console);
        page.WaitForElement("form");

        page.Find("input[maxlength='64']").Change("new-shop");
        page.Find("input[type=number]").Change("12.5");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        Assert.Equal(["new-shop", "Enabled", "12.5%"], page.FindAll("tbody tr")[1].QuerySelectorAll("td").Select(cell => cell.TextContent));
    }

    [Fact]
    public void A_viewer_reads_the_draft_without_the_form_to_change_it()
    {
        var api = DraftWithOneFlag();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderDraft(console);

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.Empty(page.FindAll("form"));
    }

    private static StubApi DraftWithOneFlag() => new StubApi()
        .Answer(HttpMethod.Get, Project, HttpStatusCode.OK, """
            {"key":"space-race","name":"Space Race","environments":[
              {"key":"dev","name":"Development","currentVersion":null,"draftRevision":2,"hasUnpublishedChanges":true}]}
            """)
        .Answer(HttpMethod.Get, Draft, HttpStatusCode.OK, """
            {"revision":2,"basedOnVersion":null,"changes":[],
             "flags":[{"key":"dark-mode","enabled":false,"onValue":true,"offValue":false,"rolloutBasisPoints":0}]}
            """);

    private static IRenderedComponent<EnvironmentDraft> RenderDraft(BunitContext console) =>
        console.Render<EnvironmentDraft>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "dev"));
}
