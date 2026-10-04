using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Console.Pages;
using static ToggleForge.Console.Tests.FormAssertions;

namespace ToggleForge.Console.Tests;

public sealed class FlagEditorPageTests
{
    private const string NewShop =
        "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev/draft/flags/new-shop";

    private const string AtTenPercent = """{"key":"new-shop","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":1000}""";
    private const string AtFiftyPercent = """{"key":"new-shop","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":5000}""";
    private const string ShopConfig =
        "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev/draft/flags/shop-config";

    private const string ShopConfigJson =
        """{"key":"shop-config","type":"json","enabled":true,"onValue":{"discount":0.2,"items":["sword"]},"offValue":{"discount":0},"rolloutBasisPoints":10000}""";

    private const string ShopConfigAtHalf =
        """{"key":"shop-config","type":"json","enabled":true,"onValue":{"discount":0.2,"items":["sword"]},"offValue":{"discount":0},"rolloutBasisPoints":5000}""";

    private const string IndentedOnValue = "{\n  \"discount\": 0.2,\n  \"items\": [\n    \"sword\"\n  ]\n}";

    private const string PublicValuesWarning =
        "Every player who has the game can read these values. Never put API keys, passwords or personal data in them.";

    private const string OutOfRoomText =
        "All JSON values of an environment add up to at most 128 KiB, because every player downloads all of them. " +
        "Make a value smaller or remove a JSON flag you no longer use, then save again. Nothing was saved.";

    private const string AtThirtyPercent = """{"key":"new-shop","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":3000}""";

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
        Assert.Equal(["Type", "State", "Rollout", "Value for included players", "Value for everyone else"], page.FindAll("dt").Select(term => term.TextContent));
        Assert.Equal(["boolean", "Enabled", "10%", "true", "false"], page.FindAll("dd").Select(value => value.TextContent));
        Assert.Empty(page.FindAll("dd details"));
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
        Assert.Empty(page.FindAll("[data-testid=type-fixed]"));
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

    [Fact]
    public void A_json_flag_opens_with_its_values_indented()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Equal(IndentedOnValue, TextOf(page.Find("#on-value")));
        Assert.Equal("{\n  \"discount\": 0\n}", TextOf(page.Find("#off-value")));
        Assert.Equal("JSON", page.Find("[data-testid=flag-type]").TextContent);
        Assert.Equal("The type is fixed: games already read this flag as JSON.", page.Find("[data-testid=type-fixed]").TextContent);
        Assert.Empty(page.FindAll("select"));
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("viewer")]
    public void The_json_editor_warns_that_values_reach_every_player(string role)
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, role);
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Equal(PublicValuesWarning, MessageOf(page.Find("#json-public")));
    }

    [Fact]
    public void The_json_fieldset_is_described_by_the_public_values_warning()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        var describedBy = page.Find("fieldset[aria-describedby]").GetAttribute("aria-describedby");
        Assert.Equal("json-public", describedBy);
        Assert.Contains("Never put API keys", page.Find($"#{describedBy}").TextContent, StringComparison.Ordinal);
        Assert.Equal("JSON values", page.Find("fieldset > legend.sr-only").TextContent);
        Assert.Empty(page.FindAll("[role=note]"));
    }

    [Fact]
    public void A_boolean_flag_has_no_warning_about_public_values()
    {
        var api = NewShopAtTenPercent();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console);

        Assert.Empty(page.FindAll("#json-public"));
    }

    [Fact]
    public void Each_value_says_who_gets_it_from_the_rollout_typed()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("input[type=number]").Change("25");

        Assert.Equal("The 25% of players inside the rollout.", page.Find("#on-value-audience").TextContent);
        Assert.Equal("The other 75%, and every player while the flag is disabled.", page.Find("#off-value-audience").TextContent);
        page.Find("input[type=checkbox]").Change(false);
        Assert.Equal("Nobody while the flag is disabled.", page.Find("#on-value-audience").TextContent);
    }

    // Escapar <, >, & e acentos multiplicaria os bytes do corpo e poderia passar dos 32 KiB com valores de 4 KiB.
    [Fact]
    public void Markup_and_accents_go_to_the_api_as_written()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK, ShopConfigJson, eTag: "\"5\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input("\"<b>é & ü</b>\"");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig)));
        Assert.Equal("""{"offValue":"<b>é & ü</b>"}""", api.BodyOf(api.RequestsTo(HttpMethod.Patch, ShopConfig).Single()));
    }

    [Fact]
    public void A_changed_json_value_is_sent_compact_with_the_etag_read()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK,
            """{"key":"shop-config","type":"json","enabled":true,"onValue":{"discount":0.5,"items":["sword","shield"]},"offValue":{"discount":0},"rolloutBasisPoints":10000}""",
            eTag: "\"5\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("{ \"discount\": 0.5, \"items\": [\"sword\", \"shield\"] }");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Saved to the draft. Games get it with the next publication.", page.Find("form > [role=status]").TextContent));
        var patch = Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig));
        Assert.Equal(["\"4\""], patch.Headers.GetValues("If-Match"));
        Assert.Equal("""{"onValue":{"discount":0.5,"items":["sword","shield"]}}""", api.BodyOf(patch));
        Assert.Equal("{\n  \"discount\": 0.5,\n  \"items\": [\n    \"sword\",\n    \"shield\"\n  ]\n}", TextOf(page.Find("#on-value")));
    }

    [Fact]
    public void Whitespace_only_edits_have_nothing_to_save()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("""{"discount":0.2,   "items":["sword"]}""");
        page.Find("form").Submit();

        Assert.Equal("Nothing to save: these are already the values in the draft.", page.Find("[role=alert]").TextContent.Trim());
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, ShopConfig));
    }

    [Fact]
    public void A_syntax_error_shows_its_line_and_column_and_marks_the_field_invalid()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("{\n  \"discount\": 0.2\n  \"items\": []\n}");

        var error = page.Find("#on-value-syntax");
        Assert.Equal("Not valid JSON at line 3, column 3.", error.QuerySelector("[data-testid=syntax-summary]")?.TextContent);
        Assert.Equal("3 │   \"items\": []\n  │   ^", error.QuerySelector("pre")?.TextContent);
        Assert.Equal("Size appears once the JSON is valid.", page.Find("#on-value-size").TextContent);
        var area = page.Find("#on-value");
        Assert.Equal("true", area.GetAttribute("aria-invalid"));
        Assert.Equal("on-value-audience on-value-size on-value-syntax", area.GetAttribute("aria-describedby"));
        Assert.Empty(page.FindAll("#on-value-syntax[role]"));
    }

    // O número da linha é texto: red-700 (#c10007) sobre o red-50 (#fef2f2) do quadro dá 5,9:1, acima dos 4,5:1 da
    // WCAG 1.4.3; o red-400 de antes dava 2,6:1.
    [Fact]
    public void The_line_number_of_a_syntax_error_has_the_contrast_of_text()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("{\n  \"discount\": 0.2\n  \"items\": []\n}");

        var gutters = page.FindAll("#on-value-syntax pre > span");
        Assert.Equal(["3 │ ", "  │ "], gutters.Select(gutter => gutter.TextContent));
        Assert.All(gutters, gutter => Assert.Contains("text-red-700", gutter.ClassList));
        Assert.All(gutters, gutter => Assert.Single(gutter.ClassList, name => name.StartsWith("text-", StringComparison.Ordinal)));
    }

    [Fact]
    public void Typing_a_value_does_not_raise_an_alert()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("{");
        page.Find("#off-value").Input(StringOfBytes(4_097));

        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void A_valid_value_is_not_marked_invalid()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        var area = page.Find("#on-value");
        Assert.Null(area.GetAttribute("aria-invalid"));
        Assert.Equal("on-value-audience on-value-size", area.GetAttribute("aria-describedby"));
        Assert.Empty(page.FindAll("#on-value-syntax"));
    }

    [Fact]
    public void The_size_meter_counts_the_bytes_of_the_compacted_value()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Equal("34 of 4,096 bytes", MeterOf(page.Find("#on-value-size")));
    }

    [Theory]
    [InlineData(3_071, "3,071 of 4,096 bytes")]
    [InlineData(3_072, "Near the limit 3,072 of 4,096 bytes")]
    [InlineData(4_096, "Near the limit 4,096 of 4,096 bytes")]
    [InlineData(4_097, "Over the limit by 1 byte 4,097 of 4,096 bytes")]
    [InlineData(4_098, "Over the limit by 2 bytes 4,098 of 4,096 bytes")]
    public void The_size_meter_warns_near_and_over_the_limit_in_words(int bytes, string shown)
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input(StringOfBytes(bytes));

        Assert.Equal(shown, MeterOf(page.Find("#off-value-size")));
    }

    [Theory]
    [InlineData(2, "w-[0%]")]
    [InlineData(102, "w-[0%]")]
    [InlineData(103, "w-[5%]")]
    [InlineData(2_048, "w-[50%]")]
    [InlineData(4_096, "w-[100%]")]
    [InlineData(5_000, "w-[100%]")]
    public void The_meter_bar_grows_in_five_percent_steps_and_stops_at_full(int bytes, string width)
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input(StringOfBytes(bytes));

        var bar = page.Find("#off-value-size [aria-hidden=true] > div");
        Assert.Contains(width, bar.ClassList);
        Assert.Single(bar.ClassList, name => name.StartsWith("w-", StringComparison.Ordinal));
    }

    // A CSP do console é style-src 'self': um atributo style posto pelo Blazor é bloqueado pelo navegador.
    [Fact]
    public void The_json_editor_sets_no_inline_style()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input(StringOfBytes(3_500));
        page.Find("#on-value").Input(StringOfBytes(4_200));

        Assert.Empty(page.FindAll("[style]"));
    }

    [Fact]
    public void A_value_over_the_limit_is_marked_invalid_while_typing()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input(StringOfBytes(4_097));

        Assert.Equal("true", page.Find("#off-value").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Only_the_level_of_the_meter_is_announced_politely_never_the_syntax()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        Assert.Equal(string.Empty, page.Find("#off-value-live[aria-live=polite]").TextContent);
        page.Find("#off-value").Input(StringOfBytes(3_500));
        Assert.Equal("The value for everyone else is near the limit.", page.Find("#off-value-live").TextContent);
        page.Find("#off-value").Input(StringOfBytes(4_097));
        Assert.Equal("The value for everyone else is over the limit.", page.Find("#off-value-live").TextContent);
        page.Find("#off-value").Input("{");
        Assert.Equal(string.Empty, page.Find("#off-value-live").TextContent);
    }

    [Fact]
    public void Saving_an_invalid_value_sends_nothing_and_focuses_the_field()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        var area = ReferenceOf(page.Find("#off-value"));

        page.Find("#off-value").Input("{\"discount\": }");
        page.Find("form").Submit();

        Assert.Equal("Nothing was saved: fix the fields marked below.", page.Find("[role=alert]").TextContent.Trim());
        Assert.Equal("{\"discount\": }", TextOf(page.Find("#off-value")));
        Assert.Empty(page.FindAll("#off-value-error"));
        Assert.Contains("Not valid JSON", page.Find("#off-value-syntax").TextContent, StringComparison.Ordinal);
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, ShopConfig));
        AssertFocused(console, area);
    }

    [Fact]
    public void An_empty_value_is_refused_under_its_field()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input(string.Empty);
        page.Find("form").Submit();

        Assert.Equal("Enter a JSON value for included players.", MessageOf(page.Find("#on-value-error")));
        Assert.Equal("Any JSON value: 3, \"gold\", [1, 2] or an object.", MessageOf(page.Find("#on-value-size")));
        Assert.Contains("on-value-error", page.Find("#on-value").GetAttribute("aria-describedby"), StringComparison.Ordinal);
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, ShopConfig));
    }

    [Fact]
    public void Null_cannot_replace_a_value_when_editing()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input("null");
        page.Find("form").Submit();

        Assert.Equal("null is accepted only when the flag is created. Use another value, such as {} or \"\".", MessageOf(page.Find("#off-value-error")));
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, ShopConfig));
    }

    [Fact]
    public void A_server_error_on_a_value_appears_under_its_field()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"The value nests deeper than 8 levels.","source":{"location":"body","pointer":"/onValue"}}]}
            """, "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        var area = ReferenceOf(page.Find("#on-value"));

        page.Find("#on-value").Input("[1]");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("The value nests deeper than 8 levels.", MessageOf(page.Find("#on-value-error"))));
        Assert.Equal("Nothing was saved: fix the fields marked below.", Assert.Single(page.FindAll("[role=alert]")).TextContent.Trim());
        Assert.Equal("[1]", TextOf(page.Find("#on-value")));
        Assert.Equal("true", page.Find("#on-value").GetAttribute("aria-invalid"));
        Assert.Contains("on-value-error", page.Find("#on-value").GetAttribute("aria-describedby"), StringComparison.Ordinal);
        Assert.Empty(page.FindAll("#on-value-error[role]"));
        AssertFocused(console, area);
    }

    [Fact]
    public void The_environment_budget_conflict_is_explained_and_keeps_the_text()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.Conflict,
            """{"type":"/problems/json-budget-reached","title":"JSON budget reached","status":409,"detail":"The draft would hold 131073 bytes of json values; the limit is 131072."}""",
            "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("[1]");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(
            ["This draft is out of room for JSON values.", OutOfRoomText],
            page.Find("[role=alert]").QuerySelectorAll("p").Select(line => line.TextContent)));
        Assert.Equal("[1]", TextOf(page.Find("#on-value")));
    }

    [Fact]
    public void A_body_too_large_is_explained_next_to_the_values()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.RequestEntityTooLarge,
            """{"type":"/problems/payload-too-large","title":"Payload too large","status":413,"detail":"The body is over the limit of 32768 bytes."}""",
            "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("[1]");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(
            "This change is too big to send (over 32 KiB), so nothing was saved. Each value can have at most 4 KiB once compacted.",
            page.Find("[data-testid=values-too-large]").TextContent));
        Assert.Equal("[1]", TextOf(page.Find("#on-value")));
    }

    [Fact]
    public void A_flag_changed_by_someone_else_reloads_the_json_values_indented()
    {
        var api = ShopConfigRead()
            .ThenAnswer(HttpMethod.Get, ShopConfig, HttpStatusCode.OK, ShopConfigAtHalf.Replace("\"discount\":0}", "\"discount\":0.1}", StringComparison.Ordinal), eTag: "\"6\"")
            .Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.PreconditionFailed,
                """{"type":"/problems/stale-revision","title":"Stale","status":412}""", "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input("[1]");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Someone changed this flag after you opened it.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Equal("{\n  \"discount\": 0.1\n}", TextOf(page.Find("#off-value")));
        Assert.Equal("50", page.Find("input[type=number]").GetAttribute("value"));
    }

    [Fact]
    public void Format_indents_the_value_keeping_key_order()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("""{"b":1,"a":[1]}""");
        page.Find("[data-testid=on-value-format]").Click();

        Assert.Equal("{\n  \"b\": 1,\n  \"a\": [\n    1\n  ]\n}", TextOf(page.Find("#on-value")));
    }

    [Fact]
    public void Format_waits_for_valid_json()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("{");

        Assert.True(page.Find("[data-testid=on-value-format]").HasAttribute("disabled"));
        Assert.False(page.Find("[data-testid=off-value-format]").HasAttribute("disabled"));
    }

    [Fact]
    public void Copying_the_included_value_fills_the_other_one()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Copy from included").Click();

        Assert.Equal(IndentedOnValue, TextOf(page.Find("#off-value")));
    }

    [Fact]
    public void Copying_announces_through_a_status_that_was_already_there()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        Assert.Equal(string.Empty, page.Find("fieldset [role=status].sr-only").TextContent);

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Copy from included").Click();

        Assert.Equal("Value copied.", page.Find("fieldset [role=status].sr-only").TextContent);
    }

    [Fact]
    public void Copying_again_changes_the_status_so_it_is_announced_again()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        var copy = () => page.FindAll("button").Single(button => button.TextContent.Trim() == "Copy from included").Click();

        copy();
        var first = page.Find("fieldset [role=status].sr-only").TextContent;
        copy();
        var second = page.Find("fieldset [role=status].sr-only").TextContent;

        Assert.NotEqual(first, second);
        Assert.Equal("Value copied.", second.Trim());
    }

    [Fact]
    public void Editing_a_value_clears_the_error_the_api_gave_it()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"The value nests deeper than 8 levels.","source":{"location":"body","pointer":"/onValue"}}]}
            """, "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        page.Find("#on-value").Input("[1]");
        page.Find("form").Submit();
        page.WaitForElement("#on-value-error");

        page.Find("#on-value").Input("[2]");

        Assert.Empty(page.FindAll("#on-value-error"));
        Assert.Null(page.Find("#on-value").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void The_summary_leaves_once_the_last_marked_field_is_edited()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        page.Find("#on-value").Input(string.Empty);
        page.Find("form").Submit();
        page.WaitForElement("[role=alert]");

        page.Find("#on-value").Input("[2]");

        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void The_summary_stays_while_another_marked_field_is_left()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        page.Find("#on-value").Input(string.Empty);
        page.Find("#off-value").Input(string.Empty);
        page.Find("form").Submit();
        page.WaitForElement("[role=alert]");

        page.Find("#on-value").Input("[2]");

        Assert.Equal("Nothing was saved: fix the fields marked below.", page.Find("[role=alert]").TextContent.Trim());
    }

    [Fact]
    public void A_raw_half_character_gets_a_neutral_message()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("\"a\uD800b\"");

        Assert.Equal("Not valid JSON: a character in it cannot be encoded.", page.Find("#on-value-syntax [data-testid=syntax-summary]").TextContent);
    }

    [Fact]
    public void Switching_the_flag_off_alone_sends_only_enabled()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK, ShopConfigJson, eTag: "\"5\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("input[type=checkbox]").Change(false);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig)));
        Assert.Equal("""{"enabled":false}""", api.BodyOf(api.RequestsTo(HttpMethod.Patch, ShopConfig).Single()));
    }

    [Fact]
    public void Changing_both_values_sends_both_compacted()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK, ShopConfigJson, eTag: "\"5\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("[ 1, 2 ]");
        page.Find("#off-value").Input("{ \"gold\" : true }");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig)));
        Assert.Equal("""{"onValue":[1,2],"offValue":{"gold":true}}""", api.BodyOf(api.RequestsTo(HttpMethod.Patch, ShopConfig).Single()));
    }

    [Fact]
    public void Formatting_the_value_read_leaves_nothing_to_save()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#on-value").Input("""{"discount":0.2,"items":["sword"]}""");
        page.Find("[data-testid=on-value-format]").Click();
        page.Find("form").Submit();

        Assert.Equal("Nothing to save: these are already the values in the draft.", page.Find("[role=alert]").TextContent.Trim());
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, ShopConfig));
    }

    [Fact]
    public void A_formatted_change_is_sent_with_the_same_compact_text()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK, ShopConfigJson, eTag: "\"5\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("#off-value").Input("""{"discount":0.1,"b":[1]}""");
        page.Find("[data-testid=off-value-format]").Click();
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig)));
        Assert.Equal("""{"offValue":{"discount":0.1,"b":[1]}}""", api.BodyOf(api.RequestsTo(HttpMethod.Patch, ShopConfig).Single()));
    }

    [Fact]
    public void A_viewer_reads_a_json_flag_indented()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Empty(page.FindAll("form"));
        Assert.Empty(page.FindAll("textarea"));
        Assert.Equal(["JSON", "Enabled"], page.FindAll("dd").Take(2).Select(value => value.TextContent));
        Assert.Equal([IndentedOnValue, "{\n  \"discount\": 0\n}"], page.FindAll("dd details[open] pre").Select(value => value.TextContent));
        Assert.Equal(2, page.FindAll("dd details button").Count(button => button.TextContent.Trim() == "Copy"));
        Assert.Empty(page.FindAll("[data-testid=type-fixed]"));
    }

    [Fact]
    public void A_json_value_with_markup_is_shown_as_text_to_a_viewer()
    {
        var api = MarkupRead();
        ConsoleRendering.SignInAs(api, "viewer");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Empty(page.FindAll("script"));
        Assert.Empty(page.FindAll("img"));
        Assert.Equal(["\"<script>x</script>\"", "\"<img src=x onerror=x>\""], page.FindAll("dd pre").Select(value => value.TextContent));
    }

    [Fact]
    public void A_json_value_with_markup_is_shown_as_text_in_the_editor()
    {
        var api = MarkupRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Empty(page.FindAll("script"));
        Assert.Empty(page.FindAll("img"));
        Assert.Equal("\"<script>x</script>\"", TextOf(page.Find("#on-value")));
        Assert.Equal("\"<img src=x onerror=x>\"", TextOf(page.Find("#off-value")));
    }

    [Fact]
    public void A_flag_of_an_unknown_type_opens_read_only()
    {
        var api = new StubApi().Answer(HttpMethod.Get, ShopConfig, HttpStatusCode.OK,
            """{"key":"shop-config","type":"remote-config","enabled":true,"onValue":{"a":1},"offValue":{"a":0},"rolloutBasisPoints":10000}""",
            eTag: "\"4\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);

        var page = RenderEditor(console, "shop-config");

        Assert.Empty(page.FindAll("form"));
        Assert.Equal("remote-config", page.Find("[data-testid=flag-type]").TextContent);
        Assert.Equal("This console cannot edit flags of type \"remote-config\" yet.", page.Find("[data-testid=unknown-type]").TextContent);
        Assert.Equal("remote-config", page.Find("dd [data-testid=flag-type]").TextContent);
        Assert.Equal(["{\n  \"a\": 1\n}", "{\n  \"a\": 0\n}"], page.FindAll("dd details pre").Select(value => value.TextContent));
    }

    [Fact]
    public void A_rollout_that_is_not_basis_points_is_refused_under_its_field()
    {
        var api = NewShopAtTenPercent();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);
        var rollout = ReferenceOf(page.Find("input[type=number]"));

        page.Find("input[type=number]").Change("12.345");
        page.Find("form").Submit();

        Assert.Equal("Enter a rollout between 0 and 100, with at most two decimals.", MessageOf(page.Find("#rollout-error")));
        Assert.Equal("rollout-error", page.Find("input[type=number]").GetAttribute("aria-describedby"));
        Assert.Empty(api.RequestsTo(HttpMethod.Patch, NewShop));
        AssertFocused(console, rollout);
    }

    [Fact]
    public void Saving_a_json_flag_with_only_a_new_rollout_sends_only_the_rollout()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK, ShopConfigAtHalf, eTag: "\"5\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");

        page.Find("input[type=number]").Change("50");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig)));
        Assert.Equal("""{"rolloutBasisPoints":5000}""", api.BodyOf(api.RequestsTo(HttpMethod.Patch, ShopConfig).Single()));
    }

    [Fact]
    public void The_whole_form_is_locked_while_the_flag_is_being_saved()
    {
        var api = ShopConfigRead()
            .Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.OK, ShopConfigJson, eTag: "\"5\"")
            .Hold(HttpMethod.Patch, ShopConfig, out var release);
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        page.Find("#on-value").Input("[1]");

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, ShopConfig)));
        Assert.All(
            ["input[type=checkbox]", "#rollout", "#on-value", "#off-value"],
            control => Assert.NotNull(page.Find(control).Closest("fieldset[disabled]")));
        release();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("fieldset[disabled]")));
    }

    [Fact]
    public void The_boolean_values_are_locked_while_the_flag_is_being_saved()
    {
        var api = NewShopAtTenPercent()
            .Answer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, AtFiftyPercent, eTag: "\"8\"")
            .Hold(HttpMethod.Patch, NewShop, out var release);
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);
        page.Find("#rollout").Change("50");

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Patch, NewShop)));
        Assert.All(page.FindAll("select"), choice => Assert.NotNull(choice.Closest("fieldset[disabled]")));
        release();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("fieldset[disabled]")));
    }

    // Um leitor de tela anuncia o campo como ele está quando o foco chega: o erro já tem de estar ligado a ele.
    [Fact]
    public void A_value_refused_before_sending_is_marked_by_the_time_the_focus_reaches_it()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        var witness = WatchFocus(console, () => page.Find("#on-value").GetAttribute("aria-describedby"));

        page.Find("#on-value").Input(string.Empty);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(["on-value-audience on-value-size on-value-error"], witness.Seen));
    }

    [Fact]
    public void A_value_the_api_refuses_is_marked_by_the_time_the_focus_reaches_it()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"The value nests deeper than 8 levels.","source":{"location":"body","pointer":"/offValue"}}]}
            """, "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        var witness = WatchFocus(console, () => page.Find("#off-value").GetAttribute("aria-invalid"));

        page.Find("#off-value").Input("[1]");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(["true"], witness.Seen));
    }

    [Fact]
    public void The_focus_moves_once_and_then_stays_where_the_member_types()
    {
        var api = ShopConfigRead();
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        page.Find("#on-value").Input(string.Empty);
        page.Find("form").Submit();

        page.Find("#off-value").Input("[2]");
        page.Find("#rollout").Change("12.5");

        Assert.Single(console.JSInterop.Invocations, invocation => invocation.Identifier.EndsWith(".focus", StringComparison.Ordinal));
    }

    // Durante o envio o formulário travado tira o foco de onde ele estava; o bUnit não simula essa perda, mas prova
    // que o foco é devolvido (WCAG 2.4.3).
    [Fact]
    public void A_saved_flag_puts_the_focus_back_on_save()
    {
        var api = NewShopAtTenPercent().Answer(HttpMethod.Patch, NewShop, HttpStatusCode.OK, AtFiftyPercent, eTag: "\"8\"");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);
        var save = ReferenceOf(page.Find("button[type=submit]"));

        page.Find("#rollout").Change("50");
        page.Find("form").Submit();

        page.WaitForElement("[role=status]");
        page.WaitForAssertion(() => AssertFocused(console, save));
    }

    [Fact]
    public void A_refusal_without_a_field_puts_the_focus_back_on_save()
    {
        var api = ShopConfigRead().Answer(HttpMethod.Patch, ShopConfig, HttpStatusCode.Conflict,
            """{"type":"/problems/json-budget-reached","title":"JSON budget reached","status":409,"detail":"Over."}""",
            "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console, "shop-config");
        var save = ReferenceOf(page.Find("button[type=submit]"));

        page.Find("#on-value").Input("[1]");
        page.Find("form").Submit();

        page.WaitForElement("[role=alert]");
        page.WaitForAssertion(() => AssertFocused(console, save));
    }

    [Fact]
    public void A_flag_gone_from_the_draft_moves_no_focus()
    {
        var api = NewShopAtTenPercent().Answer(HttpMethod.Patch, NewShop, HttpStatusCode.NotFound,
            """{"type":"/problems/not-found","title":"Not found","status":404}""", "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);

        page.Find("#rollout").Change("50");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("This flag left the draft after you opened it.", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(console.JSInterop.Invocations, invocation => invocation.Identifier.EndsWith(".focus", StringComparison.Ordinal));
    }

    // Hoje o console só manda true ou false, mas o contrato tem value-not-of-flag-type: o erro fica ligado ao select.
    [Theory]
    [InlineData("/onValue", 0, "on-value-error")]
    [InlineData("/offValue", 1, "off-value-error")]
    public void A_boolean_value_the_api_refuses_is_marked_on_its_choice_and_focused(string field, int choice, string errorId)
    {
        var api = NewShopAtTenPercent().Answer(HttpMethod.Patch, NewShop, HttpStatusCode.BadRequest, $$$"""
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"VALUE_NOT_OF_FLAG_TYPE","detail":"Use true or false.","source":{"location":"body","pointer":"{{{field}}}"}}]}
            """, "application/problem+json");
        ConsoleRendering.SignInAs(api, "editor");
        using var console = ConsoleRendering.Create(api);
        var page = RenderEditor(console);
        var select = ReferenceOf(page.FindAll("select")[choice]);

        page.FindAll("select")[choice].Change(choice == 0 ? "false" : "true");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Use true or false.", MessageOf(page.Find($"#{errorId}"))));
        var marked = page.FindAll("select")[choice];
        Assert.Equal(["true", errorId], [marked.GetAttribute("aria-invalid"), marked.GetAttribute("aria-describedby")]);
        Assert.Null(page.FindAll("select")[1 - choice].GetAttribute("aria-invalid"));
        page.WaitForAssertion(() => AssertFocused(console, select));
    }

    private static StubApi MarkupRead() =>
        new StubApi().Answer(HttpMethod.Get, ShopConfig, HttpStatusCode.OK,
            """{"key":"shop-config","type":"json","enabled":true,"onValue":"<script>x</script>","offValue":"<img src=x onerror=x>","rolloutBasisPoints":10000}""",
            eTag: "\"4\"");

    // Uma string JSON com exatamente esse tamanho em bytes depois de compactada: as aspas contam.
    private static string StringOfBytes(int bytes) => $"\"{new string('a', bytes - 2)}\"";

    private static string MeterOf(AngleSharp.Dom.IElement meter) => string.Join(' ', meter.QuerySelectorAll("p > span").Select(MessageOf));

    private static StubApi ShopConfigRead() =>
        new StubApi().Answer(HttpMethod.Get, ShopConfig, HttpStatusCode.OK, ShopConfigJson, eTag: "\"4\"");

    private static StubApi NewShopAtTenPercent() =>
        new StubApi().Answer(HttpMethod.Get, NewShop, HttpStatusCode.OK, AtTenPercent, eTag: "\"7\"");

    private static FocusWitness WatchFocus(BunitContext console, Func<string?> read)
    {
        var witness = new FocusWitness(read);
        console.JSInterop.AddInvocationHandler(witness);
        return witness;
    }

    private static IRenderedComponent<FlagEditor> RenderEditor(BunitContext console, string flagKey = "new-shop") =>
        console.Render<FlagEditor>(parameters => parameters
            .Add(page => page.OrganizationId, ConsoleRendering.OrganizationId)
            .Add(page => page.ProjectKey, "space-race")
            .Add(page => page.EnvironmentKey, "dev")
            .Add(page => page.FlagKey, flagKey));
}
