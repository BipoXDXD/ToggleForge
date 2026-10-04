using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Api.Tests.Support;
using ToggleForge.Application.Access;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Headers que mandam o navegador não adivinhar o tipo do conteúdo, não emoldurar a página noutro site e não
/// vazar a URL (com chaves de projeto e de ambiente) no Referer. Valem para toda resposta, inclusive erro.
/// </summary>
public sealed class SecurityHeadersTests(ApiFactory api)
{
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'";

    [Theory]
    [InlineData(Paths.AntiforgeryToken, HttpStatusCode.OK)]
    [InlineData(Paths.Session, HttpStatusCode.Unauthorized)]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.Unauthorized)]
    [InlineData("/sign-in", HttpStatusCode.OK)]
    [InlineData("/_framework/blazor.webassembly.js", HttpStatusCode.OK)]
    public async Task Every_response_tells_the_browser_not_to_sniff_frame_or_refer(string path, HttpStatusCode status)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertBrowserHardening(response);
    }

    /// <summary>JSON da API não é documento: nada carrega a partir dele, e nenhum site o emoldura.</summary>
    [Theory]
    [InlineData(Paths.AntiforgeryToken)]
    [InlineData(Paths.Session)]
    public async Task Api_responses_forbid_loading_anything_and_being_framed(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(ApiPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    /// <summary>Só a própria origem embute uma resposta (img, script) sem CORS; nenhum outro site a carrega no-cors.</summary>
    [Theory]
    [InlineData(Paths.AntiforgeryToken)]
    [InlineData(Paths.Session)]
    [InlineData("/sign-in")]
    [InlineData("/_framework/blazor.webassembly.js")]
    public async Task Responses_outside_the_sdk_load_only_into_the_same_origin(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Resource-Policy")));
    }

    /// <summary>O jogo lê o snapshot fora do navegador, onde o CORP não vale; a rota do SDK não ganha a restrição.</summary>
    [Fact]
    public async Task The_sdk_route_does_not_restrict_its_loading_origin()
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(Paths.Snapshot, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Cross-Origin-Resource-Policy"));
    }

    /// <summary>Uma janela aberta por outro site não ganha referência ao console (window.opener), e vice-versa.</summary>
    [Theory]
    [InlineData("/sign-in")]
    [InlineData("/index.html")]
    public async Task The_console_page_keeps_its_browsing_context_to_itself(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        using var response = await visitor.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
    }

    /// <summary>O exception handler limpa os headers da resposta antes de escrever o 500; os de segurança voltam.</summary>
    [Fact]
    public async Task An_unexpected_error_keeps_the_headers()
    {
        await using var broken = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddScoped<IMembershipDirectory, BrokenMembershipDirectory>()));
        using var member = await ConsoleClient.SignUpAsync(api);
        using var client = broken.CreateClient(new() { BaseAddress = ApiFactory.BaseAddress, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.Session);
        request.Headers.Add("Cookie", $"{ConsoleClient.SessionCookie}={member.SessionCookieValue()}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertBrowserHardening(response);
        Assert.Equal(ApiPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    private static void AssertBrowserHardening(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
            Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }
}
