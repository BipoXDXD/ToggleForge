using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Console.Pages;

namespace ToggleForge.Console.Tests;

public sealed class FlagEditorPageTests
{
    private const string NewShop =
        "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev/draft/flags/new-shop";

    private const string AtTenPercent = """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":1000}""";
    private const string AtFiftyPercent = """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":5000}""";
    private const string AtThirtyPercent = """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":3000}""";

    [Fact]
    public void Saving_sends_only_what_changed_with_the_etag_read()
    {
        var api = NewShopAtTenPercent().Answer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, AtFiftyPercent, eTag: "\"8\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.Find("input[type=number]").Change("50");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Saved to the draft. Games get it with the next publication.", page.Find("[role=status]").TextContent));
        var patch = Assert.Single(api.RequestsTo(HttpMethod.Patch, NewShop));
        Assert.Equal(["\"7\""], patch.Headers.GetValues("If-Match"));
        Assert.Equal("""{"rolloutBasisPoints":5000}""", api.BodyOf(patch));
    }

    [Fact]
    public void A_second_save_uses_the_etag_of_the_first()
    {
        var api = NewShopAtTenPercent()
            .Answer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, AtFiftyPercent, eTag: "\"8\"")
            .ThenAnswer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, AtThirtyPercent, eTag: "\"9\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.Find("input[type=number]").Change("50");
        page.Find("form").Submit();
        page.WaitForElement("[role=status]");
        page.Find("input[type=number]").Change("30");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(2, api.RequestsTo(HttpMethod.Patch, NewShop).Count()));
        Assert.Equal(["\"8\""], api.RequestsTo(HttpMethod.Patch, NewShop).Last().Headers.GetValues("If-Match"));
    }

    [Fact]
    public void A_flag_changed_by_someone_else_shows_its_current_values_instead_of_overwriting_them()
    {
        var api = NewShopAtTenPercent()
            .ThenAnswer(HttpMethod.Get, NewShop, HttpStatusCode.OK, AtThirtyPercent, eTag: "\"9\"")
            .Answer(HttpMethod.Patch, NewShop, HttpStatusCode.PreconditionFailed,
                """{"type":"/problems/stale-revision","title":"Stale","status":412}""", "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.Find("input[type=number]").Change("50");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Someone changed this flag after you opened it.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Equal("30", page.Find("input[type=number]").GetAttribute("value"));
        Assert.Single(api.RequestsTo(HttpMethod.Patch, NewShop));
    }

    [Fact]
    public void The_form_shows_the_values_read()
    {
        var api = NewShopAtTenPercent();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console);

        Assert.Equal(["true", "false"], page.FindAll("select").Select(choice => choice.GetAttribute("value")));
        Assert.Equal("10", page.Find("input[type=number]").GetAttribute("value"));
    }

    [Fact]
    public void Swapping_the_values_sends_both()
    {
        var api = NewShopAtTenPercent().Answer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, AtFiftyPercent, eTag: "\"8\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.FindAll("select")[0].Change("false");
        page.FindAll("select")[1].Change("true");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, NewShop)));
        Assert.Equal("""{"onValue":false,"offValue":true}""", api.BodyOf(api.RequestsTo(HttpMethod.Patch, NewShop).Single()));
    }

    [Fact]
    public void Saving_untouched_values_sends_nothing()
    {
        var api = NewShopAtTenPercent();
        ConsoleRendering.SignInAs(api, "owner");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.Find("form").Submit();

        Assert.Equal("Nothing to save: these are already the values in the draft.", page.Find("[role=alert]").TextContent.Trim());
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, NewShop));
    }

    [Fact]
    public void Removing_asks_first_and_then_sends_the_etag_read()
    {
        var api = NewShopAtTenPercent().Answer(HttpMethod.Delete, NewShop, HttpStatusCode.NoContent, string.Empty);
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Remove from draft").Click();
        Assert.Empty(api.RequestsTo(HttpMethod.Delete, NewShop));
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Remove").Click();

        var navigation = console.Services.GetRequiredService<NavigationManager>();
        page.WaitForAssertion(() => Assert.Equal(
            "http://localhost/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev", navigation.Uri));
        Assert.Equal(["\"7\""], Assert.Single(api.RequestsTo(HttpMethod.Delete, NewShop)).Headers.GetValues("If-Match"));
    }

    [Fact]
    public void A_viewer_reads_the_flag_without_the_form_to_change_it()
    {
        var api = NewShopAtTenPercent();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console);

        Assert.Empty(page.FindAll("form"));
        Assert.Equal(["Enabled", "10%", "true", "false"], page.FindAll("dd").Select(value => value.TextContent));
    }

    [Fact]
    public void A_flag_missing_from_the_draft_says_so()
    {
        var api = new StubApi();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console);

        page.WaitForAssertion(() => Assert.Contains("This flag is not in the draft.", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void The_breadcrumb_ends_at_the_flag()
    {
        var api = NewShopAtTenPercent();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console);

        Assert.Equal("new-shop", AccessibleNames.CurrentBreadcrumb(page));
    }

    private static StubApi NewShopAtTenPercent() =>
        new StubApi().Answer(HttpMethod.Get, NewShop, HttpStatusCode.OK, AtTenPercent, eTag: "\"7\"");

    private static IRenderedComponent<FlagEditor> RenderEditor(BunitContext console) =>
        console.Render<FlagEditor>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "dev")
            .Add(page => page.FlagKey, "new-shop"));
}
