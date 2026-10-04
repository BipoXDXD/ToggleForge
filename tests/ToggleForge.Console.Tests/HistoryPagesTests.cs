using System.Net;
using Bunit;
using ToggleForge.Console.Pages;

namespace ToggleForge.Console.Tests;

public sealed class HistoryPagesTests
{
    private const string Environment = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/prod";
    private const string Publications = Environment + "/publications";
    private const string Rollbacks = Environment + "/rollbacks";
    private const string Member = """{"id":"9b2f0c1e-5d6a-4b7c-8e9f-1a2b3c4d5e6f","email":"member@toggleforge.test"}""";

    [Fact]
    public void The_history_marks_the_live_version_and_tells_rollbacks_apart()
    {
        var api = LiveAtVersion(3, pendingChanges: false).Answer(HttpMethod.Get, Publications, HttpStatusCode.OK, $$$"""
            {"data":[
              {"version":3,"publishedAt":"2026-10-03T12:00:00Z","publishedBy":{{{Member}}},"origin":"rollback","restoredVersion":1},
              {"version":2,"publishedAt":"2026-10-02T12:00:00Z","publishedBy":{{{Member}}},"origin":"publication","restoredVersion":null,"summary":"Shop at 50%"}],
             "metadata":{}}
            """);
        using var console = ConsoleRendering.Create(api);

        var page = RenderHistory(console);

        var entries = page.FindAll("[aria-label=Publications] li");
        Assert.Equal(2, entries.Count);
        Assert.Contains("Live", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Rollback to version 1", entries[0].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Live", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Shop at 50%", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void Older_publications_come_with_the_token_of_the_page_before()
    {
        var api = LiveAtVersion(3, pendingChanges: false)
            .Answer(HttpMethod.Get, Publications, HttpStatusCode.OK, $$$"""
                {"data":[{"version":3,"publishedAt":"2026-10-03T12:00:00Z","publishedBy":{{{Member}}},"origin":"publication","restoredVersion":null}],
                 "metadata":{"nextPageToken":"page-two"}}
                """)
            .ThenAnswer(HttpMethod.Get, Publications, HttpStatusCode.OK, $$$"""
                {"data":[{"version":2,"publishedAt":"2026-10-02T12:00:00Z","publishedBy":{{{Member}}},"origin":"publication","restoredVersion":null}],
                 "metadata":{}}
                """);
        using var console = ConsoleRendering.Create(api);
        var page = RenderHistory(console);

        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[aria-label=Publications] li").Count));
        Assert.Equal("?pageToken=page-two", api.RequestsTo(HttpMethod.Get, Publications).Last().RequestUri?.Query);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void The_live_version_offers_no_rollback()
    {
        var api = LiveAtVersion(3, pendingChanges: false).Answer(HttpMethod.Get, Publications + "/3", HttpStatusCode.OK, VersionJson(3));
        using var console = ConsoleRendering.Create(api);

        var page = RenderVersion(console, 3);

        Assert.Contains("This version is live.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void A_rollback_sends_the_live_version_seen_after_the_key_is_typed()
    {
        var api = VersionOneWhileThreeIsLive(pendingChanges: false)
            .Answer(HttpMethod.Post, Rollbacks, HttpStatusCode.Created, VersionJson(4));
        using var console = ConsoleRendering.Create(api);
        var page = RenderVersionOne(console);

        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        page.Find("input[autocomplete=off]").Input("prod");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Version 4 is live in Production with the content of version 1.", page.Find("[role=status]").TextContent, StringComparison.Ordinal));
        Assert.Equal("""{"targetVersion":1,"expectedCurrentVersion":3,"discardDraftChanges":false}""", api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Post, Rollbacks))));
    }

    [Fact]
    public void Unpublished_changes_must_be_discarded_on_purpose()
    {
        var api = VersionOneWhileThreeIsLive(pendingChanges: true)
            .Answer(HttpMethod.Post, Rollbacks, HttpStatusCode.Created, VersionJson(4));
        using var console = ConsoleRendering.Create(api);
        var page = RenderVersionOne(console);

        page.Find("input[autocomplete=off]").Input("prod");
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        page.Find("input[type=checkbox]").Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Rollbacks)));
        Assert.Contains("\"discardDraftChanges\":true", api.BodyOf(api.RequestsTo(HttpMethod.Post, Rollbacks).Single()), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_rollback_result_is_checked_with_the_same_request_and_key()
    {
        var api = VersionOneWhileThreeIsLive(pendingChanges: false).Fail(HttpMethod.Post, Rollbacks);
        using var console = ConsoleRendering.Create(api);
        var page = RenderVersionOne(console);
        page.Find("input[autocomplete=off]").Input("prod");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("We could not confirm whether the rollback happened.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));

        api.Answer(HttpMethod.Post, Rollbacks, HttpStatusCode.Created, VersionJson(4));
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Check the result").Click();

        page.WaitForAssertion(() => Assert.Contains("Version 4 is live", page.Find("[role=status]").TextContent, StringComparison.Ordinal));
        var attempts = api.RequestsTo(HttpMethod.Post, Rollbacks).ToList();
        Assert.Equal(attempts[0].Headers.GetValues("Idempotency-Key"), attempts[1].Headers.GetValues("Idempotency-Key"));
        Assert.Equal(api.BodyOf(attempts[0]), api.BodyOf(attempts[1]));
    }

    [Fact]
    public void Another_publication_during_the_review_asks_for_the_history_again()
    {
        var api = VersionOneWhileThreeIsLive(pendingChanges: false)
            .ThenAnswer(HttpMethod.Get, Environment, HttpStatusCode.OK, EnvironmentJson(4, pendingChanges: false))
            .Answer(HttpMethod.Post, Rollbacks, HttpStatusCode.Conflict,
                """{"type":"/problems/current-version-changed","title":"Changed","status":409}""", "application/problem+json");
        using var console = ConsoleRendering.Create(api);
        var page = RenderVersionOne(console);
        page.Find("input[autocomplete=off]").Input("prod");

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Another publication went live after you opened this page.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Contains("Publishes version 5", page.Markup, StringComparison.Ordinal);
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void A_viewer_reads_an_old_version_without_the_way_to_roll_back()
    {
        var api = VersionOneWhileThreeIsLive(pendingChanges: false);
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderVersionOne(console);

        Assert.Single(page.FindAll("tbody tr"));
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void The_breadcrumb_ends_at_the_history()
    {
        var api = LiveAtVersion(3, pendingChanges: false).Answer(HttpMethod.Get, Publications, HttpStatusCode.OK, """{"data":[],"metadata":{}}""");
        using var console = ConsoleRendering.Create(api);

        var page = RenderHistory(console);

        Assert.Equal("History", AccessibleNames.CurrentBreadcrumb(page));
    }

    [Fact]
    public void The_version_table_is_named_and_the_breadcrumb_ends_at_the_version()
    {
        using var console = ConsoleRendering.Create(VersionOneWhileThreeIsLive(pendingChanges: false));

        var page = RenderVersionOne(console);

        page.WaitForElement("table");
        Assert.Equal("Flags in version 1", AccessibleNames.TableName(page));
        Assert.Equal("Version 1", AccessibleNames.CurrentBreadcrumb(page));
    }

    private static StubApi LiveAtVersion(long version, bool pendingChanges)
    {
        var api = new StubApi().Answer(HttpMethod.Get, Environment, HttpStatusCode.OK, EnvironmentJson(version, pendingChanges));
        ConsoleRendering.SignInAs(api, "editor");
        return api;
    }

    private static StubApi VersionOneWhileThreeIsLive(bool pendingChanges) =>
        LiveAtVersion(3, pendingChanges).Answer(HttpMethod.Get, Publications + "/1", HttpStatusCode.OK, VersionJson(1));

    private static string EnvironmentJson(long currentVersion, bool pendingChanges) =>
        $$$"""{"key":"prod","name":"Production","currentVersion":{{{currentVersion}}},"draftRevision":9,"hasUnpublishedChanges":{{{(pendingChanges ? "true" : "false")}}}}""";

    private static string VersionJson(long version) => $$$"""
        {"version":{{{version}}},"publishedAt":"2026-10-01T12:00:00Z","publishedBy":{{{Member}}},"origin":"publication","restoredVersion":null,
         "flags":[{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":1000}]}
        """;

    private static IRenderedComponent<PublicationDetail> RenderVersionOne(BunitContext console) => RenderVersion(console, 1);

    private static IRenderedComponent<PublicationDetail> RenderVersion(BunitContext console, long version) =>
        console.Render<PublicationDetail>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "prod")
            .Add(page => page.Version, version));

    private static IRenderedComponent<PublicationHistory> RenderHistory(BunitContext console) =>
        console.Render<PublicationHistory>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "prod"));
}
