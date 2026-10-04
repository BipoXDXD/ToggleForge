using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Só um proxy das redes declaradas diz o IP e o esquema do cliente. O teto anônimo de 1 por minuto mostra
/// qual IP a API enxergou: quem pode escolher o próprio IP escapa do rate limit (ADR 0006).
/// </summary>
public sealed class ForwardedHeadersTests(ApiFactory api)
{
    private const string Ingress = "10.0.0.5";
    private const string Outsider = "198.51.100.9";
    private const string TrustedNetwork = "10.0.0.0/8";

    [Fact]
    public async Task Behind_a_trusted_proxy_the_limit_counts_each_real_client()
    {
        await using var host = Host(TrustedNetwork);
        using var client = ConsoleClient.Anonymous(host);
        client.ComeFrom(Ingress);
        await TokenForAsync(client, "203.0.113.7");

        var sameClient = await TokenForAsync(client, "203.0.113.7");
        var otherClient = await TokenForAsync(client, "203.0.113.8");

        Assert.Equal(HttpStatusCode.TooManyRequests, sameClient.Status);
        Assert.Equal(HttpStatusCode.OK, otherClient.Status);
    }

    [Theory]
    [InlineData(TrustedNetwork, Outsider)]
    [InlineData(null, Ingress)]
    public async Task An_untrusted_sender_cannot_pick_its_address(string? knownNetwork, string sender)
    {
        await using var host = Host(knownNetwork);
        using var client = ConsoleClient.Anonymous(host);
        client.ComeFrom(sender);
        await TokenForAsync(client, "203.0.113.7");

        var spoofed = await TokenForAsync(client, "203.0.113.8");

        Assert.Equal(HttpStatusCode.TooManyRequests, spoofed.Status);
    }

    /// <summary>O TLS termina no ingress; sem o esquema repassado, o cookie antiforgery (só HTTPS) seria recusado.</summary>
    [Fact]
    public async Task Behind_a_trusted_proxy_that_ended_TLS_the_antiforgery_token_is_issued()
    {
        await using var host = Host(TrustedNetwork, anonymousPerMinute: 10);
        using var http = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.AntiforgeryToken);
        request.Headers.Add(ClientAddresses.Header, Ingress);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-network")]
    [InlineData("10.0.0.0/33")]
    public async Task A_network_that_is_not_CIDR_keeps_the_api_from_starting(string network)
    {
        await using var host = Host(network);

        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains("ForwardedHeaders:KnownNetworks", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fora do desenvolvimento a API sempre roda atrás do ingress. Sem a rede dele declarada, todo cliente cairia na
    /// partição do proxy no rate limit, e o HSTS nunca sairia, porque o esquema repassado seria ignorado.
    /// </summary>
    [Fact]
    public async Task Outside_development_the_api_does_not_start_without_the_ingress_network()
    {
        await using var production = api.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var error = Assert.Throws<OptionsValidationException>(() => production.CreateClient());

        Assert.Contains("ForwardedHeaders:KnownNetworks", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ASPNETCORE_FORWARDEDHEADERS_ENABLED (chave ForwardedHeaders_Enabled) faz o host ligar o próprio middleware
    /// de forwarded headers e limpar as redes conhecidas: qualquer remetente escolheria o próprio IP e esquema.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public async Task Outside_development_the_api_does_not_start_with_forwarded_headers_opened_by_the_host(string enabled)
    {
        await using var production = api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ForwardedHeaders:KnownNetworks:0", TrustedNetwork);
            builder.UseSetting("ForwardedHeaders_Enabled", enabled);
        });

        var error = Assert.Throws<OptionsValidationException>(() => production.CreateClient());

        Assert.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", error.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> Host(string? knownNetwork, int anonymousPerMinute = 1) =>
        api.WithWebHostBuilder(builder =>
        {
            if (knownNetwork is not null)
            {
                builder.UseSetting("ForwardedHeaders:KnownNetworks:0", knownNetwork);
            }

            builder.UseSetting("RateLimits:AnonymousPerMinute", anonymousPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ClientAddresses>());
        });

    private static Task<ApiResponse> TokenForAsync(ConsoleClient client, string forwardedFor) =>
        client.SendAsync(
            "getAntiforgeryToken", HttpMethod.Get, Paths.AntiforgeryToken, content: null, request => request.Headers.Add("X-Forwarded-For", forwardedFor));
}
