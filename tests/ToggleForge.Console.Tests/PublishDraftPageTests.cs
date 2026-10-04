using System.Net;
using Bunit;
using ToggleForge.Console.Pages;

namespace ToggleForge.Console.Tests;

public sealed class PublishDraftPageTests
{
    private const string Project = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race";
    private const string Draft = Project + "/environments/prod/draft";
    private const string Publications = Project + "/environments/prod/publications";
    private const string Published = """{"version":4,"publishedAt":"2026-10-03T12:00:00Z","publishedBy":{"id":"9b2f0c1e-5d6a-4b7c-8e9f-1a2b3c4d5e6f","email":"member@toggleforge.test"},"origin":"publication","restoredVersion":null,"flags":[]}""";

    [Fact]
    public void Publishing_waits_for_the_environment_key_typed_exactly()
    {
        var api = TwoChangesSinceVersionThree();
        using var console = ConsoleRendering.Create(api);
        var page = RenderPublish(console);

        page.Find("input[autocomplete=off]").Input("Prod");

        Assert.True(PublishButton(page).HasAttribute("disabled"));
        page.Find("input[autocomplete=off]").Input("prod");
        Assert.False(PublishButton(page).HasAttribute("disabled"));
        Assert.Equal("Publish 2 changes to prod", PublishButton(page).TextContent.Trim());
    }

    [Fact]
    public void The_review_lists_each_change_since_the_live_version()
    {
        var api = TwoChangesSinceVersionThree();
        using var console = ConsoleRendering.Create(api);

        var page = RenderPublish(console);

        Assert.Equal("Changes since version 3", page.Find("#changes").TextContent.Trim());
        Assert.Equal(
            ["new-shop added", "dark-mode modified"],
            page.FindAll("[aria-label=Changes] li").Select(change => string.Join(" ", change.QuerySelectorAll("span").Select(part => part.TextContent))));
    }

    [Fact]
    public void Publishing_sends_the_revision_reviewed_and_shows_the_live_version()
    {
        var api = TwoChangesSinceVersionThree().Answer(HttpMethod.Post, Publications, HttpStatusCode.Created, Published);
        using var console = ConsoleRendering.Create(api);
        var page = RenderPublish(console);

        page.Find("input[maxlength='280']").Change("Open the shop");
        Publish(page);

        page.WaitForAssertion(() => Assert.Contains("Version 4 is live in Production.", page.Find("[role=status]").TextContent, StringComparison.Ordinal));
        var request = Assert.Single(api.RequestsTo(HttpMethod.Post, Publications));
        Assert.Equal("""{"draftRevision":9,"summary":"Open the shop"}""", api.BodyOf(request));
        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
    }

    [Fact]
    public void An_unknown_result_is_checked_with_the_same_request_and_key()
    {
        var api = TwoChangesSinceVersionThree()
            .Fail(HttpMethod.Post, Publications);
        using var console = ConsoleRendering.Create(api);
        var page = RenderPublish(console);
        Publish(page);
        page.WaitForAssertion(() => Assert.Contains("We could not confirm whether the publication happened.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));

        api.Answer(HttpMethod.Post, Publications, HttpStatusCode.Created, Published);
        page.Find("input[maxlength='280']").Change("A different summary");
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Check the result").Click();

        page.WaitForAssertion(() => Assert.Contains("Version 4 is live", page.Find("[role=status]").TextContent, StringComparison.Ordinal));
        var attempts = api.RequestsTo(HttpMethod.Post, Publications).ToList();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0].Headers.GetValues("Idempotency-Key"), attempts[1].Headers.GetValues("Idempotency-Key"));
        Assert.Equal(api.BodyOf(attempts[0]), api.BodyOf(attempts[1]));
    }

    [Fact]
    public void A_draft_changed_during_the_review_is_shown_again_for_a_new_review()
    {
        var api = TwoChangesSinceVersionThree()
            .ThenAnswer(HttpMethod.Get, Draft, HttpStatusCode.OK, """
                {"revision":10,"basedOnVersion":3,"flags":[],
                 "changes":[{"flagKey":"new-shop","change":"added"},{"flagKey":"dark-mode","change":"modified"},{"flagKey":"old-menu","change":"removed"}]}
                """)
            .Answer(HttpMethod.Post, Publications, HttpStatusCode.Conflict,
                """{"type":"/problems/draft-changed","title":"Draft changed","status":409}""", "application/problem+json");
        using var console = ConsoleRendering.Create(api);
        var page = RenderPublish(console);

        Publish(page);

        page.WaitForAssertion(() => Assert.Contains("The draft changed while you reviewed it.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Equal(3, page.FindAll("[aria-label=Changes] li").Count);
        Assert.True(PublishButton(page).HasAttribute("disabled"));
    }

    [Fact]
    public void A_viewer_is_told_that_only_owners_and_editors_publish()
    {
        var api = TwoChangesSinceVersionThree();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderPublish(console);

        Assert.Contains("Only owners and editors publish.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void The_breadcrumb_ends_at_the_publish_page()
    {
        using var console = ConsoleRendering.Create(TwoChangesSinceVersionThree());

        var page = RenderPublish(console);

        Assert.Equal("Publish", AccessibleNames.CurrentBreadcrumb(page));
    }

    private static StubApi TwoChangesSinceVersionThree()
    {
        var api = new StubApi()
            .Answer(HttpMethod.Get, Project, HttpStatusCode.OK, """
                {"key":"space-race","name":"Space Race","environments":[
                  {"key":"prod","name":"Production","currentVersion":3,"draftRevision":9,"hasUnpublishedChanges":true}]}
                """)
            .Answer(HttpMethod.Get, Draft, HttpStatusCode.OK, """
                {"revision":9,"basedOnVersion":3,"flags":[],
                 "changes":[{"flagKey":"new-shop","change":"added"},{"flagKey":"dark-mode","change":"modified"}]}
                """);
        ConsoleRendering.SignInAs(api, "editor");
        return api;
    }

    private static void Publish(IRenderedComponent<PublishDraft> page)
    {
        page.Find("input[autocomplete=off]").Input("prod");
        page.Find("form").Submit();
    }

    private static AngleSharp.Dom.IElement PublishButton(IRenderedComponent<PublishDraft> page) => page.Find("button[type=submit]");

    private static IRenderedComponent<PublishDraft> RenderPublish(BunitContext console) =>
        console.Render<PublishDraft>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "prod"));
}
