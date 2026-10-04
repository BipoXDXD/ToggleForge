using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ToggleForge.Api.Tests.Support;
using ToggleForge.Application.Access;

namespace ToggleForge.Api.Tests;

/// <summary>
/// HSTS: depois da primeira resposta em HTTPS, o navegador não fala mais HTTP com a API, nem aceita certificado
/// inválido. Fora do desenvolvimento só: o navegador guarda a política, e localhost a herdaria.
/// </summary>
public sealed class StrictTransportSecurityTests(ApiFactory api)
{
    private const string Ingress = "10.0.0.5";
    private const string Outsider = "198.51.100.9";
    private const string IngressNetwork = "10.0.0.0/8";

    private static readonly Uri _publicAddress = new("https://console.toggleforge.test");

    /// <summary>O primeiro deploy pina por um dia só, para poder voltar atrás (ADR 0012); o appsettings.json traz esse valor.</summary>
    [Fact]
    public async Task Outside_development_https_responses_pin_https_for_the_configured_day()
    {
        await using var production = Production();
        using var visitor = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _publicAddress });

        using var response = await visitor.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);

        Assert.Equal("max-age=86400", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task The_pin_lasts_as_long_as_configured()
    {
        await using var production = Production(builder => builder.UseSetting("StrictTransportSecurity:MaxAge", "365.00:00:00"));
        using var visitor = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _publicAddress });

        using var response = await visitor.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);

        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-1.00:00:00")]
    [InlineData("731.00:00:00")]
    public async Task A_pin_duration_out_of_range_keeps_the_api_from_starting(string maxAge)
    {
        await using var production = Production(builder => builder.UseSetting("StrictTransportSecurity:MaxAge", maxAge));

        var error = Assert.Throws<OptionsValidationException>(() => production.CreateClient());

        Assert.Contains("MaxAge", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_no_response_pins_https()
    {
        using var visitor = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _publicAddress });

        using var response = await visitor.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>No Container Apps o TLS termina no ingress: a API vê HTTP, e só o esquema repassado diz que era HTTPS.</summary>
    [Fact]
    public async Task Behind_the_ingress_that_ended_tls_the_response_pins_https()
    {
        using var response = await ForwardedHttpsAsync(sender: Ingress);

        Assert.Equal("max-age=86400", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    /// <summary>Quem não é o ingress não diz o esquema: a requisição continua sendo HTTP, e HTTP não leva HSTS.</summary>
    [Fact]
    public async Task An_untrusted_sender_claiming_https_gets_no_pin()
    {
        using var response = await ForwardedHttpsAsync(sender: Outsider);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>O exception handler limpa os headers antes do 500; o HSTS sai mesmo assim.</summary>
    [Fact]
    public async Task An_unexpected_error_still_pins_https()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        await using var broken = Production(builder => builder.ConfigureTestServices(
            services => services.AddScoped<IMembershipDirectory, BrokenMembershipDirectory>()));
        using var client = broken.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _publicAddress, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.Session);
        request.Headers.Add("Cookie", $"{ConsoleClient.SessionCookie}={member.SessionCookieValue()}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("max-age=86400", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    private async Task<HttpResponseMessage> ForwardedHttpsAsync(string sender)
    {
        await using var production = Production(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<IStartupFilter, ClientAddresses>()));
        using var http = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://console.toggleforge.test") });
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.AntiforgeryToken);
        request.Headers.Add(ClientAddresses.Header, sender);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Proto", "https");
        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Program> Production(Action<IWebHostBuilder>? configure = null) =>
        api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ForwardedHeaders:KnownNetworks:0", IngressNetwork);
            configure?.Invoke(builder);
        });
}
