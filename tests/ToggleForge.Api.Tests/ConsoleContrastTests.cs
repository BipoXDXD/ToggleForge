using System.Net;
using Microsoft.Playwright;
using ToggleForge.Api.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O que o bUnit não vê porque depende do CSS calculado pelo navegador: contraste de bordas e textos (WCAG 1.4.3 e
/// 1.4.11), o contorno do foco do teclado (2.4.7) e a largura do medidor de bytes sob a CSP do console.
/// </summary>
public sealed class ConsoleContrastTests(ApiFactory api, Browser browser) : IClassFixture<Browser>, IAsyncLifetime
{
    private const double ControlBorderMinimum = 3.0;
    private const double TextMinimum = 4.5;

    private BrowserHost? _host;

    private BrowserHost Host => _host ?? throw new InvalidOperationException("The host has not started.");

    public static TheoryData<string> PagesWithControls => ["sign-in", "projects", "draft-json", "flag-editor-json", "publish", "version", "simulator"];

    public ValueTask InitializeAsync()
    {
        _host = new BrowserHost(api);
        return ValueTask.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(PagesWithControls))]
    public async Task Form_controls_have_a_three_to_one_border_and_a_visible_focus_outline(string consolePage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await PrepareProjectAsync(owner);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await OpenAsync(page, owner, consolePage);
        var borders = await ContrastProbe.FieldBordersAsync(page);
        var stops = await ContrastProbe.TabStopsAsync(page);

        Assert.NotEmpty(borders);
        Assert.All(borders, field => Assert.True(field.Ratio >= ControlBorderMinimum, $"{field.Control}: {field.Ratio:F2}:1"));
        Assert.NotEmpty(stops);
        Assert.All(stops, stop => Assert.True(
            stop.Style == "solid" && stop.Width >= 2, $"{stop.Element}: outline {stop.Style} {stop.Width}px"));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("rollback")]
    public async Task The_check_the_result_button_meets_text_contrast(string operation)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await PrepareProjectAsync(owner);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        // Sem resposta, o console não sabe se a operação aconteceu e oferece conferir o resultado.
        await page.RouteAsync(
            operation == "publish" ? "**/publications" : "**/rollbacks",
            route => route.Request.Method == "POST" ? route.AbortAsync() : route.ContinueAsync());

        await OpenAsync(page, owner, operation == "publish" ? "publish" : "version");
        if (operation == "rollback")
        {
            await page.GetByLabel("Discard the unpublished changes of the draft.").CheckAsync();
        }

        await page.GetByLabel("Type dev to confirm").FillAsync("dev");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^(Publish|Roll back)") }).ClickAsync();
        var check = page.GetByRole(AriaRole.Button, new() { Name = "Check the result" });
        await Expect(check).ToBeEnabledAsync();
        var resting = await ContrastProbe.TextRatioAsync(check);
        await check.HoverAsync();
        var hovered = await ContrastProbe.TextRatioAsync(check);

        Assert.True(resting >= TextMinimum, $"at rest: {resting:F2}:1");
        Assert.True(hovered >= TextMinimum, $"hovered: {hovered:F2}:1");
    }

    /// <summary>
    /// O número da linha com o erro fica na calha, em red-700 sobre o red-50 do quadro, e precisa do contraste de texto.
    /// </summary>
    [Fact]
    public async Task The_line_number_of_a_syntax_error_meets_text_contrast()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var gutters = page.Locator("#on-value-syntax pre > span");

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Radio, new() { Name = "JSON" }).CheckAsync();
        await page.Locator("#on-value").FillAsync("{\n  \"discount\": 0.2\n  \"items\": []\n}");
        await Expect(gutters).ToHaveCountAsync(2);
        var ratios = new List<double>();
        foreach (var gutter in await gutters.AllAsync())
        {
            ratios.Add(await ContrastProbe.TextRatioAsync(gutter));
        }

        Assert.Equal("3 │ ", await gutters.First.TextContentAsync());
        Assert.All(ratios, ratio => Assert.True(ratio >= TextMinimum, $"line number: {ratio:F2}:1"));
    }

    /// <summary>
    /// O rádio esconde o próprio contorno, e o cartão inteiro mostra o foco, só quando ele vem do teclado: o cartão
    /// clicado com o mouse fica com o foco e sem contorno (A10, F7).
    /// </summary>
    [Fact]
    public async Task The_type_cards_show_the_keyboard_focus_on_the_whole_card()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var booleanCard = page.Locator("label").Filter(new() { Has = page.Locator("#flag-type-boolean") });
        var jsonCard = page.Locator("label").Filter(new() { Has = page.Locator("#flag-type-json") });

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.Locator("#flag-key").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator("#flag-type-boolean")).ToBeFocusedAsync();
        await Expect(booleanCard).ToHaveCSSAsync("outline-style", "solid");
        await Expect(booleanCard).ToHaveCSSAsync("outline-width", "2px");
        var unselectedBorder = await ContrastProbe.BorderRatioAsync(jsonCard);
        await jsonCard.ClickAsync();

        await Expect(page.Locator("#flag-type-json")).ToBeFocusedAsync();
        await Expect(jsonCard).ToHaveCSSAsync("outline-style", "none");
        await Expect(booleanCard).ToHaveCSSAsync("outline-style", "none");
        Assert.True(unselectedBorder >= ControlBorderMinimum, $"unselected card border: {unselectedBorder:F2}:1");
    }

    /// <summary>
    /// A CSP do console (<c>style-src 'self'</c>) bloqueia <c>style="width: …"</c>; a barra usa classes de largura. A
    /// barra precisa ter a largura proporcional ao valor sem violar a política.
    /// </summary>
    [Fact]
    public async Task The_size_meter_bar_has_a_width_and_breaks_no_content_security_policy()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            "window.__csp = []; document.addEventListener('securitypolicyviolation', e => window.__csp.push(e.violatedDirective));");

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Radio, new() { Name = "JSON" }).CheckAsync();
        await page.Locator("#on-value").FillAsync(ConsoleClient.JsonTextOf(2_048));
        await Expect(page.Locator("#on-value-size")).ToContainTextAsync("2,048 of 4,096 bytes");
        var track = await page.Locator("#on-value-size [aria-hidden=true]").BoundingBoxAsync();
        var bar = await page.Locator("#on-value-size [aria-hidden=true] > div").BoundingBoxAsync();

        Assert.NotNull(track);
        Assert.NotNull(bar);
        Assert.InRange(bar.Width / track.Width, 0.49, 0.51);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__csp.length"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>
    /// Uma flag JSON publicada duas vezes (a versão 1 deixa de estar no ar e pode ser restaurada) e uma booleana ainda
    /// no rascunho, para a página de publicação ter o que publicar.
    /// </summary>
    private static async Task PrepareProjectAsync(ConsoleClient owner)
    {
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        var json = await owner.CreateJsonFlagAsync("space-race", "shop-config", """{"discount":0.2}""", """{"discount":0}""");
        Assert.Equal(HttpStatusCode.Created, json.Status);
        Assert.Equal(
            HttpStatusCode.Created,
            (await owner.PublishAsync("space-race", ConsoleClient.Revision(json), ConsoleClient.NewIdempotencyKey())).Status);
        var widened = await owner.UpdateFlagAsync("space-race", "shop-config", json.Header("ETag"), """{"rolloutBasisPoints":5000}""");
        Assert.Equal(
            HttpStatusCode.Created,
            (await owner.PublishAsync("space-race", ConsoleClient.Revision(widened), ConsoleClient.NewIdempotencyKey())).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "new-shop")).Status);
    }

    private static async Task OpenAsync(IPage page, ConsoleClient owner, string consolePage)
    {
        switch (consolePage)
        {
            case "sign-in":
                await page.GotoAsync("/sign-in");
                await Expect(page.GetByLabel("Email")).ToBeVisibleAsync();
                return;
            case "projects":
                await page.GotoAsync("/sign-in");
                await ConsolePages.SignInAsync(page, owner.Email, ConsoleClient.Password);
                await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Projects" })).ToBeVisibleAsync();
                return;
        }

        await ConsolePages.OpenDraftAsync(page, owner);
        switch (consolePage)
        {
            case "draft-json":
                await page.GetByRole(AriaRole.Radio, new() { Name = "JSON" }).CheckAsync();
                await Expect(page.Locator("#on-value")).ToBeVisibleAsync();
                break;
            case "flag-editor-json":
                await page.GetByRole(AriaRole.Link, new() { Name = "shop-config" }).ClickAsync();
                await Expect(page.Locator("#on-value")).ToBeVisibleAsync();
                break;
            case "publish":
                await page.GetByRole(AriaRole.Link, new() { Name = "Review and publish" }).ClickAsync();
                await Expect(page.GetByLabel("Type dev to confirm")).ToBeVisibleAsync();
                break;
            case "version":
                await page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
                await page.GetByRole(AriaRole.Link, new() { Name = "Version 1" }).ClickAsync();
                await Expect(page.GetByLabel("Type dev to confirm")).ToBeVisibleAsync();
                break;
            case "simulator":
                await page.GetByRole(AriaRole.Link, new() { Name = "Simulate" }).ClickAsync();
                await page.GetByLabel("Player ID").FillAsync("player-42");
                await page.GetByRole(AriaRole.Button, new() { Name = "Simulate" }).ClickAsync();
                await Expect(page.Locator("#simulation-results")).ToBeVisibleAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(consolePage), consolePage, "No such console page in this test.");
        }
    }
}
