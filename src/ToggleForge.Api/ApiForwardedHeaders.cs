using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace ToggleForge.Api;

/// <summary>
/// Atrás do ingress, o IP da conexão é o do proxy e o HTTPS termina nele. Só proxies das redes declaradas em
/// <c>ForwardedHeaders:KnownNetworks</c> (além do loopback) dizem o IP e o esquema do cliente; de qualquer
/// outro, os headers são ignorados, para ninguém escolher o próprio IP e escapar do rate limit (ADR 0006).
/// </summary>
internal static class ApiForwardedHeaders
{
    public const string Section = "ForwardedHeaders";

    /// <summary>A chave que ASPNETCORE_FORWARDEDHEADERS_ENABLED (ou DOTNET_FORWARDEDHEADERS_ENABLED) vira na configuração.</summary>
    private const string HostForwardedHeadersSwitch = "ForwardedHeaders_Enabled";

    public static IServiceCollection AddToggleForgeForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<TrustedProxies>()
            .BindConfiguration(Section)
            .Validate(
                proxies => proxies.KnownNetworks.All(network => IPNetwork.TryParse(network, out _)),
                $"{Section}:KnownNetworks must list networks in CIDR notation, such as 10.0.0.0/8.")
            // Fora do desenvolvimento a API sempre está atrás do ingress: sem a rede dele, o rate limit juntaria
            // todos os clientes no IP do proxy e o HSTS nunca sairia, porque o esquema repassado seria ignorado.
            .Validate<IHostEnvironment>(
                (proxies, environment) => environment.IsDevelopment() || proxies.KnownNetworks.Count > 0,
                $"Set {Section}:KnownNetworks to the network of the ingress, such as 10.0.0.0/8.")
            // A variável faz o host ligar o próprio middleware de forwarded headers e limpar as redes conhecidas
            // (Learn, "Kestrel security considerations"): todo remetente passaria a escolher o próprio IP e esquema.
            // O host só reconhece o literal "true", sem diferenciar maiúsculas; "1" ou "yes" ele ignora, e aqui também.
            .Validate<IHostEnvironment, IConfiguration>(
                (_, environment, configuration) => environment.IsDevelopment() || !HostOpensForwardedHeaders(configuration),
                $"Unset ASPNETCORE_FORWARDEDHEADERS_ENABLED ({HostForwardedHeadersSwitch}): it trusts forwarded headers from any sender.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<TrustedProxies>>((options, proxies) =>
        {
            // Proto também: o cookie antiforgery exige HTTPS, e o TLS termina no ingress.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in proxies.Value.KnownNetworks)
            {
                options.KnownIPNetworks.Add(IPNetwork.Parse(network));
            }
        });
        return services;
    }

    private static bool HostOpensForwardedHeaders(IConfiguration configuration) =>
        string.Equals(configuration[HostForwardedHeadersSwitch], "true", StringComparison.OrdinalIgnoreCase);

    private sealed class TrustedProxies
    {
        public List<string> KnownNetworks { get; } = [];
    }
}
