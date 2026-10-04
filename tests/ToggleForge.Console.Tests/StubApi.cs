using System.Net;
using System.Text;
using ToggleForge.Console.Api;
using ToggleForge.Console.Session;

namespace ToggleForge.Console.Tests;

/// <summary>
/// Respostas fixas da API por método e caminho, e o registro do que o console mandou. A API é real nos testes de
/// jornada; aqui ela vira stub para provocar o que lá é difícil: rede fora, proxy no caminho, token recusado.
/// </summary>
internal sealed class StubApi : HttpMessageHandler
{
    public const string AntiforgeryToken = "token-from-the-api";

    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);

    public StubApi() => Answer(HttpMethod.Get, "/api/v1/antiforgery-token", HttpStatusCode.OK, $$"""{"token":"{{AntiforgeryToken}}"}""");

    public List<HttpRequestMessage> Requests { get; } = [];

    public SessionState Session { get; } = new();

    public StubApi Answer(HttpMethod method, string path, HttpStatusCode status, string body, string mediaType = "application/json")
    {
        _responses[Key(method, path)] = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        return this;
    }

    public StubApi Fail(HttpMethod method, string path)
    {
        _responses[Key(method, path)] = () => throw new HttpRequestException("The network is down.");
        return this;
    }

    public ConsoleApi CreateClient() => new(new HttpClient(this) { BaseAddress = new Uri("https://console.toggleforge.test/") }, Session);

    public IEnumerable<HttpRequestMessage> RequestsTo(HttpMethod method, string path) =>
        Requests.Where(request => request.Method == method && request.RequestUri?.AbsolutePath == path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var key = Key(request.Method, request.RequestUri?.AbsolutePath ?? string.Empty);
        return Task.FromResult(_responses.TryGetValue(key, out var respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"type":"about:blank","title":"Not Found","status":404}""", Encoding.UTF8, "application/problem+json") });
    }

    private static string Key(HttpMethod method, string path) => $"{method} {path}";
}
