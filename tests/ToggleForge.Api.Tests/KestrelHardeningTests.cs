using System.Net;
using System.Net.Http.Json;
using System.Text;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O que só o Kestrel faz, por isso contra a API num Kestrel de verdade (o TestServer não aplica os limites do
/// servidor nem escreve o header <c>Server</c>).
/// </summary>
public sealed class KestrelHardeningTests(ApiFactory api) : IAsyncLifetime
{
    private const int BodyLimitBytes = 32 * 1024;

    private BrowserHost? _host;
    private HttpClient? _http;

    private HttpClient Http => _http ?? throw new InvalidOperationException("The host has not started.");

    public ValueTask InitializeAsync()
    {
        _host = new BrowserHost(api);
        _http = _host.CreateHttpClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>Nome e versão do servidor só ajudam quem procura uma vulnerabilidade conhecida dele.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData(Paths.AntiforgeryToken)]
    [InlineData(Paths.Snapshot)]
    public async Task No_response_names_the_server_software(string path)
    {
        using var response = await Http.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Server"), $"{path} sent Server: {string.Join(", ", response.Headers.Server)}");
        Assert.False(response.Headers.Contains("X-Powered-By"), $"{path} sent X-Powered-By.");
    }

    [Fact]
    public async Task A_body_of_exactly_32_KiB_is_read()
    {
        using var response = await CreateAccountAsync(BodyLimitBytes);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ApiContract.Verify("createAccount", response, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// O padrão do Kestrel são 30 MB por corpo, que o System.Text.Json leria inteiro; a maior entrada legítima, a
    /// flag JSON, cabe em 32 KiB (ADR 0011). O binding do corpo para na leitura e responde 413, que as status code
    /// pages vestem de Problem Details.
    /// </summary>
    [Fact]
    public async Task A_body_over_32_KiB_is_refused_before_anything_is_created()
    {
        var email = $"member-{Guid.NewGuid():N}@toggleforge.test";

        using var oversized = await CreateAccountAsync(BodyLimitBytes + 1, email);
        using var retry = await CreateAccountAsync(bodyBytes: null, email);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        ApiContract.Verify("createAccount", oversized, await oversized.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    /// <param name="bodyBytes">Tamanho exato do corpo, completado com espaços depois do JSON; null manda o JSON compacto.</param>
    private async Task<HttpResponseMessage> CreateAccountAsync(int? bodyBytes, string? email = null)
    {
        var token = await AntiforgeryTokenAsync();
        var json = $$"""{"email":"{{email ?? $"member-{Guid.NewGuid():N}@toggleforge.test"}}","password":"{{ConsoleClient.Password}}","organizationName":"Studio"}""";
        var body = bodyBytes is { } size ? json.PadRight(size) : json;
        Assert.Equal(bodyBytes ?? body.Length, Encoding.UTF8.GetByteCount(body));

        using var request = new HttpRequestMessage(HttpMethod.Post, Paths.Accounts)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> AntiforgeryTokenAsync()
    {
        var response = await Http.GetFromJsonAsync<AntiforgeryToken>(Paths.AntiforgeryToken, TestContext.Current.CancellationToken);
        return response?.Token ?? throw new InvalidOperationException("The API answered no antiforgery token.");
    }

    private sealed record AntiforgeryToken(string Token);
}
