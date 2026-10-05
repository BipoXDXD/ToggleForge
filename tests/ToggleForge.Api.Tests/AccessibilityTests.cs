using System.Net;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using ToggleForge.Api.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Acessibilidade do console num Chromium de verdade: o axe-core nas páginas da jornada, mais o que ele não mede
/// (foco depois de navegar, skip link e reflow em 320 px).
/// </summary>
public sealed class AccessibilityTests(ApiFactory api, Browser browser) : IClassFixture<Browser>, IAsyncLifetime
{
    /// <summary>
    /// As regras que o console já cumpre, fixadas com zero violação. Ainda não é a varredura WCAG 2.1 AA inteira: as
    /// tags <c>wcag2a</c>/<c>wcag2aa</c>/<c>wcag21aa</c> entram no lugar da lista com o PR de acessibilidade seguinte.
    /// O contraste é provado pelo <see cref="ConsoleContrastTests"/>, que mede no navegador o texto e a borda de
    /// controle (WCAG 1.4.11), que o axe não mede.
    /// </summary>
    private static readonly IReadOnlyList<string> _pinnedRules =
    [
        "region",
        "landmark-one-main",
        "page-has-heading-one",
        "aria-allowed-role",
        "aria-required-children",
        "list",
        "listitem",
        "scrollable-region-focusable",
    ];

    private const int PhoneWidth = 320;
    private const int PhoneHeight = 800;
    private BrowserHost? _host;

    private BrowserHost Host => _host ?? throw new InvalidOperationException("The host has not started.");

    /// <summary>
    /// As páginas da jornada. O <c>draft-json</c> é o rascunho com JSON escolhido no formulário de flag nova e uma linha
    /// longa no valor: as áreas de texto não quebram linha (<c>wrap="off"</c>) e precisam rolar por dentro, sem empurrar a
    /// página. O <c>flag-editor-json</c> é o editor de uma flag JSON salva; na versão e no simulador, o axe varre com o
    /// bloco do valor dela aberto.
    /// </summary>
    public static TheoryData<string> JourneyPages =>
        ["sign-in", "projects", "draft", "draft-json", "flag-editor", "flag-editor-json", "publish", "history", "version", "simulator"];

    public static TheoryData<string> PagesReachedByClicking => ["draft", "publish", "history", "version", "simulator"];

    public ValueTask InitializeAsync()
    {
        _host = new BrowserHost(api);
        return ValueTask.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(JourneyPages))]
    public async Task A_journey_page_has_no_violation_of_the_pinned_rules(string journeyPage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);

        await ConsoleJourneyPages.PrepareAsync(owner);
        await ConsoleJourneyPages.OpenAsync(page, owner, journeyPage);
        if (journeyPage is "version" or "simulator")
        {
            await ConsoleJourneyPages.OpenTheValueOfShopConfigAsync(page);
        }

        var result = await page.RunAxe(PinnedRules());

        Assert.True(result.Violations.Length == 0, Describe(result.Violations));
        Assert.Empty(browserErrors);
    }

    [Fact]
    public async Task The_loading_screen_before_the_runtime_starts_has_a_main_landmark_and_a_heading()
    {
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        // Sem o script do Blazor, a página fica na tela de carregamento do index.html.
        await page.RouteAsync("**/_framework/blazor.webassembly.js", route => route.AbortAsync());

        await page.GotoAsync("/");
        await Expect(page.GetByText("Loading ToggleForge…")).ToBeVisibleAsync();
        var result = await page.RunAxe(PinnedRules());

        Assert.True(result.Violations.Length == 0, Describe(result.Violations));
    }

    [Theory]
    [MemberData(nameof(PagesReachedByClicking))]
    public async Task Navigating_from_the_projects_moves_focus_to_the_page_heading(string journeyPage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await ConsoleJourneyPages.PrepareAsync(owner);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ClickThroughToAsync(page, owner, journeyPage);

        await Expect(page.Locator("h1")).ToBeFocusedAsync();
    }

    [Theory]
    [MemberData(nameof(JourneyPages))]
    public async Task No_page_scrolls_sideways_on_a_phone(string journeyPage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api, emailPrefix: "studio-owner-with-a-long-address");
        Assert.True(owner.Email.Length > 60, owner.Email);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(PhoneWidth, PhoneHeight);

        await ConsoleJourneyPages.PrepareAsync(owner);
        await ConsoleJourneyPages.OpenAsync(page, owner, journeyPage);
        var widths = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
        var (scrollWidth, clientWidth) = (widths[0], widths[1]);

        Assert.True(scrollWidth <= clientWidth, $"{journeyPage}: scrollWidth {scrollWidth} > clientWidth {clientWidth}");
    }

    /// <summary>
    /// Num celular, a tabela rola por dentro da região dela, e a coluna dos valores mantém a largura do que mostra: a
    /// prévia não transborda da célula, e o bloco aberto fica legível em vez de encolher a zero.
    /// </summary>
    [Theory]
    [InlineData("draft")]
    [InlineData("version")]
    [InlineData("simulator")]
    public async Task The_values_of_a_json_flag_keep_their_width_on_a_phone(string journeyPage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(PhoneWidth, PhoneHeight);
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "shop-config" });

        await ConsoleJourneyPages.PrepareAsync(owner);
        await ConsoleJourneyPages.OpenAsync(page, owner, journeyPage);
        if (journeyPage is not "draft")
        {
            await ConsoleJourneyPages.OpenTheValueOfShopConfigAsync(page);
        }

        var overflowing = await row.EvaluateAsync<string[]>("""
            row => [...row.querySelectorAll('dd, details, pre')]
              .filter(box => box.clientWidth === 0 || box.scrollWidth > box.clientWidth && getComputedStyle(box).overflowX === 'visible')
              .map(box => box.tagName.toLowerCase() + ' ' + box.clientWidth + '/' + box.scrollWidth)
            """);
        Assert.Empty(overflowing);
    }

    [Fact]
    public async Task The_skip_link_is_the_first_stop_and_moves_focus_to_the_main_content_without_navigating()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await ConsoleJourneyPages.PrepareAsync(owner);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);
        await ConsolePages.OpenDraftAsync(page, owner);
        var address = page.Url;
        // Um clique no canto vazio do cabeçalho leva o ponto de partida do Tab ao topo do documento, sem recarregar:
        // a página carregada de novo pediria os arquivos do runtime com o cookie de sessão, cada um conferindo o
        // security stamp no banco, e esgotaria as conexões do Postgres do teste.
        await page.Mouse.ClickAsync(4, 4);

        await page.Keyboard.PressAsync("Tab");
        var skip = page.GetByRole(AriaRole.Link, new() { Name = "Skip to content" });
        await Expect(skip).ToBeFocusedAsync();
        Assert.True((await skip.BoundingBoxAsync())?.Width > 1, "The focused skip link stays visually hidden.");
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("main#main")).ToBeFocusedAsync();
        Assert.Equal(address, page.Url);
        Assert.Empty(browserErrors);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    private static AxeRunOptions PinnedRules() => new() { RunOnly = new RunOnlyOptions { Type = "rule", Values = _pinnedRules.ToList() } };

    private static string Describe(IEnumerable<AxeResultItem> violations) =>
        string.Join(
            Environment.NewLine,
            violations.SelectMany(violation => violation.Nodes.Select(node => $"{violation.Id}: {node.Target} {node.Html}")));

    /// <summary>Chega à página pelos links do console, como quem navega, e não pelo endereço.</summary>
    private static async Task ClickThroughToAsync(IPage page, ConsoleClient owner, string journeyPage)
    {
        if (journeyPage == "draft")
        {
            await page.GotoAsync($"/organizations/{owner.OrganizationId}/projects");
            await ConsolePages.SignInAsync(page, owner.Email, ConsoleClient.Password);
            await page.GetByRole(AriaRole.Link, new() { Name = "Project space-race" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Draft flags" })).ToBeVisibleAsync();
            return;
        }

        await ConsolePages.OpenDraftAsync(page, owner);
        switch (journeyPage)
        {
            case "publish":
                await page.GetByRole(AriaRole.Link, new() { Name = "Review and publish" }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Publish to Development (dev)" })).ToBeVisibleAsync();
                break;
            case "history":
                await page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "History of Development (dev)" })).ToBeVisibleAsync();
                break;
            case "version":
                await page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
                await page.GetByRole(AriaRole.Link, new() { Name = "Version 1" }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Version 1", Exact = true })).ToBeVisibleAsync();
                break;
            case "simulator":
                await page.GetByRole(AriaRole.Link, new() { Name = "Simulate" }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Simulate a player in Development (dev)" })).ToBeVisibleAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(journeyPage), journeyPage, "Unknown journey page.");
        }
    }
}
