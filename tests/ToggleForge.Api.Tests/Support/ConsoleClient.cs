using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Um membro usando o console: guarda o cookie de sessão, manda o token antiforgery nas mutações e confere
/// cada resposta contra o contrato.
/// </summary>
public sealed class ConsoleClient : IDisposable
{
    public const string Password = "correct horse battery staple";
    public const string SessionCookie = "__Host-ToggleForge";

    private readonly HttpClient _http;
    private string? _antiforgeryToken;

    private ConsoleClient(WebApplicationFactory<Program> api)
    {
        _http = api.CreateDefaultClient(ApiFactory.BaseAddress, new CookieContainerHandler(Cookies));
    }

    public CookieContainer Cookies { get; } = new();

    public string Email { get; private set; } = string.Empty;

    public Guid MemberId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public static ConsoleClient Anonymous(WebApplicationFactory<Program> api) => new(api);

    /// <summary>Cria a conta com uma organização própria, da qual o membro é owner, e já entra.</summary>
    public static async Task<ConsoleClient> SignUpAsync(WebApplicationFactory<Program> api)
    {
        var client = new ConsoleClient(api);
        var email = $"member-{Guid.NewGuid():N}@toggleforge.test";
        var created = await client.SendAsync("createAccount", HttpMethod.Post, Paths.Accounts, new
        {
            email,
            password = Password,
            organizationName = "Studio " + email[7..15],
        });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        client.Email = email;
        client.MemberId = Guid.Parse(created.Json["member"]!["id"]!.GetValue<string>());
        client.OrganizationId = Guid.Parse(created.Json["organizations"]![0]!["organizationId"]!.GetValue<string>());
        client._antiforgeryToken = null;
        return client;
    }

    public string? SessionCookieValue() => Cookies.GetCookies(ApiFactory.BaseAddress)[SessionCookie]?.Value;

    public async Task<ApiResponse> SignInAsync(string email, string password)
    {
        var response = await SendAsync("signIn", HttpMethod.Post, Paths.Session, new { email, password });
        _antiforgeryToken = null;
        return response;
    }

    public async Task<ApiResponse> SignOutAsync()
    {
        var response = await SendAsync("signOut", HttpMethod.Delete, Paths.Session);
        _antiforgeryToken = null;
        return response;
    }

    public Task<ApiResponse> CreateProjectAsync(string key, Guid? organizationId = null) =>
        SendAsync("createProject", HttpMethod.Post, Paths.Projects(organizationId ?? OrganizationId), new { key, name = $"Project {key}" });

    public Task<ApiResponse> GetProjectAsync(string key, Guid? organizationId = null) =>
        SendAsync("getProject", HttpMethod.Get, Paths.Project(organizationId ?? OrganizationId, key));

    public Task<ApiResponse> GetDraftAsync(string projectKey, string environmentKey = "dev", Guid? organizationId = null) =>
        SendAsync("getDraft", HttpMethod.Get, $"{Paths.Environment(organizationId ?? OrganizationId, projectKey, environmentKey)}/draft");

    public Task<ApiResponse> CreateFlagAsync(
        string projectKey, string flagKey, int rolloutBasisPoints = 1_000, string environmentKey = "dev", Guid? organizationId = null) =>
        SendAsync(
            "createFlag",
            HttpMethod.Post,
            $"{Paths.Environment(organizationId ?? OrganizationId, projectKey, environmentKey)}/draft/flags",
            new { key = flagKey, enabled = true, onValue = true, offValue = false, rolloutBasisPoints });

    public Task<ApiResponse> PublishAsync(
        string projectKey,
        long draftRevision,
        string idempotencyKey,
        string? summary = null,
        string environmentKey = "dev",
        Guid? organizationId = null) =>
        SendAsync(
            "publishDraft",
            HttpMethod.Post,
            $"{Paths.Environment(organizationId ?? OrganizationId, projectKey, environmentKey)}/publications",
            summary is null ? new { draftRevision } : new { draftRevision, summary },
            request => request.Headers.Add("Idempotency-Key", idempotencyKey));

    public Task<ApiResponse> CreateCredentialAsync(string projectKey, string environmentKey = "dev", Guid? organizationId = null) =>
        SendAsync(
            "createCredential",
            HttpMethod.Post,
            $"{Paths.Environment(organizationId ?? OrganizationId, projectKey, environmentKey)}/credentials",
            new { name = "Unity build" });

    /// <summary>Projeto com uma flag publicada em <c>dev</c>; devolve a revisão publicada.</summary>
    public async Task<long> PublishedProjectAsync(string projectKey, string flagKey = "new-shop")
    {
        Assert.Equal(HttpStatusCode.Created, (await CreateProjectAsync(projectKey)).Status);
        var flag = await CreateFlagAsync(projectKey, flagKey);
        Assert.Equal(HttpStatusCode.Created, flag.Status);
        var revision = Revision(flag);
        Assert.Equal(HttpStatusCode.Created, (await PublishAsync(projectKey, revision, NewIdempotencyKey())).Status);
        return revision;
    }

    public static long Revision(ApiResponse response) => long.Parse(response.Header("ETag")!.Trim('"'), System.Globalization.CultureInfo.InvariantCulture);

    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    /// <summary>Envia um corpo JSON montado à mão, para os casos que um objeto anônimo não consegue expressar.</summary>
    public Task<ApiResponse> SendRawAsync(
        string operationId, HttpMethod method, string path, string json, string mediaType = "application/json") =>
        SendAsync(operationId, method, path, new StringContent(json, Encoding.UTF8, mediaType));

    public Task<ApiResponse> SendAsync(
        string operationId, HttpMethod method, string path, object? body = null, Action<HttpRequestMessage>? configure = null) =>
        SendAsync(operationId, method, path, body is null or HttpContent ? body as HttpContent : JsonContent.Create(body), configure);

    public async Task<ApiResponse> SendAsync(
        string operationId, HttpMethod method, string path, HttpContent? content, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-XSRF-TOKEN", await AntiforgeryTokenAsync());
        }

        configure?.Invoke(request);
        return await ReceiveAsync(operationId, await _http.SendAsync(request, TestContext.Current.CancellationToken));
    }

    public async Task<ApiResponse> SendWithoutAntiforgeryAsync(string operationId, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        return await ReceiveAsync(operationId, await _http.SendAsync(request, TestContext.Current.CancellationToken));
    }

    public void Dispose() => _http.Dispose();

    private static async Task<ApiResponse> ReceiveAsync(string operationId, HttpResponseMessage message)
    {
        var body = await message.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        ApiContract.Verify(operationId, message, body);
        return new ApiResponse(message.StatusCode, message, body);
    }

    /// <summary>O token é preso à sessão, então é buscado de novo depois de entrar ou sair.</summary>
    private async Task<string> AntiforgeryTokenAsync()
    {
        if (_antiforgeryToken is null)
        {
            var response = await ReceiveAsync(
                "getAntiforgeryToken", await _http.GetAsync(Paths.AntiforgeryToken, TestContext.Current.CancellationToken));
            _antiforgeryToken = response.Json["token"]!.GetValue<string>();
        }

        return _antiforgeryToken;
    }
}
