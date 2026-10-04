using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Canvas D3: 32 KiB por corpo nas rotas de flag. Roda contra a API num Kestrel de verdade, porque o TestServer não
/// implementa o limite de tamanho do corpo e deixaria qualquer corpo passar.
/// </summary>
public sealed class BodyLimitTests(ApiFactory api) : IAsyncLifetime
{
    private const int BodyLimitBytes = 32 * 1024;
    private const string Project = "space-race";

    private BrowserHost? _host;

    private BrowserHost Host => _host ?? throw new InvalidOperationException("The host has not started.");

    public ValueTask InitializeAsync()
    {
        _host = new BrowserHost(api);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_body_over_32_KiB_gets_413()
    {
        using var owner = await OwnerAsync();

        var response = await CreateFlagAsync(owner, CreationOf(BodyLimitBytes + 1));

        AssertPayloadTooLarge(response);
        await AssertNothingSavedAsync(owner);
    }

    [Fact]
    public async Task A_chunked_body_over_32_KiB_gets_413()
    {
        using var owner = await OwnerAsync();

        var response = await CreateFlagAsync(owner, new ChunkedContent(CreationOf(BodyLimitBytes + 1)));

        AssertPayloadTooLarge(response);
        await AssertNothingSavedAsync(owner);
    }

    [Fact]
    public async Task A_patch_over_32_KiB_gets_413()
    {
        using var owner = await OwnerAsync();
        var created = await CreateFlagAsync(owner, CreationOf(bytes: 200));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var patch = """{"enabled":false""" + new string(' ', BodyLimitBytes) + "}";

        var response = await owner.UpdateFlagAsync(Project, "new-shop", created.Header("ETag"), patch);

        AssertPayloadTooLarge(response);
        Assert.True((await owner.GetFlagAsync(Project, "new-shop")).Json["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_body_of_exactly_32_KiB_is_read()
    {
        using var owner = await OwnerAsync();

        var response = await CreateFlagAsync(owner, CreationOf(BodyLimitBytes));

        Assert.Equal(HttpStatusCode.Created, response.Status);
    }

    [Fact]
    public async Task The_413_problem_says_only_the_limit()
    {
        using var owner = await OwnerAsync();

        var response = await CreateFlagAsync(owner, CreationOf(BodyLimitBytes + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.Status);
        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(["detail", "status", "title", "type"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal($"The body is over the limit of {BodyLimitBytes} bytes.", body.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("Exception", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Kestrel", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", response.Body, StringComparison.Ordinal);
    }

    /// <summary>A conta nasce pelo TestServer, no mesmo banco, e entra pelo Kestrel.</summary>
    private async Task<ConsoleClient> OwnerAsync()
    {
        using var signedUp = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await signedUp.CreateProjectAsync(Project)).Status);
        return await ConsoleClient.SignInAsync(Host, signedUp);
    }

    private static Task<ApiResponse> CreateFlagAsync(ConsoleClient owner, string json) =>
        CreateFlagAsync(owner, new StringContent(json, Encoding.UTF8, "application/json"));

    private static Task<ApiResponse> CreateFlagAsync(ConsoleClient owner, HttpContent content) =>
        owner.SendAsync("createFlag", HttpMethod.Post, $"{Paths.Environment(owner.OrganizationId, Project, "dev")}/draft/flags", content);

    /// <summary>Uma criação válida de flag booleana com exatamente <paramref name="bytes"/> bytes, acolchoada com espaços.</summary>
    private static string CreationOf(int bytes)
    {
        const string Creation = """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":100}""";
        return Creation[..^1] + new string(' ', bytes - Creation.Length) + "}";
    }

    private static void AssertPayloadTooLarge(ApiResponse response)
    {
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.Status);
        Assert.Equal("application/problem+json", response.Message.Content.Headers.ContentType?.MediaType);
        Assert.Equal("/problems/payload-too-large", response.ProblemType);
    }

    private async Task AssertNothingSavedAsync(ConsoleClient owner)
    {
        Assert.Empty((await owner.GetDraftAsync(Project)).Json["flags"]!.AsArray());
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal) && entry.Contains("Request body too large", StringComparison.Ordinal));
    }

    /// <summary>Sem tamanho conhecido, o HttpClient manda o corpo em <c>Transfer-Encoding: chunked</c>.</summary>
    private sealed class ChunkedContent : HttpContent
    {
        private readonly byte[] _body;

        public ChunkedContent(string json)
        {
            _body = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
