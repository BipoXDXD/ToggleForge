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

    // A mesma conta do texto opaco: #767676 sobre #fff dá 4,54.
    [Fact]
    public async Task A_field_border_is_measured_against_the_background_outside_it()
    {
        await using var context = await browser.NewContextAsync(_nowhere);
        var page = await OpenAsync(context, """<body style="margin:0;background:#fff"><input id="field" style="border:1px solid #767676"></body>""");

        var ratio = await ContrastProbe.BorderRatioAsync(page.Locator("#field"));

        Assert.Equal(4.54, ratio, 2);
    }

    // Sem borda desenhada, a cor computada ainda existe (o currentColor), mas não aparece: a razão é a de cor nenhuma.
    [Theory]
    [InlineData("border:none")]
    [InlineData("border:0 solid #000")]
    [InlineData("border:1px hidden #000")]
    public async Task A_field_without_a_drawn_border_has_no_border_contrast(string border)
    {
        await using var context = await browser.NewContextAsync(_nowhere);
        var page = await OpenAsync(context, $"""<body style="margin:0;background:#fff"><input id="field" style="{border}"></body>""");

        var ratio = await ContrastProbe.BorderRatioAsync(page.Locator("#field"));

        Assert.Equal(1.0, ratio, 2);
    }

    [Fact]
    public async Task The_tab_walk_stops_at_every_field_and_ends_when_the_focus_leaves_the_page()
    {
        await using var context = await browser.NewContextAsync(_nowhere);
        var page = await OpenAsync(context, """<body><input id="first"><input id="second"></body>""");

        var stops = await ContrastProbe.TabStopsAsync(page);

        Assert.Equal(["input#first", "input#second"], stops.Select(stop => stop.Element));
    }

    // Um campo que segura o Tab deixaria o resto da página sem medir; a sonda acusa em vez de aprovar o que viu.
    [Fact]
    public async Task A_field_that_keeps_the_tab_fails_the_walk()
    {
        await using var context = await browser.NewContextAsync(_nowhere);
        var page = await OpenAsync(context, """
            <body>
              <input id="first"><input id="trap"><input id="never-reached">
              <script>
                document.getElementById('trap').addEventListener('keydown', e => { if (e.key === 'Tab') e.preventDefault(); });
              </script>
            </body>
            """);

        var walk = await Assert.ThrowsAsync<InvalidOperationException>(() => ContrastProbe.TabStopsAsync(page));

        Assert.Contains("input#trap", walk.Message, StringComparison.Ordinal);
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string html)
    {
        var page = await context.NewPageAsync();
        await page.SetContentAsync(html);
        return page;
    }
}
