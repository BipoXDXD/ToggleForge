using System.ComponentModel.DataAnnotations;

namespace ToggleForge.Api.Http;

/// <summary>
/// Por quanto tempo o navegador só fala HTTPS com a API depois de uma resposta com HSTS. Configuração, não
/// constante: o primeiro deploy usa um dia, para poder voltar atrás, e sobe para um ano depois que o HTTPS se
/// provar estável (ADR 0012).
/// </summary>
internal sealed class StrictTransportSecuritySettings
{
    public const string Section = "StrictTransportSecurity";

    /// <summary>De 1 segundo a 2 anos; zero desligaria o HSTS em silêncio, e mais de 2 anos não tem volta prática.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "730.00:00:00")]
    public TimeSpan MaxAge { get; set; }
}
