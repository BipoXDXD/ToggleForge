using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Console.Pages;

namespace ToggleForge.Console.Tests;

public sealed class SimulatorPageTests
{
    private const string Environment = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/prod";
    private const string Simulations = Environment + "/simulations";
    private const string Development = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev";
    private const string PlayerId = "player-CANARY-6f0d2c1e";

    [Fact]
    public void A_draft_simulation_explains_each_flag()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """
            {"source":"draft","version":null,"results":[
              {"flagKey":"beta-map","type":"boolean","value":false,"reason":"excluded","bucket":7310},
              {"flagKey":"new-shop","type":"boolean","value":true,"reason":"included","bucket":412},
              {"flagKey":"old-ui","type":"boolean","value":false,"reason":"disabled","bucket":null}]}
            """);
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);

        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("tbody tr").Count));
        Assert.Equal("Draft", page.Find("#simulation-results").TextContent);
        Assert.Equal(
            [
                ["beta-map", "false", "Bucket 7310 is outside the rollout."],
                ["new-shop", "true", "Bucket 412 is inside the rollout."],
                ["old-ui", "false", "Disabled: everyone gets the off value."],
            ],
            page.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent).ToArray()));
        Assert.Equal($$"""{"playerId":"{{PlayerId}}","source":"draft"}""", api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Post, Simulations))));
    }

    [Fact]
    public void A_published_version_is_simulated_starting_from_the_live_one()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK,
            """{"source":"publication","version":3,"results":[{"flagKey":"new-shop","type":"boolean","value":true,"reason":"included","bucket":null}]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);

        page.Find("input[autocomplete=off]").Change(PlayerId);
        Assert.True(page.Find("input[aria-label='Version number']").HasAttribute("disabled"));
        page.FindAll("input[type=radio]")[1].Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Version 3", page.Find("#simulation-results").TextContent));
        Assert.Equal("The rollout includes everyone.", page.FindAll("tbody td")[2].TextContent);
        Assert.Equal(
            $$"""{"playerId":"{{PlayerId}}","source":"publication","version":3}""",
            api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Post, Simulations))));
    }

    [Fact]
    public void A_typed_version_is_the_one_simulated()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"publication","version":2,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.FindAll("input[type=radio]")[1].Change(true);

        page.Find("input[aria-label='Version number']").Change("2");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Version 2", page.Find("#simulation-results").TextContent));
        Assert.Contains("\"version\":2", api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Post, Simulations))), StringComparison.Ordinal);
    }

    [Fact]
    public void Going_back_to_the_draft_leaves_the_version_out()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.FindAll("input[type=radio]")[1].Change(true);

        page.FindAll("input[type=radio]")[0].Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Draft", page.Find("#simulation-results").TextContent));
        Assert.Equal($$"""{"playerId":"{{PlayerId}}","source":"draft"}""", api.BodyOf(Assert.Single(api.RequestsTo(HttpMethod.Post, Simulations))));
    }

    [Fact]
    public void The_chosen_source_is_the_only_one_checked()
    {
        using var console = ConsoleRendering.Create(LiveAtVersion(3));
        var page = RenderSimulator(console);
        Assert.Equal([true, false], page.FindAll("input[type=radio]").Select(radio => radio.HasAttribute("checked")));

        page.FindAll("input[type=radio]")[1].Change(true);

        Assert.Equal([false, true], page.FindAll("input[type=radio]").Select(radio => radio.HasAttribute("checked")));
    }

    [Fact]
    public void A_cleared_version_is_asked_for_before_reaching_the_api()
    {
        var api = LiveAtVersion(3);
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.FindAll("input[type=radio]")[1].Change(true);

        page.Find("input[aria-label='Version number']").Change(string.Empty);
        page.Find("form").Submit();

        Assert.Equal("Enter the version to simulate.", page.Find("[role=alert]").TextContent.Trim());
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Simulations));
    }

    [Fact]
    public void An_environment_that_cannot_be_read_shows_why_instead_of_the_form()
    {
        var api = new StubApi();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderSimulator(console);

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[role=alert]")));
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void An_environment_never_published_only_simulates_the_draft()
    {
        var api = new StubApi().Answer(HttpMethod.Get, Environment, HttpStatusCode.OK, EnvironmentJson("null"));
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderSimulator(console);

        page.WaitForAssertion(() => Assert.Contains("Nothing published yet, so only the draft can be simulated.", page.Markup, StringComparison.Ordinal));
        Assert.Single(page.FindAll("input[type=radio]"));
        Assert.Empty(page.FindAll("input[type=number]"));
    }

    [Fact]
    public void A_refused_simulation_replaces_the_previous_results_with_the_reason()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK,
            """{"source":"draft","version":null,"results":[{"flagKey":"new-shop","type":"boolean","value":true,"reason":"included","bucket":412}]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));

        api.Answer(HttpMethod.Post, Simulations, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-request","title":"Invalid request","status":400,
             "errors":[{"detail":"Use 1 to 256 characters, not all blank, without control characters or unpaired surrogates."}]}
            """, "application/problem+json");
        page.Find("input[autocomplete=off]").Change("   ");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Use 1 to 256 characters", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void A_successful_simulation_clears_the_previous_problem()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.FindAll("input[type=radio]")[1].Change(true);
        page.Find("input[aria-label='Version number']").Change(string.Empty);
        page.Find("form").Submit();
        Assert.Single(page.FindAll("[role=alert]"));

        page.FindAll("input[type=radio]")[0].Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("#simulation-results")));
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void The_simulate_button_is_free_again_after_an_answer()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("#simulation-results")));
        Assert.False(page.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void Another_environment_starts_from_the_draft_without_the_previous_results()
    {
        var api = LiveAtVersion(3)
            .Answer(HttpMethod.Get, Development, HttpStatusCode.OK, """{"key":"dev","name":"Development","currentVersion":1,"draftRevision":2,"hasUnpublishedChanges":false}""")
            .Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"publication","version":3,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.FindAll("input[type=radio]")[1].Change(true);
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("#simulation-results")));

        page.Render(parameters => parameters.Add(simulator => simulator.EnvironmentKey, "dev"));

        page.WaitForAssertion(() => Assert.Contains("Development (dev)", page.Find("h1").TextContent, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("#simulation-results"));
        Assert.True(page.Find("input[aria-label='Version number']").HasAttribute("disabled"));
    }

    [Fact]
    public void The_results_name_the_player_they_were_simulated_for()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();

        page.Find("input[autocomplete=off]").Change("someone-else");

        page.WaitForAssertion(() => Assert.Contains($"For player {PlayerId}", page.Find("section").TextContent, StringComparison.Ordinal));
        Assert.Contains("No flags to evaluate.", page.Find("section").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_results_arrive_in_a_region_screen_readers_announce()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.WaitForElement("[aria-live=polite]");

        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Draft", page.Find("[aria-live=polite] #simulation-results").TextContent));
    }

    [Fact]
    public void A_simulation_answered_after_switching_environments_is_dropped()
    {
        var api = LiveAtVersion(3)
            .Answer(HttpMethod.Get, Development, HttpStatusCode.OK, """{"key":"dev","name":"Development","currentVersion":null,"draftRevision":1,"hasUnpublishedChanges":true}""")
            .Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""")
            .Hold(HttpMethod.Post, Simulations, out var answer);
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);
        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();

        page.Render(parameters => parameters.Add(simulator => simulator.EnvironmentKey, "dev"));
        page.WaitForAssertion(() => Assert.Contains("Development (dev)", page.Find("h1").TextContent, StringComparison.Ordinal));
        answer();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Simulations)));
        Assert.Empty(page.FindAll("#simulation-results"));
        Assert.False(page.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void The_player_id_stays_out_of_every_address()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK, """{"source":"draft","version":null,"results":[]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);

        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Simulations)));
        Assert.All(api.Requests, request => Assert.DoesNotContain("CANARY", request.RequestUri?.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain("CANARY", console.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void The_results_table_is_named_by_its_heading_and_the_breadcrumb_ends_at_the_simulator()
    {
        var api = LiveAtVersion(3).Answer(HttpMethod.Post, Simulations, HttpStatusCode.OK,
            """{"source":"draft","version":null,"results":[{"flagKey":"new-shop","value":true,"reason":"included","bucket":412}]}""");
        using var console = ConsoleRendering.Create(api);
        var page = RenderSimulator(console);

        page.Find("input[autocomplete=off]").Change(PlayerId);
        page.Find("form").Submit();

        page.WaitForElement("table");
        Assert.Equal("Draft", AccessibleNames.TableName(page));
        Assert.Equal("Simulator", AccessibleNames.CurrentBreadcrumb(page));
    }

    [Fact]
    public void The_heading_is_there_while_the_environment_loads()
    {
        var api = LiveAtVersion(3).Hold(HttpMethod.Get, Environment, out var release);
        using var console = ConsoleRendering.Create(api);

        var page = RenderSimulator(console);

        Assert.Equal("Simulate a player in prod", page.Find("h1").TextContent);
        release();
        page.WaitForAssertion(() => Assert.Equal("Simulate a player in Production (prod)", page.Find("h1").TextContent));
    }

    private static StubApi LiveAtVersion(long version)
    {
        var api = new StubApi().Answer(HttpMethod.Get, Environment, HttpStatusCode.OK, EnvironmentJson(version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        ConsoleRendering.SignInAs(api, "viewer");
        return api;
    }

    private static string EnvironmentJson(string currentVersion) =>
        $$$"""{"key":"prod","name":"Production","currentVersion":{{{currentVersion}}},"draftRevision":9,"hasUnpublishedChanges":false}""";

    private static IRenderedComponent<Simulator> RenderSimulator(BunitContext console) =>
        console.Render<Simulator>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "prod"));
}
