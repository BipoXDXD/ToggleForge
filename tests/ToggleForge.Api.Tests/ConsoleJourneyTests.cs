using System.Net;
using System.Text.RegularExpressions;
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
        var browserErrors = ConsolePages.CollectErrors(page);

        await page.GotoAsync("/");
        await ConsolePages.SignInAsync(page, owner.Email, ConsoleClient.Password);
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

    /// <summary>
    /// A evidência do Marco 3 (ToggleForge.md, seção 9): a experiência central inteira pela interface. Só a conta
    /// nasce pela API, porque o console não tem cadastro.
    /// </summary>
    [Fact]
    public async Task A_member_creates_simulates_publishes_widens_and_rolls_back_a_flag_from_the_console()
    {
        const string playerId = "player-42";
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);

        await page.GotoAsync("/");
        await ConsolePages.SignInAsync(page, owner.Email, ConsoleClient.Password);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Projects" })).ToBeVisibleAsync();
        await page.GetByLabel("Name").FillAsync("Space Race");
        await page.GetByLabel("Key").FillAsync("space-race");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Space Race" })).ToBeVisibleAsync();
        await page.GetByLabel("Key").FillAsync("new-shop");
        await page.GetByLabel("Rollout (%)").FillAsync("10");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "new-shop" })).ToContainTextAsync("10%");

        await SimulateAsync(page, playerId, publishedVersion: false);
        await Expect(ResultCells(page).Nth(2)).ToHaveTextAsync(new Regex("^Bucket \\d+ is (inside|outside) the rollout\\.$"));
        var atTenPercent = await ResultCells(page).AllInnerTextsAsync();
        await ReturnToDraftAsync(page);
        await PublishAsync(page, "Open the new shop to 10%");
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 1 is live in Development.");

        await page.GetByRole(AriaRole.Link, new() { Name = "Back to the draft" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "new-shop" }).ClickAsync();
        await page.GetByLabel("Rollout (%)").FillAsync("100");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Saved to the draft");
        await ReturnToDraftAsync(page);
        await PublishAsync(page, "Open the new shop to everyone");
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 2 is live in Development.");
        await page.GetByRole(AriaRole.Link, new() { Name = "Back to the draft" }).ClickAsync();
        await SimulateAsync(page, playerId, publishedVersion: true);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Version 2", Exact = true })).ToBeVisibleAsync();
        await Expect(ResultCells(page)).ToHaveTextAsync(["new-shop boolean", "true", "The rollout includes everyone."]);

        await ReturnToDraftAsync(page);
        await page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Version 1" }).ClickAsync();
        await page.GetByLabel("Type dev to confirm").FillAsync("dev");
        await page.GetByRole(AriaRole.Button, new() { Name = "Roll back to version 1" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 3 is live in Development with the content of version 1.");
        await ReturnToDraftAsync(page);
        await SimulateAsync(page, playerId, publishedVersion: true);

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Version 3", Exact = true })).ToBeVisibleAsync();
        await Expect(ResultCells(page)).ToHaveTextAsync(atTenPercent.ToArray());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    /// <summary>A jornada da ADR 0011 pelo console: uma configuração JSON criada, publicada, simulada, alterada e revertida.</summary>
    [Fact]
    public async Task A_member_creates_publishes_simulates_and_rolls_back_a_json_flag_from_the_console()
    {
        const string playerId = "player-42";
        const string indentedAtTwentyPercent = "{\n  \"discount\": 0.2,\n  \"featuredItems\": [\n    \"sword\"\n  ]\n}";
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.GetByLabel("Key").FillAsync("shop-config");
        await page.GetByRole(AriaRole.Radio, new() { Name = "JSON" }).CheckAsync();
        await Expect(page.Locator("#json-public")).ToContainTextAsync("Every player who has the game can read these values.");
        await page.GetByLabel("Rollout (%)").FillAsync("100");
        await page.GetByLabel("Value for included players").FillAsync("{\"discount\": 0.2, \"featuredItems\": [\"sword\"]}");
        await page.GetByLabel("Value for everyone else").FillAsync("{\"discount\": 0}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" }).ClickAsync();

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "shop-config" });
        await Expect(row.GetByTestId("flag-type")).ToHaveTextAsync("JSON");
        await Expect(row).ToContainTextAsync("Included");
        await Expect(row.Locator("code").First).ToHaveAttributeAsync("title", "{\"discount\":0.2,\"featuredItems\":[\"sword\"]}");
        await PublishAsync(page, "Shop config at 20% off");
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 1 is live in Development.");

        await page.GetByRole(AriaRole.Link, new() { Name = "Back to the draft" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "shop-config" }).ClickAsync();
        await Expect(page.GetByLabel("Value for included players")).ToHaveValueAsync(indentedAtTwentyPercent);
        await page.GetByLabel("Value for included players").FillAsync("{\"discount\": 0.5, \"featuredItems\": [\"sword\"]}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // O editor JSON tem outra região de status, vazia, que anuncia a cópia de valor.
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved to the draft" })).ToBeVisibleAsync();
        await ReturnToDraftAsync(page);
        await PublishAsync(page, "Shop config at 50% off");
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 2 is live in Development.");

        await page.GetByRole(AriaRole.Link, new() { Name = "Back to the draft" }).ClickAsync();
        await SimulateAsync(page, playerId, publishedVersion: true);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Version 2", Exact = true })).ToBeVisibleAsync();
        await ExpectSimulatedValueAsync(page, "\"discount\": 0.5");

        await ReturnToDraftAsync(page);
        await page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Version 1" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "shop-config" }).GetByTestId("flag-type")).ToHaveTextAsync("JSON");
        await page.GetByLabel("Type dev to confirm").FillAsync("dev");
        await page.GetByRole(AriaRole.Button, new() { Name = "Roll back to version 1" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 3 is live in Development with the content of version 1.");
        await ReturnToDraftAsync(page);
        await SimulateAsync(page, playerId, publishedVersion: true);

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Version 3", Exact = true })).ToBeVisibleAsync();
        await ExpectSimulatedValueAsync(page, "\"discount\": 0.2");
        Assert.Equal(
            "{\"discount\":0.2,\"featuredItems\":[\"sword\"]}",
            (await owner.GetFlagAsync("space-race", "shop-config")).Json["onValue"]!.ToJsonString());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    /// <summary>O stub não prova isto: o console reconhece o <c>type</c> exato do 409 que a API real devolve.</summary>
    [Fact]
    public async Task A_member_creating_a_json_flag_in_a_full_draft_is_told_the_draft_is_out_of_room()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await owner.FillJsonBudgetAsync("space-race", 131_071);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ConsolePages.OpenDraftAsync(page, owner);
        await AddJsonFlagAsync(page, "shop-config", "1", "0");

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("This draft is out of room for JSON values.");
        await Expect(page.Locator("#flag-key")).ToHaveValueAsync("shop-config");
        var keys = (await owner.GetDraftAsync("space-race")).Json["flags"]!.AsArray().Select(flag => flag!["key"]!.GetValue<string>());
        Assert.DoesNotContain("shop-config", keys);
    }

    /// <summary>O console aceita a sintaxe e deixa a API decidir a profundidade; o erro dela volta ao campo, com o foco.</summary>
    [Fact]
    public async Task A_json_value_nested_past_eight_levels_is_refused_under_its_field()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ConsolePages.OpenDraftAsync(page, owner);
        await AddJsonFlagAsync(page, "shop-config", "1", "[[[[[[[[[1]]]]]]]]]");

        await Expect(page.Locator("#off-value-error")).ToContainTextAsync("The value nests deeper than 8 levels.");
        await Expect(page.Locator("#off-value")).ToBeFocusedAsync();
        await Expect(page.Locator("#off-value")).ToHaveAttributeAsync("aria-invalid", "true");
    }

    /// <summary>
    /// Uma chave publicada guarda o tipo no ambiente (ADR 0011): apagada do rascunho, ela não volta com o outro tipo,
    /// e o console diz isso na chave, que é o que o membro tem de trocar.
    /// </summary>
    [Fact]
    public async Task A_key_published_as_boolean_cannot_come_back_as_json()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var read = await owner.GetFlagAsync("space-race", "new-shop");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFlagAsync("space-race", "new-shop", read.Header("ETag"))).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);

        await ConsolePages.OpenDraftAsync(page, owner);
        await AddJsonFlagAsync(page, "new-shop", "1", "0");

        await Expect(page.Locator("#flag-key-error")).ToContainTextAsync(
            "This key was published as a Boolean flag in this environment, and a published key keeps its type. Use a new key for a JSON flag.");
        await Expect(page.Locator("#flag-key")).ToBeFocusedAsync();
        await Expect(page.Locator("#flag-key")).ToHaveAttributeAsync("aria-invalid", "true");
        Assert.Empty((await owner.GetDraftAsync("space-race")).Json["flags"]!.AsArray());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    /// <summary>
    /// Durante o envio o formulário fica desabilitado e o navegador manda o foco para o <c>body</c>; o bUnit não vê essa
    /// perda. Depois do sucesso, o foco volta à chave vazia, pronta para a próxima flag (WCAG 2.4.3).
    /// </summary>
    [Fact]
    public async Task After_adding_a_flag_with_enter_the_focus_is_back_on_the_empty_key()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.Locator("#flag-key").FillAsync("new-shop");
        await page.Locator("#flag-key").PressAsync("Enter");

        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = "new-shop" })).ToBeVisibleAsync();
        await Expect(page.Locator("#flag-key")).ToBeFocusedAsync();
        await Expect(page.Locator("#flag-key")).ToHaveValueAsync(string.Empty);
    }

    /// <summary>Uma recusa sem campo (a chave já está no rascunho) devolve o foco ao botão, onde o membro estava.</summary>
    [Fact]
    public async Task After_a_refusal_without_a_field_the_focus_is_back_on_the_add_button()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "new-shop")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.Locator("#flag-key").FillAsync("new-shop");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" })).ToBeFocusedAsync();
    }

    /// <summary>Uma recusa da API que aponta um campo (a chave fora do padrão) leva o foco a esse campo.</summary>
    [Fact]
    public async Task After_the_api_refuses_the_key_the_focus_is_on_the_key()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.Locator("#flag-key").FillAsync("Shop Config");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" }).ClickAsync();

        await Expect(page.Locator("#flag-key-error")).ToBeVisibleAsync();
        await Expect(page.Locator("#flag-key")).ToBeFocusedAsync();
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
        await ConsolePages.SignInAsync(page, owner.Email, ConsoleClient.Password);

        await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Environments" }).GetByRole(AriaRole.Link, new() { Name = "Production" }))
            .ToHaveAttributeAsync("aria-current", "page");
        Assert.EndsWith($"/organizations/{owner.OrganizationId}/projects/space-race/environments/prod", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_keeps_the_visitor_on_the_sign_in_page()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/sign-in");
        await ConsolePages.SignInAsync(page, owner.Email, "not the password");

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
        var browserErrors = ConsolePages.CollectErrors(page);

        await ConsolePages.OpenDraftAsync(page, owner);
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
        await ConsolePages.OpenDraftAsync(page, owner);
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

        await ConsolePages.OpenDraftAsync(page, owner);
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
        var browserErrors = ConsolePages.CollectErrors(page);

        await ConsolePages.OpenDraftAsync(page, owner);
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

    [Fact]
    public async Task A_member_rolls_back_to_an_earlier_version_from_the_history()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var read = await owner.GetFlagAsync("space-race", "new-shop");
        var widened = await owner.UpdateFlagAsync("space-race", "new-shop", read.Header("ETag"), """{"rolloutBasisPoints":5000}""");
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync("space-race", ConsoleClient.Revision(widened), ConsoleClient.NewIdempotencyKey())).Status);
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Link, new() { Name = "History" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Publications" }).GetByRole(AriaRole.Listitem).First).ToContainTextAsync("Live");
        await page.GetByRole(AriaRole.Link, new() { Name = "Version 1" }).ClickAsync();
        await page.GetByLabel("Type dev to confirm").FillAsync("dev");
        await page.GetByRole(AriaRole.Button, new() { Name = "Roll back to version 1" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Version 3 is live in Development with the content of version 1.");
        Assert.Equal(3, (await owner.GetEnvironmentAsync("space-race", "dev")).Json["currentVersion"]!.GetValue<long>());
        Assert.Equal(1_000, (await owner.GetFlagAsync("space-race", "new-shop")).Json["rolloutBasisPoints"]!.GetValue<int>());
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    [Fact]
    public async Task A_member_simulates_a_player_on_the_draft_and_on_the_live_version()
    {
        const string playerId = "player-CANARY-6f0d2c1e";
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var read = await owner.GetFlagAsync("space-race", "new-shop");
        Assert.Equal(HttpStatusCode.OK, (await owner.UpdateFlagAsync("space-race", "new-shop", read.Header("ETag"), """{"rolloutBasisPoints":10000}""")).Status);
        var live = (await owner.SimulatePublicationAsync("space-race", playerId, version: 1)).Json["results"]![0]!;
        var (bucket, included) = (live["bucket"]!.GetValue<int>(), live["value"]!.GetValue<bool>());
        await using var context = await browser.NewContextAsync(Host.Address);
        var page = await context.NewPageAsync();
        var browserErrors = ConsolePages.CollectErrors(page);
        var browserMessages = new List<string>();
        page.Console += (_, message) => browserMessages.Add(message.Text);

        await ConsolePages.OpenDraftAsync(page, owner);
        await page.GetByRole(AriaRole.Link, new() { Name = "Simulate" }).ClickAsync();
        await page.GetByLabel("Player ID").FillAsync(playerId);
        await page.GetByRole(AriaRole.Button, new() { Name = "Simulate" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Draft", Exact = true })).ToBeVisibleAsync();
        await Expect(ResultCells(page)).ToHaveTextAsync(["new-shop boolean", "true", "The rollout includes everyone."]);
        await page.GetByLabel("Published version").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simulate" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Version 1", Exact = true })).ToBeVisibleAsync();
        await Expect(ResultCells(page)).ToHaveTextAsync(
            included ? ["new-shop boolean", "true", $"Bucket {bucket} is inside the rollout."] : ["new-shop boolean", "false", $"Bucket {bucket} is outside the rollout."]);
        Assert.DoesNotContain("CANARY", page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(browserMessages, message => message.Contains("CANARY", StringComparison.Ordinal));
        Assert.DoesNotContain("CANARY", await page.EvaluateAsync<string>("JSON.stringify({ ...localStorage, ...sessionStorage })"), StringComparison.Ordinal);
        Assert.True(browserErrors.Count == 0, string.Join(" | ", browserErrors));
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>Do rascunho ao simulador; <paramref name="publishedVersion"/> simula a versão no ar, que vem preenchida.</summary>
    private static async Task SimulateAsync(IPage page, string playerId, bool publishedVersion)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Simulate" }).ClickAsync();
        await page.GetByLabel("Player ID").FillAsync(playerId);
        if (publishedVersion)
        {
            await page.GetByLabel("Published version").CheckAsync();
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Simulate" }).ClickAsync();
        await Expect(page.Locator("#simulation-results")).ToBeVisibleAsync();
    }

    /// <summary>Da página de revisão até o pedido de publicação, com a chave de <c>dev</c> digitada.</summary>
    private static async Task PublishAsync(IPage page, string summary)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Review and publish" }).ClickAsync();
        await page.GetByLabel("Summary (optional)").FillAsync(summary);
        await page.GetByLabel("Type dev to confirm").FillAsync("dev");
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish 1 change to dev" }).ClickAsync();
    }

    /// <summary>Preenche o formulário "New flag" como JSON, com o rollout padrão, e envia.</summary>
    private static async Task AddJsonFlagAsync(IPage page, string key, string onValue, string offValue)
    {
        await page.Locator("#flag-key").FillAsync(key);
        await page.GetByRole(AriaRole.Radio, new() { Name = "JSON" }).CheckAsync();
        await page.Locator("#on-value").FillAsync(onValue);
        await page.Locator("#off-value").FillAsync(offValue);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to draft" }).ClickAsync();
    }

    /// <summary>O valor JSON do simulador começa recolhido; aberto, mostra o texto indentado.</summary>
    private static async Task ExpectSimulatedValueAsync(IPage page, string expected)
    {
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "shop-config" });
        await Expect(row.Locator("pre")).ToBeHiddenAsync();
        await row.Locator("summary").ClickAsync();
        await Expect(row.Locator("pre")).ToContainTextAsync(expected);
    }

    private static async Task ReturnToDraftAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "space-race · dev" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Draft flags" })).ToBeVisibleAsync();
    }

    private static ILocator ResultCells(IPage page) =>
        page.GetByRole(AriaRole.Row).Filter(new() { HasText = "new-shop" }).GetByRole(AriaRole.Cell);
}
