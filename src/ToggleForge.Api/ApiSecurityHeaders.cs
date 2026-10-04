using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ToggleForge.Api;

/// <summary>
/// Headers que endurecem o navegador em toda resposta (OWASP REST Security Cheat Sheet): sem adivinhar o tipo
/// do conteúdo, sem moldura noutro site e sem Referer, que levaria a URL com chaves de projeto e ambiente.
/// </summary>
internal static class ApiSecurityHeaders
{
    /// <summary>JSON da API não é documento: nada carrega a partir dele. O console troca por <see cref="ApiConsole.ContentSecurityPolicy"/>.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    private const string ReferrerPolicy = "Referrer-Policy";
    private const string PermissionsPolicy = "Permissions-Policy";
    private const string CrossOriginResourcePolicy = "Cross-Origin-Resource-Policy";

    /// <summary>O console não usa nenhum desses recursos; negá-los tira de um script injetado o acesso a eles.</summary>
    private const string DeniedBrowserFeatures = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    /// <summary>
    /// O CORP só vale para cargas no-cors (img, script, iframe de outra origem); um fetch com CORS não passa por
    /// ele. O SDK fica de fora porque quem o lê é o jogo, fora do navegador ou num build WebGL de outra origem, e
    /// a restrição ali não protege nada que a credencial já não proteja.
    /// </summary>
    private static readonly PathString _sdkPath = new("/sdk");

    /// <summary>
    /// Um ano, o valor comum depois que o HTTPS está estável; o TLS termina no ingress gerenciado desde o primeiro
    /// deploy. Sem includeSubDomains nem preload: os dois prendem domínios que esta API não controla e são difíceis
    /// de desfazer.
    /// </summary>
    private static readonly TimeSpan _strictTransportSecurityMaxAge = TimeSpan.FromDays(365);

    public static IServiceCollection AddToggleForgeStrictTransportSecurity(this IServiceCollection services) =>
        services.AddHsts(options => options.MaxAge = _strictTransportSecurityMaxAge);

    /// <summary>
    /// Escreve os headers quando a resposta começa, não quando a requisição chega: o exception handler limpa os
    /// headers antes do 500, e o endpoint do console já terá posto a própria CSP. Roda depois dos forwarded
    /// headers, porque o HSTS depende do esquema repassado pelo ingress.
    /// </summary>
    public static WebApplication UseToggleForgeSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // HSTS só fora do desenvolvimento: o navegador guarda a política, e o localhost a herdaria.
        var hsts = app.Environment.IsDevelopment() ? null : app.Services.GetRequiredService<IOptions<HstsOptions>>().Value;
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                AddTo(context.Request, context.Response.Headers);
                if (hsts is not null && PinsHttps(context.Request, hsts))
                {
                    context.Response.Headers.StrictTransportSecurity = $"max-age={(long)hsts.MaxAge.TotalSeconds}";
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
        return app;
    }

    /// <summary>Como o <c>HstsMiddleware</c>: só em HTTPS e nunca para os hosts excluídos (loopback, por padrão).</summary>
    private static bool PinsHttps(HttpRequest request, HstsOptions hsts) =>
        request.IsHttps && !hsts.ExcludedHosts.Contains(request.Host.Host, StringComparer.OrdinalIgnoreCase);

    private static void AddTo(HttpRequest request, IHeaderDictionary headers)
    {
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers[ReferrerPolicy] = "no-referrer";
        headers[PermissionsPolicy] = DeniedBrowserFeatures;
        if (!request.Path.StartsWithSegments(_sdkPath))
        {
            headers[CrossOriginResourcePolicy] = "same-origin";
        }

        if (!headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
        {
            headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
        }
    }
}
