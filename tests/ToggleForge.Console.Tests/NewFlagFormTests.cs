using System.Net;
using Bunit;
using ToggleForge.Console.Api;
using ToggleForge.Console.Components;
using static ToggleForge.Console.Tests.FormAssertions;

namespace ToggleForge.Console.Tests;

public sealed class NewFlagFormTests
{
    private const string Flags = "/api/v1/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev/draft/flags";

    private const string PublicValuesWarning =
        "Every player who has the game can read these values. Never put API keys, passwords or personal data in them.";

    private const string FixMarkedFields = "Nothing was saved: fix the fields marked below.";

    private const string InvalidRollout = "Enter a rollout between 0 and 100, with at most two decimals.";

    private const string CreatedJson =
        """{"key":"shop-config","type":"json","enabled":true,"onValue":1,"offValue":0,"rolloutBasisPoints":0}""";

    [Fact]
    public void The_type_is_one_group_of_two_native_radios_described_by_the_hint()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));

        var form = RenderForm(console);

        var type = form.Find("fieldset[aria-describedby=type-hint]");
        Assert.Equal("Type", type.QuerySelector("legend")?.TextContent);
        Assert.Equal("Chosen now and fixed for good: games read the flag with this type.", form.Find("#type-hint").TextContent);
        var radios = type.QuerySelectorAll("input[type=radio][name=flag-type]");
        Assert.Equal(["boolean", "json"], radios.Select(radio => radio.GetAttribute("value")));
        Assert.All(radios, radio => Assert.Equal("LABEL", radio.ParentElement?.TagName));
        Assert.True(form.Find("#flag-type-boolean").HasAttribute("checked"));
        Assert.False(form.Find("#flag-type-json").HasAttribute("checked"));
    }

    [Fact]
    public void Only_the_json_card_is_named_json()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));

        var form = RenderForm(console);

        Assert.Contains("JSON", form.Find("#flag-type-json").ParentElement?.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON", form.Find("#flag-type-boolean").ParentElement?.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chosen_card_is_highlighted()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));
        var form = RenderForm(console);

        form.Find("#flag-type-json").Change(true);

        Assert.Contains("border-indigo-500", CardOf(form, "#flag-type-json").ClassList);
        Assert.DoesNotContain("border-indigo-500", CardOf(form, "#flag-type-boolean").ClassList);
        Assert.True(form.Find("#flag-type-json").HasAttribute("checked"));
        Assert.False(form.Find("#flag-type-boolean").HasAttribute("checked"));
    }

    [Fact]
    public void A_boolean_flag_is_still_created_with_true_and_false()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created,
            """{"key":"new-shop","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":1250}"""));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("new-shop");
        form.Find("#rollout").Change("12.5");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        Assert.Equal(
            """{"key":"new-shop","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":1250}""",
            api.BodyOf(api.RequestsTo(HttpMethod.Post, Flags).Single()));
        Assert.Empty(form.FindAll("textarea"));
    }

    [Fact]
    public void Creating_a_json_flag_sends_the_type_and_compact_values()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created, CreatedJson));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#rollout").Change("100");
        form.Find("#on-value").Input("{\n  \"discount\": 0.2,\n  \"featuredItems\": [\n    \"sword\"\n  ]\n}");
        form.Find("#off-value").Input("{\"discount\": 0}");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        Assert.Equal(
            """{"key":"shop-config","type":"json","enabled":true,"onValue":{"discount":0.2,"featuredItems":["sword"]},"offValue":{"discount":0},"rolloutBasisPoints":10000}""",
            api.BodyOf(api.RequestsTo(HttpMethod.Post, Flags).Single()));
    }

    // Escapar <, >, & e acentos multiplicaria os bytes do corpo e poderia passar dos 32 KiB com valores de 4 KiB.
    [Fact]
    public void Markup_and_accents_go_to_the_api_as_written()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created, CreatedJson));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("\"<b>é & ü</b>\"");
        form.Find("#off-value").Input("""{"tag":"<i>"}""");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        Assert.Equal(
            """{"key":"shop-config","type":"json","enabled":true,"onValue":"<b>é & ü</b>","offValue":{"tag":"<i>"},"rolloutBasisPoints":0}""",
            api.BodyOf(api.RequestsTo(HttpMethod.Post, Flags).Single()));
    }

    [Fact]
    public void A_json_flag_can_be_created_with_null()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created, CreatedJson));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("null");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        Assert.Equal(
            """{"key":"shop-config","type":"json","enabled":true,"onValue":1,"offValue":null,"rolloutBasisPoints":0}""",
            api.BodyOf(api.RequestsTo(HttpMethod.Post, Flags).Single()));
    }

    [Fact]
    public void Switching_back_to_boolean_sends_true_and_false_and_ignores_the_typed_values()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created,
            """{"key":"shop-config","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":0}"""));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("""{"a":1}""");
        form.Find("#off-value").Input("""{"a":1}""");
        form.Find("#flag-type-boolean").Change(true);
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        Assert.Equal(
            """{"key":"shop-config","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":0}""",
            api.BodyOf(api.RequestsTo(HttpMethod.Post, Flags).Single()));
    }

    [Fact]
    public void Choosing_json_reveals_the_editor_and_boolean_hides_it_keeping_the_text()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));
        var form = RenderForm(console);
        Assert.Empty(form.FindAll("#json-public"));

        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("""{"a":1}""");
        form.Find("#flag-type-boolean").Change(true);
        Assert.Empty(form.FindAll("textarea"));
        form.Find("#flag-type-json").Change(true);

        Assert.Equal("""{"a":1}""", TextOf(form.Find("#on-value")));
    }

    [Fact]
    public void The_new_flag_form_warns_that_json_values_reach_every_player()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));
        var form = RenderForm(console);
        Assert.Empty(form.FindAll("#json-public"));

        form.Find("#flag-type-json").Change(true);

        Assert.Equal(PublicValuesWarning, MessageOf(form.Find("#json-public")));
    }

    [Fact]
    public void The_values_say_who_gets_them_from_the_rollout_typed()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));
        var form = RenderForm(console);

        form.Find("#flag-type-json").Change(true);
        form.Find("#rollout").Change("10");

        Assert.Equal("The 10% of players inside the rollout.", form.Find("#on-value-audience").TextContent);
        Assert.Equal("The other 90%, and every player while the flag is disabled.", form.Find("#off-value-audience").TextContent);
    }

    [Fact]
    public void A_created_flag_resets_the_form_to_boolean_with_empty_values()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created, CreatedJson));
        using var console = ConsoleRendering.Create(api);
        var created = 0;
        var form = RenderForm(console, () => created++);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("0");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Equal(1, created));
        Assert.True(form.Find("#flag-type-boolean").HasAttribute("checked"));
        Assert.Empty(form.FindAll("textarea"));
        Assert.Equal(string.Empty, form.Find("#flag-key").GetAttribute("value"));
        form.Find("#flag-type-json").Change(true);
        Assert.Equal(string.Empty, TextOf(form.Find("#on-value")));
    }

    [Fact]
    public void The_whole_form_is_locked_while_the_flag_is_being_created()
    {
        var api = Editor(new StubApi()
            .Answer(HttpMethod.Post, Flags, HttpStatusCode.Created, CreatedJson)
            .Hold(HttpMethod.Post, Flags, out var release));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("0");

        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        Assert.All(
            ["#flag-key", "#flag-type-boolean", "#flag-type-json", "#rollout", "#on-value", "#off-value"],
            control => Assert.NotNull(form.Find(control).Closest("fieldset[disabled]")));
        release();
        form.WaitForAssertion(() => Assert.Empty(form.FindAll("fieldset[disabled]")));
    }

    // Durante o envio o fieldset desabilitado tira o foco do campo; o bUnit não simula essa perda, mas prova que o
    // foco é devolvido (WCAG 2.4.3).
    [Fact]
    public void A_created_flag_puts_the_focus_back_on_the_key()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Created, CreatedJson));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        var key = ReferenceOf(form.Find("#flag-key"));

        form.Find("#flag-key").Change("shop-config");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Single(api.RequestsTo(HttpMethod.Post, Flags)));
        form.WaitForAssertion(() => AssertFocused(console, key));
    }

    [Fact]
    public void A_refusal_without_a_field_puts_the_focus_back_on_the_button()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Conflict,
            """{"type":"/problems/key-taken","title":"Key taken","status":409,"detail":"The draft already has a flag with the key shop-config."}""",
            "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        var button = ReferenceOf(form.Find("button[type=submit]"));

        form.Find("#flag-key").Change("shop-config");
        form.Find("form").Submit();

        form.WaitForElement("[role=alert]");
        form.WaitForAssertion(() => AssertFocused(console, button));
    }

    [Fact]
    public void Nothing_is_marked_invalid_before_submitting()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));

        var form = RenderForm(console);

        Assert.Empty(form.FindAll("[aria-invalid]"));
        Assert.Empty(form.FindAll("[role=alert]"));
    }

    [Fact]
    public void An_invalid_rollout_is_marked_on_its_field_and_focused()
    {
        var api = Editor(new StubApi());
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        var rollout = ReferenceOf(form.Find("#rollout"));

        form.Find("#flag-key").Change("new-shop");
        form.Find("#rollout").Change("12.345");
        form.Find("form").Submit();

        Assert.Equal("true", form.Find("#rollout").GetAttribute("aria-invalid"));
        Assert.Equal("rollout-error", form.Find("#rollout").GetAttribute("aria-describedby"));
        Assert.Equal(InvalidRollout, MessageOf(form.Find("#rollout-error")));
        Assert.Equal(FixMarkedFields, Assert.Single(form.FindAll("[role=alert]")).TextContent.Trim());
        AssertFocused(console, rollout);
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Flags));
    }

    [Fact]
    public void The_focus_moves_once_and_then_stays_where_the_member_types()
    {
        using var console = ConsoleRendering.Create(Editor(new StubApi()));
        var form = RenderForm(console);
        form.Find("#rollout").Change("12.345");
        form.Find("form").Submit();

        form.Find("#flag-key").Change("new-shop");
        form.Find("#rollout").Change("12.5");

        Assert.Single(console.JSInterop.Invocations, invocation => invocation.Identifier.EndsWith(".focus", StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_the_api_refuses_is_marked_on_its_field_and_focused()
    {
        const string KeyRule = "Keys have 1 to 64 characters from [a-z0-9._-] and start with a letter or digit.";
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.BadRequest, $$$"""
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"{{{KeyRule}}}","source":{"location":"body","pointer":"/key"}}]}
            """, "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        var key = ReferenceOf(form.Find("#flag-key"));

        form.Find("#flag-key").Change("Shop Config");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Equal(KeyRule, MessageOf(form.Find("#flag-key-error"))));
        Assert.Equal("true", form.Find("#flag-key").GetAttribute("aria-invalid"));
        Assert.Equal("flag-key-error", form.Find("#flag-key").GetAttribute("aria-describedby"));
        Assert.Equal(FixMarkedFields, Assert.Single(form.FindAll("[role=alert]")).TextContent.Trim());
        Assert.Equal("Shop Config", form.Find("#flag-key").GetAttribute("value"));
        AssertFocused(console, key);
    }

    [Fact]
    public void Editing_the_key_clears_the_error_the_api_gave_it()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"Keys start with a letter or digit.","source":{"location":"body","pointer":"/key"}}]}
            """, "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        form.Find("#flag-key").Change("-shop");
        form.Find("form").Submit();
        form.WaitForElement("#flag-key-error");

        form.Find("#flag-key").Change("shop");

        Assert.Empty(form.FindAll("#flag-key-error"));
        Assert.Null(form.Find("#flag-key").GetAttribute("aria-invalid"));
        Assert.Empty(form.FindAll("[role=alert]"));
    }

    [Fact]
    public void The_first_invalid_field_in_page_order_gets_the_focus()
    {
        var api = Editor(new StubApi());
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        var rollout = ReferenceOf(form.Find("#rollout"));

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#rollout").Change("12.345");
        form.Find("form").Submit();

        AssertFocused(console, rollout);
        Assert.Equal("Enter a JSON value for included players.", MessageOf(form.Find("#on-value-error")));
        Assert.Equal("Enter a JSON value for everyone else.", MessageOf(form.Find("#off-value-error")));
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Flags));
    }

    [Fact]
    public void A_missing_json_value_focuses_its_area()
    {
        var api = Editor(new StubApi());
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        form.Find("#flag-type-json").Change(true);
        var included = ReferenceOf(form.Find("#on-value"));

        form.Find("#flag-key").Change("shop-config");
        form.Find("#off-value").Input("0");
        form.Find("form").Submit();

        AssertFocused(console, included);
        Assert.Empty(form.FindAll("#off-value-error"));
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Flags));
    }

    [Fact]
    public void Text_that_is_not_json_is_refused_before_reaching_the_api()
    {
        var api = Editor(new StubApi());
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("{discount: 0.2}");
        form.Find("#off-value").Input("{}");
        form.Find("form").Submit();

        Assert.NotEmpty(form.FindAll("#on-value-syntax"));
        Assert.Equal("true", form.Find("#on-value").GetAttribute("aria-invalid"));
        Assert.Empty(form.FindAll("#on-value-error"));
        Assert.Equal(FixMarkedFields, Assert.Single(form.FindAll("[role=alert]")).TextContent.Trim());
        Assert.Empty(api.RequestsTo(HttpMethod.Post, Flags));
    }

    [Fact]
    public void A_value_the_api_refuses_is_shown_on_its_field()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"The value nests deeper than 8 levels.","source":{"location":"body","pointer":"/offValue"}}]}
            """, "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        form.Find("#flag-type-json").Change(true);
        var others = ReferenceOf(form.Find("#off-value"));

        form.Find("#flag-key").Change("shop-config");
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("[[[[[[[[[1]]]]]]]]]");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Equal("The value nests deeper than 8 levels.", MessageOf(form.Find("#off-value-error"))));
        Assert.Equal(FixMarkedFields, Assert.Single(form.FindAll("[role=alert]")).TextContent.Trim());
        AssertFocused(console, others);
    }

    [Fact]
    public void Editing_a_value_clears_the_error_the_api_gave_it()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.BadRequest, """
            {"type":"/problems/invalid-input","title":"Invalid input","status":400,
             "errors":[{"type":"INVALID_VALUE","detail":"The value nests deeper than 8 levels.","source":{"location":"body","pointer":"/offValue"}}]}
            """, "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);
        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("[[[[[[[[[1]]]]]]]]]");
        form.Find("form").Submit();
        form.WaitForElement("#off-value-error");

        form.Find("#off-value").Input("[1]");

        Assert.Empty(form.FindAll("#off-value-error"));
        Assert.Empty(form.FindAll("[role=alert]"));
    }

    [Fact]
    public void The_json_budget_conflict_is_explained_and_keeps_the_form()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Conflict,
            """{"type":"/problems/json-budget-reached","title":"JSON budget reached","status":409,"detail":"A draft can hold at most 128 KiB of json values."}""",
            "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("0");
        form.Find("form").Submit();

        form.WaitForElement("[role=alert]");
        var alert = form.Find("[role=alert]").TextContent;
        Assert.Contains("This draft is out of room for JSON values.", alert, StringComparison.Ordinal);
        Assert.Contains("Nothing was saved.", alert, StringComparison.Ordinal);
        Assert.Equal("shop-config", form.Find("#flag-key").GetAttribute("value"));
        Assert.Equal(["1", "0"], [TextOf(form.Find("#on-value")), TextOf(form.Find("#off-value"))]);
    }

    [Fact]
    public void The_flag_limit_conflict_shows_the_api_detail()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Conflict,
            """{"type":"/problems/limit-reached","title":"Limit reached","status":409,"detail":"A draft can have at most 200 flags."}""",
            "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("0");
        form.Find("form").Submit();

        form.WaitForElement("[role=alert]");
        Assert.Equal("A draft can have at most 200 flags.", form.Find("[role=alert]").TextContent.Trim());
    }

    [Fact]
    public void A_key_already_in_the_draft_shows_the_api_detail()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.Conflict,
            """{"type":"/problems/key-taken","title":"Key taken","status":409,"detail":"The draft already has a flag with the key shop-config."}""",
            "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("form").Submit();

        form.WaitForElement("[role=alert]");
        Assert.Equal("The draft already has a flag with the key shop-config.", form.Find("[role=alert]").TextContent.Trim());
        Assert.Equal("shop-config", form.Find("#flag-key").GetAttribute("value"));
    }

    [Fact]
    public void A_boolean_body_too_large_shows_the_api_detail()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.RequestEntityTooLarge,
            """{"type":"/problems/payload-too-large","title":"Payload too large","status":413,"detail":"The body is over the limit of 32768 bytes."}""",
            "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("new-shop");
        form.Find("form").Submit();

        form.WaitForElement("[role=alert]");
        Assert.Equal("The body is over the limit of 32768 bytes.", form.Find("[role=alert]").TextContent.Trim());
    }

    [Fact]
    public void A_body_too_large_is_explained_next_to_the_values()
    {
        var api = Editor(new StubApi().Answer(HttpMethod.Post, Flags, HttpStatusCode.RequestEntityTooLarge,
            """{"type":"/problems/payload-too-large","title":"Payload too large","status":413,"detail":"The body is over the limit of 32768 bytes."}""",
            "application/problem+json"));
        using var console = ConsoleRendering.Create(api);
        var form = RenderForm(console);

        form.Find("#flag-key").Change("shop-config");
        form.Find("#flag-type-json").Change(true);
        form.Find("#on-value").Input("1");
        form.Find("#off-value").Input("0");
        form.Find("form").Submit();

        form.WaitForAssertion(() => Assert.Equal(
            "This change is too big to send (over 32 KiB), so nothing was saved. Each value can have at most 4 KiB once compacted.",
            form.Find("[data-testid=values-too-large]").TextContent));
    }

    private static AngleSharp.Dom.IElement CardOf(IRenderedComponent<NewFlagForm> form, string radio) =>
        form.Find(radio).ParentElement ?? throw new InvalidOperationException($"{radio} has no card.");

    private static StubApi Editor(StubApi api)
    {
        ConsoleRendering.SignInAs(api, "editor");
        return api;
    }

    private static IRenderedComponent<NewFlagForm> RenderForm(BunitContext console, Action? created = null) =>
        console.Render<NewFlagForm>(parameters => parameters
            .Add(form => form.Address, new EnvironmentAddress(ConsoleRendering.OrganizationId, "space-race", "dev"))
            .Add(form => form.Created, created ?? (() => { })));
}
