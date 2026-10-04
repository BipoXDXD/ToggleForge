using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// A API serve o console na mesma origem (ADR 0009): a página responde às rotas do console, nunca às da API,
/// e sai com uma CSP que só permite script da própria origem.
/// </summary>
public sealed class ConsoleHostingTests(ApiFactory api)
{
    private const string ConsolePageMarker = "<div id=\"app\">";

    [Theory]
    [InlineData("/")]
    [InlineData("/sign-in")]
    [InlineData("/organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev")]
    [InlineData("/apis")]
    [InlineData("/sdks/unity")]
    public async Task Any_route_outside_the_api_answers_the_console_page(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(ConsolePageMarker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/API/v1/does-not-exist")]
    [InlineData("/sdk/v1/does-not-exist")]
    public async Task An_unknown_api_route_never_answers_the_console_page(string path)
    {
        using var member = await SignedInHttpClientAsync();

        using var response = await member.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/sign-in")]
    [InlineData("/index.html")]
    public async Task The_console_page_allows_scripts_only_from_its_own_origin(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(
            "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; " +
            "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("/_framework/blazor.webassembly.js", "text/javascript")]
    [InlineData("/css/app.css", "text/css")]
    public async Task Console_files_are_served_to_anonymous_visitors(string path, string mediaType)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_console_page_only_answers_reads()
    {
        using var member = await SignedInHttpClientAsync();

        using var response = await member.PostAsync("/projects", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Loading_the_console_does_not_spend_the_anonymous_limit()
    {
        const int Limit = 2;
        await using var host = api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimits:AnonymousPerMinute", Limit.ToString(CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ClientAddresses>());
        });
        using var visitor = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = ApiFactory.BaseAddress });
        visitor.DefaultRequestHeaders.Add(ClientAddresses.Header, "203.0.113.9");

        var loads = new List<HttpStatusCode>();
        for (var nth = 0; nth <= Limit; nth++)
        {
            foreach (var path in new[] { "/", "/_framework/blazor.webassembly.js", "/css/app.css" })
            {
                using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);
                loads.Add(response.StatusCode);
            }
        }

        using var antiforgery = await visitor.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);

        Assert.All(loads, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.OK, antiforgery.StatusCode);
    }

    /// <summary>Anônimo, toda rota sem endpoint já é 401; com sessão, o roteamento chega a responder.</summary>
    private async Task<HttpClient> SignedInHttpClientAsync()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        return api.CreateDefaultClient(ApiFactory.BaseAddress, new CookieContainerHandler(member.Cookies));
    }
}
