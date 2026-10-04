using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Varreduras sobre todas as rotas registradas, para uma rota nova não escapar das regras gerais. STRIDE E3:
/// toda rota da API exige autenticação, exceto uma allowlist explícita. STRIDE S2: toda mutação exige o token
/// antiforgery. Fora de <c>/api</c> e <c>/sdk</c> só existem os arquivos do console, só de leitura (ADR 0009);
/// que eles são públicos, <see cref="ConsoleHostingTests"/> prova.
/// </summary>
public sealed partial class DefaultDenyTests(ApiFactory api)
{
    private static readonly string[] _publicOperations = ["createAccount", "getAntiforgeryToken", "signIn"];

    [Fact]
    public void Only_the_allowlisted_operations_are_public()
    {
        var anonymous = Endpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(OperationId)
            .Order(StringComparer.Ordinal);

        Assert.Equal(_publicOperations, anonymous);
    }

    [Fact]
    public async Task Every_other_route_answers_401_without_credentials()
    {
        using var anonymous = api.CreateAnonymousClient();
        var protectedRoutes = Endpoints()
            .Where(endpoint => !_publicOperations.Contains(OperationId(endpoint)))
            .SelectMany(endpoint => Methods(endpoint).Select(method => (Method: method, Path: SamplePath(endpoint))))
            .ToList();

        Assert.NotEmpty(protectedRoutes);
        foreach (var (method, path) in protectedRoutes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var response = await anonymous.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} answered {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public async Task Every_mutation_rejects_a_session_without_the_antiforgery_token()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        var mutations = Endpoints().Where(endpoint => Methods(endpoint) is [not "GET"]).ToList();

        Assert.NotEmpty(mutations);
        foreach (var endpoint in mutations)
        {
            var operationId = OperationId(endpoint);
            var response = await member.SendWithoutAntiforgeryAsync(
                operationId, new HttpMethod(Methods(endpoint)[0]), SamplePath(endpoint), EmptyBody(endpoint));

            Assert.True(response.Status == HttpStatusCode.BadRequest, $"{operationId} answered {(int)response.Status}.");
            Assert.Equal(["X-XSRF-TOKEN"], response.ErrorSources());
        }
    }

    [Fact]
    public void Outside_the_api_every_route_only_reads()
    {
        var consoleRoutes = AllEndpoints().Where(endpoint => !IsApiRoute(endpoint)).ToList();

        Assert.NotEmpty(consoleRoutes);
        // Sem metadado de método, o endpoint aceita qualquer método: a lista vazia também é falha.
        Assert.All(consoleRoutes, endpoint =>
        {
            Assert.NotEmpty(Methods(endpoint));
            Assert.Subset(new HashSet<string>(["GET", "HEAD"]), Methods(endpoint).ToHashSet());
        });
    }

    [Fact]
    public void Every_route_is_an_operation_of_the_contract()
    {
        var contract = ApiContract.Operations();

        foreach (var endpoint in Endpoints())
        {
            var operationId = OperationId(endpoint);
            Assert.True(contract.TryGetValue(operationId, out var declared), $"{operationId} is not in the contract.");
            Assert.Equal(declared, (Assert.Single(Methods(endpoint)), endpoint.RoutePattern.RawText));
        }
    }

    private List<RouteEndpoint> Endpoints() => AllEndpoints().Where(IsApiRoute).ToList();

    private List<RouteEndpoint> AllEndpoints() =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static bool IsApiRoute(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText is { } route
        && (route.StartsWith("/api/", StringComparison.Ordinal) || route.StartsWith("/sdk/", StringComparison.Ordinal));

    private static string OperationId(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
        ?? throw new Xunit.Sdk.XunitException($"{endpoint.RoutePattern.RawText} has no operation name.");

    private static IReadOnlyList<string> Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

    /// <summary>Um corpo que o binding aceita, para a requisição chegar até a checagem do token.</summary>
    private static StringContent? EmptyBody(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is { ContentTypes: [var mediaType, ..] }
            ? new StringContent("{}", Encoding.UTF8, mediaType)
            : null;

    private static string SamplePath(RouteEndpoint endpoint) =>
        RouteParameter().Replace(
            endpoint.RoutePattern.RawText!,
            match => match.Groups["name"].Value == "organizationId" ? Guid.NewGuid().ToString() : "sample");

    [GeneratedRegex(@"\{(?<name>[^}]+)\}")]
    private static partial Regex RouteParameter();
}
