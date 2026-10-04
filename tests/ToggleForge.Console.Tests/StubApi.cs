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

    private readonly Dictionary<string, List<Func<HttpResponseMessage>>> _responses = new(StringComparer.Ordinal);
    private readonly Dictionary<HttpRequestMessage, string> _bodies = [];

    public StubApi() => Answer(HttpMethod.Get, "/api/v1/antiforgery-token", HttpStatusCode.OK, $$"""{"token":"{{AntiforgeryToken}}"}""");

    public List<HttpRequestMessage> Requests { get; } = [];

    public SessionState Session { get; } = new();

    /// <summary>Troca a resposta da rota; vale para todas as chamadas seguintes.</summary>
    public StubApi Answer(
        HttpMethod method, string path, HttpStatusCode status, string body, string mediaType = "application/json", string? eTag = null)
    {
        _responses[Key(method, path)] = [Respond(status, body, mediaType, eTag)];
        return this;
    }

    /// <summary>Acrescenta uma resposta depois das já combinadas: a última se repete dali em diante.</summary>
    public StubApi ThenAnswer(
        HttpMethod method, string path, HttpStatusCode status, string body, string mediaType = "application/json", string? eTag = null)
    {
        _responses[Key(method, path)].Add(Respond(status, body, mediaType, eTag));
        return this;
    }

    public StubApi Fail(HttpMethod method, string path)
    {
        _responses[Key(method, path)] = [() => throw new HttpRequestException("The network is down.")];
        return this;
    }

    public string BodyOf(HttpRequestMessage request) => _bodies.GetValueOrDefault(request, string.Empty);

    public ConsoleApi CreateClient() => new(new HttpClient(this) { BaseAddress = new Uri("https://console.toggleforge.test/") }, Session);

    public IEnumerable<HttpRequestMessage> RequestsTo(HttpMethod method, string path) =>
        Requests.Where(request => request.Method == method && request.RequestUri?.AbsolutePath == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            _bodies[request] = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var key = Key(request.Method, request.RequestUri?.AbsolutePath ?? string.Empty);
        if (!_responses.TryGetValue(key, out var answers))
        {
            return Respond(HttpStatusCode.NotFound, """{"type":"about:blank","title":"Not Found","status":404}""", "application/problem+json", eTag: null)();
        }

        var respond = answers[0];
        if (answers.Count > 1)
        {
            answers.RemoveAt(0);
        }

        return respond();
    }

    private static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body, string mediaType, string? eTag) => () =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        if (eTag is not null)
        {
            response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(eTag);
        }

        return response;
    };

    private static string Key(HttpMethod method, string path) => $"{method} {path}";
}
