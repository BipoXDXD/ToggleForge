using System.Net;
using Microsoft.Playwright;
using ToggleForge.Api.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O tracer bullet do Marco 3 num navegador de verdade: o console carregado da API, falando com ela pela mesma
/// origem, com o cookie de sessão e o token antiforgery reais (ADR 0009).
/// </summary>
public sealed class ConsoleJourneyTests(ApiFactory api, Browser browser) : IClassFixture<Browser>, IAsyncLifetime
{
    private BrowserHost? _host;

    private BrowserHost Host => _host ?? throw new InvalidOperationException("The host has not started.");

    public ValueTask InitializeAsync()
    {
        _host = new BrowserHost(api);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_member_signs_in_creates_a_project_and_adds_a_flag_to_its_draft()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = CollectErrors(page);

        await page.GotoAsync("/");
        await SignInAsync(page, owner.Email, ConsoleClient.Password);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Projects" })).ToBeVisibleAsync();
        await page.GetByLabel("Name").FillAsync("Space Race");
        await page.GetByLabel("Key").FillAsync("space-race");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Space Race" })).ToBeVisibleAsync();
        await page.GetByLabel("Key").FillAsync("new-shop");
        await page.GetByLabel("Rollout (%)").FillAsync("25");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" }).ClickAsync();

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "new-shop" });
        await Expect(row).ToContainTextAsync("Enabled");
        await Expect(row).ToContainTextAsync("25%");
        var draft = await owner.GetDraftAsync("space-race");
        var flag = Assert.Single(draft.Json["flags"]!.AsArray())!;
        Assert.Equal("new-shop", flag["key"]!.GetValue<string>());
        Assert.Equal(2_500, flag["rolloutBasisPoints"]!.GetValue<int>());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    [Fact]
    public async Task A_visitor_without_session_signs_in_and_returns_to_the_page_asked_for()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"/organizations/{owner.OrganizationId}/projects/space-race/environments/prod");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/sign-in\\?returnUrl="));
        await SignInAsync(page, owner.Email, ConsoleClient.Password);

        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Production" })).ToHaveAttributeAsync("aria-selected", "true");
        Assert.EndsWith($"/organizations/{owner.OrganizationId}/projects/space-race/environments/prod", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_keeps_the_visitor_on_the_sign_in_page()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/sign-in");
        await SignInAsync(page, owner.Email, "not the password");

        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("The email or the password is wrong.");
        Assert.EndsWith("/sign-in", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_changes_the_rollout_of_a_flag_in_the_draft()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = CollectErrors(page);

        await OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Link, new() { Name = "new-shop" }).ClickAsync();
        await Expect(page.GetByLabel("Value for included players")).ToHaveValueAsync("true");
        await Expect(page.GetByLabel("Value for everyone else")).ToHaveValueAsync("false");
        await page.GetByLabel("Rollout (%)").FillAsync("50");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Saved to the draft");
        await page.GetByRole(AriaRole.Link, new() { Name = "space-race · dev" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "new-shop" })).ToContainTextAsync("50%");
        Assert.Equal(5_000, (await owner.GetFlagAsync("space-race", "new-shop")).Json["rolloutBasisPoints"]!.GetValue<int>());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    [Fact]
    public async Task A_change_made_by_someone_else_meanwhile_is_not_overwritten()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        await OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Link, new() { Name = "new-shop" }).ClickAsync();
        await Expect(page.GetByLabel("Rollout (%)")).ToHaveValueAsync("10");

        var read = await owner.GetFlagAsync("space-race", "new-shop");
        var meanwhile = await owner.UpdateFlagAsync("space-race", "new-shop", read.Header("ETag"), """{"rolloutBasisPoints":3000}""");
        await page.GetByLabel("Rollout (%)").FillAsync("50");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        Assert.Equal(HttpStatusCode.OK, meanwhile.Status);
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Someone changed this flag after you opened it.");
        await Expect(page.GetByLabel("Rollout (%)")).ToHaveValueAsync("30");
        Assert.Equal(3_000, (await owner.GetFlagAsync("space-race", "new-shop")).Json["rolloutBasisPoints"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_member_removes_a_flag_from_the_draft_after_confirming()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Link, new() { Name = "new-shop" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove from draft" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();

        await Expect(page.GetByText("No flags in this draft yet.")).ToBeVisibleAsync();
        Assert.Empty((await owner.GetDraftAsync("space-race")).Json["flags"]!.AsArray());
    }

    [Fact]
    public async Task A_member_reviews_the_changes_and_publishes_after_typing_the_environment_key()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "new-shop", rolloutBasisPoints: 2_500)).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = CollectErrors(page);

        await OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Link, new() { Name = "Review and publish" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Changes" })).ToContainTextAsync("new-shop");
        var publish = page.GetByRole(AriaRole.Button, new() { Name = "Publish 1 change to dev" });
        await Expect(publish).ToBeDisabledAsync();
        await page.GetByLabel("Summary (optional)").FillAsync("Open the new shop to 25%");
        await page.GetByLabel("Type dev to confirm").FillAsync("dev");
        await publish.ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 1 is live in Development.");
        await page.GetByRole(AriaRole.Link, new() { Name = "Back to the draft" }).ClickAsync();
        await Expect(page.GetByTestId("draft-status")).ToContainTextAsync("Version 1 is live. No unpublished changes.");
        var publication = (await owner.ListPublicationsAsync("space-race")).Json["data"]!.AsArray().Single()!;
        Assert.Equal(1, publication["version"]!.GetValue<long>());
        Assert.Equal("Open the new shop to 25%", publication["summary"]!.GetValue<string>());
        Assert.Equal(owner.Email, publication["publishedBy"]!["email"]!.GetValue<string>());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>Abre o rascunho de <c>dev</c> pelo endereço: o console pede o login e volta para ele.</summary>
    private static async Task OpenDraftAsync(IPage page, ConsoleClient member)
    {
        await page.GotoAsync($"/organizations/{member.OrganizationId}/projects/space-race/environments/dev");
        await SignInAsync(page, member.Email, ConsoleClient.Password);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Draft flags" })).ToBeVisibleAsync();
    }

    private static async Task SignInAsync(IPage page, string email, string password)
    {
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    /// <summary>
    /// Erros de script, violações da CSP e arquivos do console que não carregaram aparecem só no console do
    /// navegador. O status das chamadas à API fica de fora: o 401 de quem ainda não entrou faz parte do fluxo, e
    /// a interface é que mostra se a resposta foi tratada.
    /// </summary>
    private static List<string> CollectErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            var apiCallStatus = message.Text.StartsWith("Failed to load resource", StringComparison.Ordinal)
                && new Uri(message.Location).AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);
            if (message.Type == "error" && !apiCallStatus)
            {
                errors.Add($"{message.Text} at {message.Location}");
            }
        };
        page.PageError += (_, error) => errors.Add(error);
        return errors;
    }
}
