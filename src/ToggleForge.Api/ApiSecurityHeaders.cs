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

    /// <summary>
    /// Um ano, o valor comum depois que o HTTPS está estável; o TLS termina no ingress gerenciado desde o primeiro
    /// deploy. Sem includeSubDomains nem preload: os dois prendem domínios que esta API não controla e são difíceis
    /// de desfazer.
    /// </summary>
    private static readonly TimeSpan _strictTransportSecurityMaxAge = TimeSpan.FromDays(365);

    public static IServiceCollection AddToggleForgeStrictTransportSecurity(this IServiceCollection services) =>
        services.AddHsts(options => options.MaxAge = _strictTransportSecurityMaxAge);

    /// <summary>
    /// HSTS só fora do desenvolvimento: o navegador guarda a política, e o localhost a herdaria. Depende do esquema
    /// repassado pelo ingress, então roda depois dos forwarded headers.
    /// </summary>
    public static WebApplication UseToggleForgeStrictTransportSecurity(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        return app;
    }

    /// <summary>
    /// Escreve os headers quando a resposta começa, não quando a requisição chega: o exception handler limpa os
    /// headers antes do 500, e o endpoint do console já terá posto a própria CSP.
    /// </summary>
    public static IApplicationBuilder UseToggleForgeSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                AddTo(context.Response.Headers);
                return Task.CompletedTask;
            });
            return next(context);
        });

    private static void AddTo(IHeaderDictionary headers)
    {
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers[ReferrerPolicy] = "no-referrer";
        if (!headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
        {
            headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
        }
    }
}
