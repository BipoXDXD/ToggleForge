using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>STRIDE E3: toda rota registrada exige autenticação, exceto uma allowlist explícita.</summary>
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

    private List<RouteEndpoint> Endpoints() =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static string OperationId(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
        ?? throw new Xunit.Sdk.XunitException($"{endpoint.RoutePattern.RawText} has no operation name.");

    private static IReadOnlyList<string> Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

    private static string SamplePath(RouteEndpoint endpoint) =>
        RouteParameter().Replace(
            endpoint.RoutePattern.RawText!,
            match => match.Groups["name"].Value == "organizationId" ? Guid.NewGuid().ToString() : "sample");

    [GeneratedRegex(@"\{(?<name>[^}]+)\}")]
    private static partial Regex RouteParameter();
}
