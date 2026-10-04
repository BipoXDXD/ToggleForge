namespace ToggleForge.Api;

/// <summary>
/// Endurece o Kestrel. Os demais limites e timeouts ficam no padrão, que já cobre slowloris e cabeçalhos
/// gigantes (32 KB de headers, 30 s para recebê-los, 240 bytes/s mínimos no corpo).
/// </summary>
internal static class ApiKestrel
{
    public static WebApplicationBuilder AddToggleForgeKestrel(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // "Server: Kestrel" só ajuda quem procura uma vulnerabilidade conhecida do servidor.
            kestrel.AddServerHeader = false;
        });
        return builder;
    }
}
