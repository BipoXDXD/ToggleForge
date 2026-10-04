using Microsoft.Playwright;

namespace ToggleForge.Api.Tests.Support;

/// <summary>Um Chromium headless por classe de teste; cada teste abre o próprio contexto, sem cookies de outro.</summary>
public sealed class Browser : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _chromium;

    public async ValueTask InitializeAsync()
    {
        // Baixa o Chromium da versão do pacote na primeira vez; depois, só confere que ele está lá.
        var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installed != 0)
        {
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {installed}).");
        }

        _playwright = await Playwright.CreateAsync();
        _chromium = await _playwright.Chromium.LaunchAsync();
    }

    public Task<IBrowserContext> NewContextAsync(Uri baseAddress) =>
        (_chromium ?? throw new InvalidOperationException("The browser has not started."))
            .NewContextAsync(new BrowserNewContextOptions { BaseURL = baseAddress.ToString(), IgnoreHTTPSErrors = true });

    public async ValueTask DisposeAsync()
    {
        if (_chromium is not null)
        {
            await _chromium.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
