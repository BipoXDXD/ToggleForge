using Microsoft.Playwright;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// A régua das provas de contraste e de foco do console, medida em páginas mínimas com valores calculados à mão: uma
/// sonda que erra para cima aprova o que a WCAG reprova.
/// </summary>
public sealed class ContrastProbeTests(Browser browser) : IClassFixture<Browser>
{
    // A página fica em about:blank; o endereço base só satisfaz o contexto.
    private static readonly Uri _nowhere = new("https://localhost/");

    // #767676 sobre #fff: L = 0,1812 e 1,0, então (1,05) / (0,2312) = 4,54.
    [Fact]
    public async Task An_opaque_text_is_measured_against_the_opaque_background()
    {
        await using var context = await browser.NewContextAsync(_nowhere);
        var page = await OpenAsync(context, """<body style="margin:0;background:#fff"><p id="text" style="color:#767676">Text</p></body>""");

        var ratio = await ContrastProbe.TextRatioAsync(page.Locator("#text"));

        Assert.Equal(4.54, ratio, 2);
    }

    // O fundo 60% de rgba(238,242,255) sobre o branco dá rgb(244,8; 247,2; 255), L = 0,9324; o texto #646464 tem
    // L = 0,1274, então (0,9824) / (0,1774) = 5,54. Com o alfa aplicado duas vezes, a sonda dava 11,48.
    [Fact]
    public async Task A_translucent_background_is_composed_over_the_one_behind_it()
    {
        await using var context = await browser.NewContextAsync(_nowhere);
        var page = await OpenAsync(context, """
            <body style="margin:0;background:#fff">
              <div style="background:rgba(238,242,255,0.6)"><p id="text" style="color:#646464">Text</p></div>
            </body>
            """);

        var ratio = await ContrastProbe.TextRatioAsync(page.Locator("#text"));

        Assert.Equal(5.54, ratio, 2);
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string html)
    {
        var page = await context.NewPageAsync();
        await page.SetContentAsync(html);
        return page;
    }
}
