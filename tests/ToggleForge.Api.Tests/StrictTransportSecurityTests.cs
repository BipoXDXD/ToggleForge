using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// HSTS: depois da primeira resposta em HTTPS, o navegador não fala mais HTTP com a API, nem aceita certificado
/// inválido. Fora do desenvolvimento só: o navegador guarda a política, e localhost a herdaria.
/// </summary>
public sealed class StrictTransportSecurityTests(ApiFactory api)
{
    private static readonly Uri _publicAddress = new("https://console.toggleforge.test");

    [Fact]
    public async Task Outside_development_https_responses_pin_https_for_a_year()
    {
        await using var production = api.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var visitor = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _publicAddress });

        using var response = await visitor.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);

        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task In_development_no_response_pins_https()
    {
        using var visitor = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _publicAddress });

        using var response = await visitor.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
