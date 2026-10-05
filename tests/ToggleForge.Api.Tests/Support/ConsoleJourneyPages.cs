using System.Net;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Os dados e as páginas que os testes de acessibilidade e de contraste do console percorrem: o mesmo projeto, aberto
/// do mesmo jeito, para o axe e a sonda de contraste medirem as mesmas telas.
/// </summary>
internal static class ConsoleJourneyPages
{
    private const string LongJsonLine = "{\"featuredItems\": [\"sword\", \"shield\", \"bow\", \"arrows\", \"potion\", \"map\"]}";
    // Indentado, passa da altura máxima do bloco (max-h-80) e rola por dentro: o axe confere que o teclado chega nele.
    private const string ShopConfig = """
        {"discount":0.2,"featuredItems":["sword","shield","bow","arrows","potion","map","rope","torch","compass",
        "lantern","key","scroll","gem","ring","amulet","boots","cloak","helmet"]}
        """;

    /// <summary>
    /// Duas publicações em <c>dev</c> (a versão 1 deixa de estar no ar e ganha o formulário de rollback), as duas com
    /// uma flag booleana e uma JSON cujo valor não cabe na prévia, e uma flag nova no rascunho, para a página de
    /// publicar ter o que revisar.
    /// </summary>
    public static async Task PrepareAsync(ConsoleClient owner)
    {
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "new-shop")).Status);
        var json = await owner.CreateJsonFlagAsync("space-race", "shop-config", ShopConfig, """{"discount":0}""");
        Assert.Equal(HttpStatusCode.Created, json.Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync("space-race", ConsoleClient.Revision(json), ConsoleClient.NewIdempotencyKey())).Status);
        var read = await owner.GetFlagAsync("space-race", "new-shop");
        var widened = await owner.UpdateFlagAsync("space-race", "new-shop", read.Header("ETag"), """{"rolloutBasisPoints":5000}""");
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync("space-race", ConsoleClient.Revision(widened), ConsoleClient.NewIdempotencyKey())).Status);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateFlagAsync("space-race", "dark-mode")).Status);
    }

    /// <summary>Abre a página pelo endereço (o editor de flag, pelo link do rascunho), passando pelo login, e espera o conteúdo dela.</summary>
    public static async Task OpenAsync(IPage page, ConsoleClient owner, string journeyPage)
    {
        if (journeyPage == "sign-in")
        {
            await page.GotoAsync("/sign-in");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in to ToggleForge" })).ToBeVisibleAsync();
            return;
        }

        var draft = $"/organizations/{owner.OrganizationId}/projects/space-race/environments/dev";
        var (path, heading) = journeyPage switch
        {
            "projects" => ($"/organizations/{owner.OrganizationId}/projects", "Projects"),
            "draft" or "draft-json" or "flag-editor" or "flag-editor-json" => (draft, "Draft flags"),
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

        if (journeyPage == "draft-json")
        {
            await page.GetByRole(AriaRole.Radio, new() { Name = "JSON" }).CheckAsync();
            await page.Locator("#on-value").FillAsync(LongJsonLine);
            await Expect(page.Locator("#on-value-size")).ToContainTextAsync("bytes");
        }

        if (journeyPage == "flag-editor")
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "new-shop" }).ClickAsync();
            // O h1 aparece antes de a flag carregar: esperar o formulário evita varrer a tela de carregamento.
            await Expect(page.GetByLabel("Rollout (%)")).ToBeVisibleAsync();
        }

        if (journeyPage == "flag-editor-json")
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "shop-config" }).ClickAsync();
            await Expect(page.Locator("#on-value")).ToBeVisibleAsync();
        }
    }

    /// <summary>Abre o bloco do primeiro valor da flag JSON, que começa recolhido: o axe só vê o que está na tela.</summary>
    public static async Task OpenTheValueOfShopConfigAsync(IPage page)
    {
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "shop-config" });
        await row.Locator("summary").First.ClickAsync();
        await Expect(row.GetByRole(AriaRole.Group, new() { Name = "Value, indented" }).First).ToBeVisibleAsync();
    }
}
