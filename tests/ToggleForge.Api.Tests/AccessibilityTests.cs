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
    /// As regras que o console já cumpre, fixadas com zero violação. Ainda não é a varredura WCAG 2.1 AA inteira:
    /// o contraste (o botão âmbar "Check the result", as bordas dos campos) e os erros ligados aos campos chegam
    /// com a fatia de configurações JSON do console e com um PR de acessibilidade seguinte, e só então as tags
    /// <c>wcag2a</c>/<c>wcag2aa</c>/<c>wcag21aa</c> entram aqui no lugar da lista.
    /// </summary>
    private static readonly List<string> _pinnedRules =
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

    public static TheoryData<string> JourneyPages => ["sign-in", "projects", "draft", "publish", "history", "version", "simulator"];

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

        await OpenAsync(page, owner, journeyPage);
        var result = await page.RunAxe(PinnedRules());

        Assert.True(result.Violations.Length == 0, Describe(result.Violations));
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
    public async Task Navigating_to_a_page_moves_focus_to_its_heading(string journeyPage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await PrepareJourneyDataAsync(owner);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ClickThroughToAsync(page, owner, journeyPage);

        await Expect(page.Locator("h1")).ToBeFocusedAsync();
    }

    [Theory]
    [MemberData(nameof(JourneyPages))]
    public async Task No_journey_page_scrolls_sideways_on_a_phone(string journeyPage)
    {
        using var owner = await ConsoleClient.SignUpAsync(api, emailPrefix: "studio-owner-with-a-long-address");
        Assert.True(owner.Email.Length > 60, owner.Email);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(PhoneWidth, PhoneHeight);

        await OpenAsync(page, owner, journeyPage);
        var widths = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
        var (scrollWidth, clientWidth) = (widths[0], widths[1]);

        Assert.True(scrollWidth <= clientWidth, $"{journeyPage}: scrollWidth {scrollWidth} > clientWidth {clientWidth}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    private static AxeRunOptions PinnedRules() => new() { RunOnly = new RunOnlyOptions { Type = "rule", Values = _pinnedRules } };

    private static string Describe(IEnumerable<AxeResultItem> violations) =>
        string.Join(
            Environment.NewLine,
            violations.SelectMany(violation => violation.Nodes.Select(node => $"{violation.Id}: {node.Target} {node.Html}")));

    /// <summary>
    /// Duas publicações em <c>dev</c> (a versão 1 deixa de estar no ar e ganha o formulário de rollback) e uma flag
    /// nova no rascunho, para a página de publicar ter o que revisar.
    /// </summary>
    private static async Task PrepareJourneyDataAsync(ConsoleClient owner)
    {
        await owner.PublishedProjectAsync("space-race");
        var read = await owner.GetFlagAsync("space-race", "new-shop");
        var widened = await owner.UpdateFlagAsync("space-race", "new-shop", read.Header("ETag"), """{"rolloutBasisPoints":5000}""");
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync("space-race", ConsoleClient.Revision(widened), ConsoleClient.NewIdempotencyKey())).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "dark-mode")).Status);
    }

    /// <summary>Abre a página pelo endereço, passando pelo login, e espera o conteúdo dela.</summary>
    private static async Task OpenAsync(IPage page, ConsoleClient owner, string journeyPage)
    {
        if (journeyPage == "sign-in")
        {
            await page.GotoAsync("/sign-in");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in to ToggleForge" })).ToBeVisibleAsync();
            return;
        }

        await PrepareJourneyDataAsync(owner);
        var draft = $"/organizations/{owner.OrganizationId}/projects/space-race/environments/dev";
        var (path, heading) = journeyPage switch
        {
            "projects" => ($"/organizations/{owner.OrganizationId}/projects", "Projects"),
            "draft" => (draft, "Draft flags"),
            "publish" => ($"{draft}/publish", "Changes since version 2"),
            "history" => ($"{draft}/history", "History of Development (dev)"),
            "version" => ($"{draft}/history/1", "Roll back Development to version 1"),
            "simulator" => ($"{draft}/simulator", "Simulate a player in Development (dev)"),
            _ => throw new ArgumentOutOfRangeException(nameof(journeyPage), journeyPage, "Unknown journey page."),
        };
        await page.GotoAsync(path);
        await ConsolePages.SignInAsync(page, owner.Email, ConsoleClient.Password);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading })).ToBeVisibleAsync();
        if (journeyPage == "simulator")
        {
            await page.GetByLabel("Player ID").FillAsync("player-42");
            await page.GetByRole(AriaRole.Button, new() { Name = "Simulate" }).ClickAsync();
            await Expect(page.Locator("#simulation-results")).ToBeVisibleAsync();
        }
    }

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
