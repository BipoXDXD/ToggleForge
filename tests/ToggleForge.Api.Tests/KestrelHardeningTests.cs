using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O que só o Kestrel faz, por isso contra a API num Kestrel de verdade (o TestServer não aplica os limites do
/// servidor nem escreve o header <c>Server</c>).
/// </summary>
public sealed class KestrelHardeningTests(ApiFactory api) : IAsyncLifetime
{
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
}
