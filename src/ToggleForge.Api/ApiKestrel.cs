namespace ToggleForge.Api;

/// <summary>
/// Endurece o Kestrel. Os demais limites e timeouts ficam no padrão, que já cobre slowloris e cabeçalhos
/// gigantes (32 KB de headers, 30 s para recebê-los, 240 bytes/s mínimos no corpo).
/// </summary>
internal static class ApiKestrel
{
    /// <summary>
    /// Teto de todo corpo de requisição. O padrão do Kestrel (30 MB) seria lido inteiro pelo System.Text.Json; a
    /// maior entrada legítima, a flag JSON, cabe em 32 KiB (ADR 0011), e o resto da API fica muito abaixo disso.
    /// </summary>
    public const int MaxRequestBodyBytes = 32 * 1024;

    public static WebApplicationBuilder AddToggleForgeKestrel(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // "Server: Kestrel" só ajuda quem procura uma vulnerabilidade conhecida do servidor.
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });
        return builder;
    }
}
