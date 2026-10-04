using System.Globalization;
using Microsoft.AspNetCore.Routing;

namespace ToggleForge.Api;

/// <summary>
/// Serve o console Blazor WebAssembly na mesma origem da API (ADR 0009). Os arquivos são públicos e ficam
/// fora do rate limit: não carregam dado nenhum, e um carregamento do runtime são dezenas de arquivos, o que
/// esgotaria o teto anônimo de quem ainda não entrou.
/// </summary>
internal static class ApiConsole
{
    /// <summary>
    /// Script só da própria origem; <c>wasm-unsafe-eval</c> é o que o runtime .NET exige para compilar o
    /// WebAssembly. O index.html não tem script nem estilo inline.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string ConsoleRoute = "consoleroute";

    public static IServiceCollection AddToggleForgeConsole(this IServiceCollection services) =>
        services.Configure<RouteOptions>(options => options.SetParameterPolicy<ConsoleRouteConstraint>(ConsoleRoute));

    public static WebApplication MapToggleForgeConsole(this WebApplication app)
    {
        app.MapStaticAssets().AllowAnonymous().DisableRateLimiting();
        app.MapFallbackToFile($"{{*path:nonfile:{ConsoleRoute}}}", "index.html")
            .AllowAnonymous()
            .DisableRateLimiting()
            .Add(endpoint =>
            {
                var serveIndex = endpoint.RequestDelegate!;
                endpoint.RequestDelegate = context =>
                {
                    context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
                    return serveIndex(context);
                };
            });
        return app;
    }

    /// <summary>
    /// Rotas do console são tudo fora de <c>/api</c> e <c>/sdk</c>: uma rota desconhecida da API continua 404
    /// em Problem Details, nunca a página do console. O roteamento ignora maiúsculas, então a comparação também.
    /// </summary>
    private sealed class ConsoleRouteConstraint : IRouteConstraint
    {
        private static readonly string[] _apiPrefixes = ["api", "sdk"];

        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        {
            var path = values.TryGetValue(routeKey, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
            return string.IsNullOrEmpty(path) || !_apiPrefixes.Any(prefix => IsUnder(path, prefix));
        }

        private static bool IsUnder(string path, string prefix) =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (path.Length == prefix.Length || path[prefix.Length] == '/');
    }
}
