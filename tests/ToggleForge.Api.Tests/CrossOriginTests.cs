using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O console está na mesma origem (ADR 0009), então a API não libera CORS para ninguém: um site alheio não lê
/// resposta nenhuma, nem com o cookie do membro. Liberar uma origem passa a ser decisão explícita.
/// </summary>
public sealed class CrossOriginTests(ApiFactory api)
{
    private const string OtherSite = "https://evil.example";

    [Fact]
    public async Task Another_site_cannot_read_an_api_response()
    {
        using var visitor = api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.AntiforgeryToken);
        request.Headers.Add("Origin", OtherSite);

        using var response = await visitor.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Another_site_gets_no_preflight_approval_for_a_mutation()
    {
        using var visitor = api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, Paths.Session);
        request.Headers.Add("Origin", OtherSite);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-xsrf-token");

        using var response = await visitor.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
    }

    /// <summary>O caso que importa: o navegador de um membro logado, levado a outro site, manda o cookie junto.</summary>
    [Fact]
    public async Task Another_site_cannot_read_a_signed_in_members_session()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        using var browser = api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.Session);
        request.Headers.Add("Origin", OtherSite);
        request.Headers.Add("Cookie", $"{ConsoleClient.SessionCookie}={member.SessionCookieValue()}");

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers, header => header.Key.StartsWith("Access-Control-Allow-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_preflight_for_the_session_from_another_site_is_not_approved()
    {
        using var visitor = api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, Paths.Session);
        request.Headers.Add("Origin", OtherSite);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await visitor.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.IsSuccessStatusCode, $"The preflight answered {(int)response.StatusCode}.");
        Assert.DoesNotContain(response.Headers, header => header.Key.StartsWith("Access-Control-Allow-", StringComparison.OrdinalIgnoreCase));
    }
}
