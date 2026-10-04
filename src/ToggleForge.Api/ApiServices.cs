using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Http;
using ToggleForge.Application.Access;
using ToggleForge.Application.Authoring;
using ToggleForge.Application.Delivery;
using ToggleForge.Infrastructure;

namespace ToggleForge.Api;

internal static class ApiServices
{
    public const string SdkPolicy = "Sdk";

    private const string SessionCookieName = "__Host-ToggleForge";
    private const string AntiforgeryCookieName = "__Host-ToggleForge-Antiforgery";
    private static readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);

    public static IServiceCollection AddToggleForgeApi(this IServiceCollection services, string connectionString)
    {
        services.AddToggleForgeInfrastructure(connectionString);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MemberAccess>();
        services.AddScoped<ProjectService>();
        services.AddScoped<DraftService>();
        services.AddScoped<PublishingService>();
        services.AddScoped<IdempotencyRetention>();
        services.AddScoped<PublicationHistoryService>();
        services.AddScoped<SimulationService>();
        services.AddSingleton<PageTokens>();
        services.AddScoped<CredentialService>();
        services.AddScoped<SnapshotService>();
        services.AddSingleton<SignInTiming>();
        services.AddToggleForgeRateLimiting();
        services.AddToggleForgeForwardedHeaders();
        services.AddToggleForgeStrictTransportSecurity();
        services.AddToggleForgeConsole();

        services.ConfigureHttpJsonOptions(options =>
        {
            // Mass assignment (STRIDE T2): campo desconhecido, repetido ou número em string vira 400.
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.AllowDuplicateProperties = false;
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryFilter.HeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, SdkCredentialHandler>(SdkCredentialHandler.SchemeName, configureOptions: null)
            .AddIdentityCookies();
        services.AddScoped<SessionCookieEvents>();
        services.ConfigureApplicationCookie(ConfigureSessionCookie);

        // Negado por padrão (STRIDE E3): só o que declara AllowAnonymous ou outra política escapa da sessão.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser().Build())
            .AddPolicy(SdkPolicy, policy => policy
                .AddAuthenticationSchemes(SdkCredentialHandler.SchemeName)
                .RequireClaim(SdkCredentialHandler.EnvironmentClaim));
        return services;
    }

    private static void ConfigureSessionCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = SessionCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Duração fixa: renovar o cookie poria Set-Cookie e "no-cache, no-store" em respostas que o contrato
        // declara como cacheáveis pelo próprio cliente.
        options.ExpireTimeSpan = _sessionLifetime;
        options.SlidingExpiration = false;
        options.EventsType = typeof(SessionCookieEvents);
    }
}
